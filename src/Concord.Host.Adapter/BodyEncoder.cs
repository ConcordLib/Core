using System.Reflection;
using System.Reflection.Emit;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Host.Adapter;

/// <summary>
///     Turns a composed body into the ECMA-335 method body the runtime replaces a target with. Labels become byte
///     offsets, operands become metadata tokens, and the exception regions are ordered innermost first.
/// </summary>
public static class BodyEncoder {
    /// <summary>Encodes <paramref name="body" /> into bytes, minting every operand token in the target's module.</summary>
    /// <param name="body">The composed body to encode.</param>
    /// <param name="tokens">The token source for the module the body is written into.</param>
    /// <param name="maxStack">Supplies the <c>maxstack</c> the header declares.</param>
    /// <returns>The encoded bytes with the per-instruction offset table beside them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when one label marks two instructions, an operand has the wrong type, or a value overflows its operand.</exception>
    /// <exception cref="NotSupportedException">Thrown when an opcode's operand kind or a clause kind has no encoding here.</exception>
    public static EncodedBody Encode(ComposedBody body, ITokenSource tokens, IMaxStackSource maxStack) {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(maxStack);

        int count = body.Instructions.Count;
        BlobBuilder code = new BlobBuilder();
        ControlFlowBuilder flow = new ControlFlowBuilder();
        InstructionEncoder il = new InstructionEncoder(code, flow);

        LabelHandle[] boundaries = new LabelHandle[count + 1];
        for (int i = 0; i < boundaries.Length; i++) {
            boundaries[i] = il.DefineLabel();
        }

        Dictionary<Label, int> targets = MapTargets(body);
        int[] offsets = new int[count];
        bool dynamicStack = false;

        for (int i = 0; i < count; i++) {
            il.MarkLabel(boundaries[i]);
            offsets[i] = il.Offset;
            CodeInstruction instruction = body.Instructions[i].Instruction;
            dynamicStack |= instruction.opcode == OpCodes.Localloc;
            Emit(il, instruction, i, targets, boundaries, tokens);
        }

        il.MarkLabel(boundaries[count]);
        AddRegions(flow, body, boundaries, tokens);

        StandaloneSignatureHandle localsSignature = default;
        if (body.Locals.Count > 0) {
            localsSignature = (StandaloneSignatureHandle)MetadataTokens.EntityHandle(tokens.TokenForLocals(LocalSignatureEncoder.Encode(body.Locals, tokens)));
        }

        int stack = maxStack.MaxStackFor(body);
        MethodBodyAttributes attributes = body.InitLocals ? MethodBodyAttributes.InitLocals : MethodBodyAttributes.None;
        BlobBuilder stream = new BlobBuilder();
        int at = new MethodBodyStreamEncoder(stream).AddMethodBody(il, stack, localsSignature, attributes, dynamicStack);
        if (at != 0) {
            throw new InvalidOperationException($"A method body stream holding one body placed it at offset {at} instead of 0.");
        }

        return new EncodedBody(stream.ToArray(), offsets, stack, code.Count);
    }

    /// <summary>Orders exception clauses the way a method body has to list them: innermost region first.</summary>
    /// <param name="clauses">The clauses to order. Not modified.</param>
    /// <returns>Indices into <paramref name="clauses" />, later try starts first, then shorter try ranges, then original order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clauses" /> is <c>null</c>.</exception>
    public static int[] InnermostFirst(IReadOnlyList<ComposedClause> clauses) {
        ArgumentNullException.ThrowIfNull(clauses);

        int[] order = new int[clauses.Count];
        for (int i = 0; i < order.Length; i++) {
            order[i] = i;
        }

        Array.Sort(order, (left, right) => {
            ComposedClause first = clauses[left];
            ComposedClause second = clauses[right];
            int byStart = second.TryStart.CompareTo(first.TryStart);
            if (byStart != 0) {
                return byStart;
            }

            int byEnd = first.TryEnd.CompareTo(second.TryEnd);
            return byEnd != 0 ? byEnd : left.CompareTo(right);
        });

        return order;
    }

    private static Dictionary<Label, int> MapTargets(ComposedBody body) {
        Dictionary<Label, int> targets = new Dictionary<Label, int>();
        for (int i = 0; i < body.Instructions.Count; i++) {
            foreach (Label label in body.Instructions[i].Instruction.labels) {
                if (!targets.TryAdd(label, i)) {
                    throw new ArgumentException($"{label} is attached to instruction {targets[label]} and to instruction {i}, so a branch to it has two destinations.");
                }
            }
        }

        return targets;
    }

    private static void Emit(InstructionEncoder il, CodeInstruction instruction, int index, Dictionary<Label, int> targets, LabelHandle[] boundaries, ITokenSource tokens) {
        OpCode opcode = instruction.opcode;
        ILOpCode code = (ILOpCode)(ushort)opcode.Value;
        object? operand = instruction.operand;

        switch (opcode.OperandType) {
            case OperandType.InlineNone:
                il.OpCode(code);
                return;

            case OperandType.ShortInlineBrTarget:
            case OperandType.InlineBrTarget:
                il.Branch(code.GetLongBranch(), boundaries[TargetOf(operand, index, opcode, targets)]);
                return;

            case OperandType.InlineSwitch: {
                Label[] cases = operand as Label[] ?? throw Bad(index, opcode, operand, "a Label[]");
                SwitchInstructionEncoder table = il.Switch(cases.Length);
                foreach (Label label in cases) {
                    table.Branch(boundaries[TargetOf(label, index, opcode, targets)]);
                }

                return;
            }

            case OperandType.ShortInlineI: {
                long value = Integer(operand, index, opcode);
                if (opcode == OpCodes.Ldc_I4_S) {
                    if (value is < sbyte.MinValue or > sbyte.MaxValue) {
                        throw Range(index, opcode, value, "-128 to 127");
                    }

                    il.OpCode(code);
                    il.CodeBuilder.WriteSByte((sbyte)value);
                    return;
                }

                if (value is < 0 or > byte.MaxValue) {
                    throw Range(index, opcode, value, "0 to 255");
                }

                il.OpCode(code);
                il.CodeBuilder.WriteByte((byte)value);
                return;
            }

            case OperandType.InlineI: {
                long value = Integer(operand, index, opcode);
                if (value is < int.MinValue or > uint.MaxValue) {
                    throw Range(index, opcode, value, "a 32-bit range");
                }

                il.OpCode(code);
                il.CodeBuilder.WriteInt32(unchecked((int)value));
                return;
            }

            case OperandType.InlineI8:
                il.OpCode(code);
                il.CodeBuilder.WriteInt64(operand is long value64 ? value64 : Integer(operand, index, opcode));
                return;

            case OperandType.ShortInlineR:
                il.OpCode(code);
                il.CodeBuilder.WriteSingle(operand is float single ? single : throw Bad(index, opcode, operand, "a float"));
                return;

            case OperandType.InlineR:
                il.OpCode(code);
                il.CodeBuilder.WriteDouble(operand is double real ? real : operand is float widened ? widened : throw Bad(index, opcode, operand, "a double"));
                return;

            case OperandType.InlineString:
                il.OpCode(code);
                il.Token(tokens.TokenFor(operand as string ?? throw Bad(index, opcode, operand, "a string")));
                return;

            case OperandType.InlineType:
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineTok:
                il.OpCode(code);
                il.Token(MemberToken(operand, index, opcode, tokens));
                return;

            case OperandType.ShortInlineVar:
            case OperandType.InlineVar: {
                int slot = SlotOf(operand, index, opcode);
                il.OpCode(code);
                if (opcode.OperandType == OperandType.ShortInlineVar) {
                    if (slot is < 0 or > byte.MaxValue) {
                        throw Range(index, opcode, slot, "0 to 255");
                    }

                    il.CodeBuilder.WriteByte((byte)slot);
                    return;
                }

                if (slot is < 0 or > ushort.MaxValue) {
                    throw Range(index, opcode, slot, "0 to 65535");
                }

                il.CodeBuilder.WriteUInt16((ushort)slot);
                return;
            }

            case OperandType.InlineSig:
                throw new NotSupportedException($"Instruction {index} is '{opcode.Name}', whose operand is a standalone signature. The runtime's token api cannot produce one, so a body carrying it must be refused before it reaches the encoder.");

            default:
                throw new NotSupportedException($"Instruction {index} is '{opcode.Name}', whose operand kind '{opcode.OperandType}' has no encoding.");
        }
    }

    private static void AddRegions(ControlFlowBuilder flow, ComposedBody body, LabelHandle[] boundaries, ITokenSource tokens) {
        foreach (int index in InnermostFirst(body.Clauses)) {
            ComposedClause clause = body.Clauses[index];
            LabelHandle tryStart = Boundary(boundaries, clause.TryStart, index, nameof(clause.TryStart));
            LabelHandle tryEnd = Boundary(boundaries, clause.TryEnd, index, nameof(clause.TryEnd));
            LabelHandle handlerStart = Boundary(boundaries, clause.HandlerStart, index, nameof(clause.HandlerStart));
            LabelHandle handlerEnd = Boundary(boundaries, clause.HandlerEnd, index, nameof(clause.HandlerEnd));

            switch (clause.Kind) {
                case ClauseKind.Catch:
                    Type caught = clause.CatchType ?? throw new NotSupportedException($"Clause {index} is a catch with no catch type.");
                    flow.AddCatchRegion(tryStart, tryEnd, handlerStart, handlerEnd, MetadataTokens.EntityHandle(tokens.TokenFor(caught)));
                    break;

                case ClauseKind.Filter:
                    flow.AddFilterRegion(tryStart, tryEnd, handlerStart, handlerEnd, Boundary(boundaries, clause.FilterStart, index, nameof(clause.FilterStart)));
                    break;

                case ClauseKind.Finally:
                    flow.AddFinallyRegion(tryStart, tryEnd, handlerStart, handlerEnd);
                    break;

                case ClauseKind.Fault:
                    flow.AddFaultRegion(tryStart, tryEnd, handlerStart, handlerEnd);
                    break;

                default:
                    throw new NotSupportedException($"Clause {index} has kind '{clause.Kind}', which has no encoding.");
            }
        }
    }

    private static LabelHandle Boundary(LabelHandle[] boundaries, int index, int clause, string name) {
        if (index < 0 || index >= boundaries.Length) {
            throw new ArgumentOutOfRangeException(nameof(clause), $"Clause {clause} has {name} {index}, outside 0 to {boundaries.Length - 1}.");
        }

        return boundaries[index];
    }

    private static int TargetOf(object? operand, int index, OpCode opcode, Dictionary<Label, int> targets) {
        Label label = operand is Label branch ? branch : throw Bad(index, opcode, operand, "a Label");
        return TargetOf(label, index, opcode, targets);
    }

    private static int TargetOf(Label label, int index, OpCode opcode, Dictionary<Label, int> targets) {
        if (targets.TryGetValue(label, out int target)) {
            return target;
        }

        throw new ArgumentException($"Instruction {index} ('{opcode.Name}') branches to {label}, which is attached to no instruction in the body.");
    }

    private static int MemberToken(object? operand, int index, OpCode opcode, ITokenSource tokens) {
        return operand switch {
            Type type => tokens.TokenFor(type),
            FieldInfo field => tokens.TokenFor(field),
            MethodBase method => tokens.TokenFor(method),
            string literal => tokens.TokenFor(literal),
            _ => throw Bad(index, opcode, operand, "a Type, FieldInfo, MethodBase or string"),
        };
    }

    private static int SlotOf(object? operand, int index, OpCode opcode) {
        return operand switch {
            LocalRef local => local.Index,
            byte slot => slot,
            short slot => slot,
            ushort slot => slot,
            int slot => slot,
            _ => throw Bad(index, opcode, operand, "a LocalRef or an argument index"),
        };
    }

    private static long Integer(object? operand, int index, OpCode opcode) {
        return operand switch {
            sbyte value => value,
            byte value => value,
            short value => value,
            ushort value => value,
            int value => value,
            uint value => value,
            long value => value,
            _ => throw Bad(index, opcode, operand, "an integer"),
        };
    }

    private static ArgumentException Bad(int index, OpCode opcode, object? operand, string expected) {
        return new ArgumentException($"Instruction {index} ('{opcode.Name}') needs {expected} as its operand, not '{operand?.GetType().ToString() ?? "null"}'.");
    }

    private static ArgumentOutOfRangeException Range(int index, OpCode opcode, long value, string allowed) {
        return new ArgumentOutOfRangeException(nameof(value), $"Instruction {index} ('{opcode.Name}') has operand {value}, outside {allowed}.");
    }
}
