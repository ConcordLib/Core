using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;
using OperandType = Mono.Cecil.Cil.OperandType;

namespace Concord.Emit;

internal static class CecilCodeConverter {
    private static readonly Dictionary<string, OpCode> OpCodesByName = BuildOpCodeTable();
    private static readonly Dictionary<string, Mono.Cecil.Cil.OpCode> CecilOpCodesByName = BuildCecilOpCodeTable();

    public static List<CodeInstruction> ToInstructions(MethodDefinition definition, TranspilerContext context) {
        MethodBody body = definition.Body;
        context.ExistingLocalCount = body.Variables.Count;

        AssignLabels(body, context);

        List<CodeInstruction> instructions = new List<CodeInstruction>(body.Instructions.Count);
        Dictionary<Instruction, CodeInstruction> byInstruction = new Dictionary<Instruction, CodeInstruction>(body.Instructions.Count);

        foreach (Instruction instruction in body.Instructions) {
            CodeInstruction converted = new CodeInstruction(MapOpCode(instruction.OpCode), MapOperandToClr(instruction, context));
            if (context.LabelsByInstruction.TryGetValue(instruction, out Label label)) {
                converted.labels.Add(label);
            }

            instructions.Add(converted);
            byInstruction[instruction] = converted;
        }

        AttachExceptionBlocks(body, byInstruction, context);

        return instructions;
    }

    public static void WriteBack(MethodDefinition definition, IReadOnlyList<CodeInstruction> instructions, TranspilerContext context) {
        ModuleDefinition module = definition.Module;
        MethodBody body = definition.Body;

        foreach (Type declaredLocal in context.DeclaredLocals) {
            body.Variables.Add(new VariableDefinition(module.ImportReference(declaredLocal)));
        }

        Dictionary<int, VariableDefinition> localsByIndex = new Dictionary<int, VariableDefinition>();
        foreach (VariableDefinition variable in body.Variables) {
            localsByIndex[variable.Index] = variable;
        }

        List<Instruction> emitted = new List<Instruction>(instructions.Count);
        foreach (CodeInstruction instruction in instructions) {
            emitted.Add(CreateInstruction(instruction, definition, localsByIndex, module));
        }

        Dictionary<Label, Instruction> instructionByLabel = BuildLabelMap(instructions, emitted);
        for (int i = 0; i < instructions.Count; i++) {
            FixupOperand(emitted[i], instructions[i], instructionByLabel);
        }

        ApplyBlocks(definition, emitted, instructions, context);

        body.Instructions.Clear();
        ILProcessor il = body.GetILProcessor();
        foreach (Instruction instruction in emitted) {
            il.Append(instruction);
        }
    }

    private static Dictionary<string, OpCode> BuildOpCodeTable() {
        Dictionary<string, OpCode> table = new Dictionary<string, OpCode>(StringComparer.Ordinal);
        FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (FieldInfo field in fields) {
            if (field.FieldType != typeof(OpCode)) {
                continue;
            }

            OpCode opcode = (OpCode)field.GetValue(null)!;
            if (opcode.Name is not null) {
                table[opcode.Name] = opcode;
            }
        }

        table["ldelem.any"] = OpCodes.Ldelem;
        table["stelem.any"] = OpCodes.Stelem;

        return table;
    }

    private static OpCode MapOpCode(Mono.Cecil.Cil.OpCode source) {
        if (OpCodesByName.TryGetValue(source.Name, out OpCode mapped)) {
            return mapped;
        }

        throw new ConcordEmitException("CONC118", $"Unknown opcode '{source.Name}'.");
    }

    private static Dictionary<string, Mono.Cecil.Cil.OpCode> BuildCecilOpCodeTable() {
        Dictionary<string, Mono.Cecil.Cil.OpCode> table = new Dictionary<string, Mono.Cecil.Cil.OpCode>(StringComparer.Ordinal);
        FieldInfo[] fields = typeof(Mono.Cecil.Cil.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (FieldInfo field in fields) {
            if (field.FieldType != typeof(Mono.Cecil.Cil.OpCode)) {
                continue;
            }

            Mono.Cecil.Cil.OpCode opcode = (Mono.Cecil.Cil.OpCode)field.GetValue(null)!;
            if (opcode.Name is not null) {
                table[opcode.Name] = opcode;
            }
        }

        table["ldelem"] = Mono.Cecil.Cil.OpCodes.Ldelem_Any;
        table["stelem"] = Mono.Cecil.Cil.OpCodes.Stelem_Any;

        return table;
    }

    private static Mono.Cecil.Cil.OpCode MapOpCodeToCecil(OpCode source) {
        if (source.Name is not null && CecilOpCodesByName.TryGetValue(source.Name, out Mono.Cecil.Cil.OpCode mapped)) {
            return mapped;
        }

        throw new ConcordEmitException("CONC118", $"Unknown opcode '{source.Name}'.");
    }

    private static void AssignLabels(MethodBody body, TranspilerContext context) {
        foreach (Instruction instruction in body.Instructions) {
            if (instruction.Operand is Instruction branchTarget) {
                EnsureLabel(branchTarget, context);
            } else if (instruction.Operand is Instruction[] switchTargets) {
                foreach (Instruction switchTarget in switchTargets) {
                    EnsureLabel(switchTarget, context);
                }
            }
        }

        foreach (ExceptionHandler handler in body.ExceptionHandlers) {
            if (handler.TryStart is not null) {
                EnsureLabel(handler.TryStart, context);
            }

            if (handler.TryEnd is not null) {
                EnsureLabel(handler.TryEnd, context);
            }

            if (handler.HandlerStart is not null) {
                EnsureLabel(handler.HandlerStart, context);
            }

            if (handler.HandlerEnd is not null) {
                EnsureLabel(handler.HandlerEnd, context);
            }

            if (handler.FilterStart is not null) {
                EnsureLabel(handler.FilterStart, context);
            }
        }
    }

    private static void EnsureLabel(Instruction target, TranspilerContext context) {
        if (!context.LabelsByInstruction.ContainsKey(target)) {
            context.LabelsByInstruction[target] = context.DefineLabel();
        }
    }

    private static object? MapOperandToClr(Instruction instruction, TranspilerContext context) {
        return instruction.Operand switch {
            null => null,
            Instruction target => context.LabelsByInstruction[target],
            Instruction[] targets => MapSwitchTargets(targets, context),
            VariableDefinition variable => LocalRefFactory.Create(variable.Index, ResolveType(variable.VariableType)),
            ParameterDefinition parameter => MapArgumentOperand(instruction.OpCode.OperandType, parameter),
            CallSite callSite => new CallSiteSignature(callSite),
            TypeReference type => ResolveType(type),
            FieldReference field => ResolveField(field),
            MethodReference method => ResolveMethod(method),
            _ => instruction.Operand,
        };
    }

    private static Label[] MapSwitchTargets(Instruction[] targets, TranspilerContext context) {
        Label[] labels = new Label[targets.Length];
        for (int i = 0; i < targets.Length; i++) {
            labels[i] = context.LabelsByInstruction[targets[i]];
        }

        return labels;
    }

    private static object MapArgumentOperand(OperandType operandType, ParameterDefinition parameter) {
        return operandType == OperandType.ShortInlineArg ? (byte)parameter.Index : parameter.Index;
    }

    private static Type ResolveType(TypeReference typeReference) {
        return typeReference.ResolveReflection() ?? throw new ConcordEmitException("CONC118", $"Could not resolve CLR type for '{typeReference.FullName}'.");
    }

    private static FieldInfo ResolveField(FieldReference fieldReference) {
        return fieldReference.ResolveReflection() ?? throw new ConcordEmitException("CONC118", $"Could not resolve CLR field for '{fieldReference.FullName}'.");
    }

    private static MethodBase ResolveMethod(MethodReference methodReference) {
        return methodReference.ResolveReflection() ?? throw new ConcordEmitException("CONC118", $"Could not resolve CLR method for '{methodReference.FullName}'.");
    }

    private static void AttachExceptionBlocks(MethodBody body, Dictionary<Instruction, CodeInstruction> byInstruction, TranspilerContext context) {
        if (body.ExceptionHandlers.Count == 0) {
            return;
        }

        Instruction lastInstruction = body.Instructions[body.Instructions.Count - 1];
        Dictionary<Instruction, int> indexOf = new Dictionary<Instruction, int>(body.Instructions.Count);
        int index = 0;
        foreach (Instruction instruction in body.Instructions) {
            indexOf[instruction] = index++;
        }

        List<ExceptionEnvelope> envelopes = BuildEnvelopes(body.ExceptionHandlers, indexOf, lastInstruction, body.Instructions.Count);
        List<ExceptionEnvelope> roots = LinkEnvelopes(envelopes);
        roots.Sort((a, b) => a.TryStartIndex.CompareTo(b.TryStartIndex));

        foreach (ExceptionEnvelope root in roots) {
            EmitEnvelope(root, byInstruction, indexOf, context);
        }
    }

    // Cecil lists exception handlers innermost-first, but two handlers can legitimately share the
    // same TryStart while protecting different-sized regions - a try/catch/finally is TWO nested
    // envelopes (the finally's protected range encloses the try body AND the catch handler), not one
    // flat group. Grouping must key on the exact (TryStart, TryEnd) pair - true multi-catch siblings
    // share both; a catch nested inside an enclosing finally does not.
    //
    // HandlerEnd is legally null when a handler runs to the literal end of the method body (no
    // trailing instruction exists to mark where it stops). That envelope's real extent is "one past
    // the last instruction" for containment purposes - using the last real instruction's index here
    // would make it indistinguishable from (and possibly subordinate to) a sibling envelope that
    // genuinely ends at that same last instruction, so the synthetic index must be strictly greater
    // than every real index (instructionCount, never a valid index into body.Instructions).
    private static List<ExceptionEnvelope> BuildEnvelopes(IEnumerable<ExceptionHandler> handlers, Dictionary<Instruction, int> indexOf, Instruction lastInstruction, int instructionCount) {
        Dictionary<(Instruction TryStart, Instruction TryEnd), List<ExceptionHandler>> byRange = new Dictionary<(Instruction, Instruction), List<ExceptionHandler>>();
        List<(Instruction TryStart, Instruction TryEnd)> order = new List<(Instruction, Instruction)>();

        foreach (ExceptionHandler handler in handlers) {
            (Instruction TryStart, Instruction TryEnd) key = (handler.TryStart, handler.TryEnd);
            if (!byRange.TryGetValue(key, out List<ExceptionHandler>? group)) {
                group = new List<ExceptionHandler>();
                byRange[key] = group;
                order.Add(key);
            }

            group.Add(handler);
        }

        List<ExceptionEnvelope> envelopes = new List<ExceptionEnvelope>();
        foreach ((Instruction TryStart, Instruction TryEnd) key in order) {
            List<ExceptionHandler> group = byRange[key];
            group.Sort((a, b) => HandlerOpenIndex(a, indexOf).CompareTo(HandlerOpenIndex(b, indexOf)));

            Instruction endInstruction = key.TryStart;
            int endIndex = -1;
            bool endsAtBodyEnd = false;
            foreach (ExceptionHandler handler in group) {
                bool handlerEndsAtBodyEnd = handler.HandlerEnd is null;
                Instruction handlerEndInstruction = handlerEndsAtBodyEnd ? lastInstruction : handler.HandlerEnd!;
                int handlerEndIndex = handlerEndsAtBodyEnd ? instructionCount : indexOf[handlerEndInstruction];
                if (handlerEndIndex > endIndex) {
                    endIndex = handlerEndIndex;
                    endInstruction = handlerEndInstruction;
                    endsAtBodyEnd = handlerEndsAtBodyEnd;
                }
            }

            envelopes.Add(new ExceptionEnvelope(group, key.TryStart, indexOf[key.TryStart], endInstruction, endIndex, endsAtBodyEnd));
        }

        return envelopes;
    }

    private static int HandlerOpenIndex(ExceptionHandler handler, Dictionary<Instruction, int> indexOf) {
        Instruction openPosition = handler.HandlerType == ExceptionHandlerType.Filter ? handler.FilterStart : handler.HandlerStart;
        return indexOf[openPosition];
    }

    private static List<ExceptionEnvelope> LinkEnvelopes(List<ExceptionEnvelope> envelopes) {
        List<ExceptionEnvelope> roots = new List<ExceptionEnvelope>();

        foreach (ExceptionEnvelope envelope in envelopes) {
            ExceptionEnvelope? parent = null;
            foreach (ExceptionEnvelope candidate in envelopes) {
                if (ReferenceEquals(candidate, envelope)) {
                    continue;
                }

                bool contains = candidate.TryStartIndex <= envelope.TryStartIndex && envelope.EndIndex <= candidate.EndIndex
                    && (candidate.TryStartIndex != envelope.TryStartIndex || candidate.EndIndex != envelope.EndIndex);
                if (!contains) {
                    continue;
                }

                int candidateSpan = candidate.EndIndex - candidate.TryStartIndex;
                int parentSpan = parent is null ? int.MaxValue : parent.EndIndex - parent.TryStartIndex;
                if (parent is null || candidateSpan < parentSpan) {
                    parent = candidate;
                }
            }

            if (parent is null) {
                roots.Add(envelope);
            } else {
                parent.Children.Add(envelope);
            }
        }

        foreach (ExceptionEnvelope envelope in envelopes) {
            envelope.Children.Sort((a, b) => a.TryStartIndex.CompareTo(b.TryStartIndex));
        }

        return roots;
    }

    // Interleaves this envelope's own boundary markers with its children's, in the order a
    // transpiler author calling BeginExceptionBlock/BeginCatchBlock/.../EndExceptionBlock by hand
    // would have to call them: a child whose TryStart ties with one of this envelope's own boundary
    // positions opens strictly after that boundary is emitted (nothing can precede this envelope's
    // own open), and closes entirely - including its own EndExceptionBlock - before this envelope's
    // NEXT boundary is emitted, even when that next boundary lands on the exact same instruction
    // (a finally's handler always encloses its guarded catch, and the catch's close commonly lands
    // on the same instruction the finally's handler begins).
    private static void EmitEnvelope(ExceptionEnvelope envelope, Dictionary<Instruction, CodeInstruction> byInstruction, Dictionary<Instruction, int> indexOf, TranspilerContext context) {
        List<(int Index, Instruction Position, ExceptionBlock Block)> ownPoints = BuildOwnPoints(envelope, indexOf, context);
        int childCursor = 0;

        for (int i = 0; i < ownPoints.Count; i++) {
            (int pointIndex, Instruction position, ExceptionBlock block) = ownPoints[i];

            while (childCursor < envelope.Children.Count && envelope.Children[childCursor].TryStartIndex < pointIndex) {
                EmitEnvelope(envelope.Children[childCursor], byInstruction, indexOf, context);
                childCursor++;
            }

            byInstruction[position].blocks.Add(block);

            while (childCursor < envelope.Children.Count && envelope.Children[childCursor].TryStartIndex == pointIndex) {
                EmitEnvelope(envelope.Children[childCursor], byInstruction, indexOf, context);
                childCursor++;
            }
        }

        if (childCursor < envelope.Children.Count) {
            throw new ConcordEmitException("CONC118", "An exception region's child region was not contained within its parent's own boundaries.");
        }
    }

    private static List<(int Index, Instruction Position, ExceptionBlock Block)> BuildOwnPoints(ExceptionEnvelope envelope, Dictionary<Instruction, int> indexOf, TranspilerContext context) {
        List<(int Index, Instruction Position, ExceptionBlock Block)> points = new List<(int, Instruction, ExceptionBlock)> {
            (envelope.TryStartIndex, envelope.TryStart, new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock)),
        };

        foreach (ExceptionHandler handler in envelope.Handlers) {
            if (handler.HandlerType == ExceptionHandlerType.Filter) {
                points.Add((indexOf[handler.FilterStart], handler.FilterStart, new ExceptionBlock(ExceptionBlockType.BeginExceptFilterBlock)));
                points.Add((indexOf[handler.HandlerStart], handler.HandlerStart, new ExceptionBlock(ExceptionBlockType.BeginCatchBlock)));
            } else if (handler.HandlerType == ExceptionHandlerType.Fault) {
                points.Add((indexOf[handler.HandlerStart], handler.HandlerStart, new ExceptionBlock(ExceptionBlockType.BeginFaultBlock)));
            } else if (handler.HandlerType == ExceptionHandlerType.Finally) {
                points.Add((indexOf[handler.HandlerStart], handler.HandlerStart, new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock)));
            } else {
                Type? catchType = handler.CatchType is null ? null : ResolveType(handler.CatchType);
                points.Add((indexOf[handler.HandlerStart], handler.HandlerStart, new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, catchType)));
            }
        }

        ExceptionBlock endBlock = new ExceptionBlock(ExceptionBlockType.EndExceptionBlock);
        if (envelope.EndsAtBodyEnd) {
            context.EndOfBodyBlocks.Add(endBlock);
        }

        points.Add((envelope.EndIndex, envelope.EndInstruction, endBlock));
        return points;
    }

    private static Dictionary<Label, Instruction> BuildLabelMap(IReadOnlyList<CodeInstruction> instructions, List<Instruction> emitted) {
        Dictionary<Label, Instruction> map = new Dictionary<Label, Instruction>();
        for (int i = 0; i < instructions.Count; i++) {
            foreach (Label label in instructions[i].labels) {
                map[label] = emitted[i];
            }
        }

        return map;
    }

    private static Instruction CreateInstruction(CodeInstruction source, MethodDefinition definition, Dictionary<int, VariableDefinition> localsByIndex, ModuleDefinition module) {
        Mono.Cecil.Cil.OpCode opcode = MapOpCodeToCecil(source.opcode);

        return source.operand switch {
            null => Instruction.Create(opcode),
            Label => Instruction.Create(opcode, Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop)),
            Label[] => Instruction.Create(opcode, Array.Empty<Instruction>()),
            LocalRef local => Instruction.Create(opcode, ResolveLocal(local, localsByIndex, source)),
            CallSiteSignature callSite => Instruction.Create(opcode, (CallSite)callSite.Handle),
            Type type => Instruction.Create(opcode, module.ImportReference(type)),
            FieldInfo field => Instruction.Create(opcode, module.ImportReference(field)),
            ConstructorInfo ctor => Instruction.Create(opcode, module.ImportReference(ctor)),
            MethodInfo method => Instruction.Create(opcode, module.ImportReference(method)),
            string s => Instruction.Create(opcode, s),
            sbyte sb => Instruction.Create(opcode, sb),
            byte b => IsArgumentOperand(opcode) ? Instruction.Create(opcode, ResolveParameter(definition, b)) : Instruction.Create(opcode, b),
            int i32 => IsArgumentOperand(opcode) ? Instruction.Create(opcode, ResolveParameter(definition, i32)) : Instruction.Create(opcode, i32),
            long i64 => Instruction.Create(opcode, i64),
            float f32 => Instruction.Create(opcode, f32),
            double f64 => Instruction.Create(opcode, f64),
            _ => throw new ConcordEmitException("CONC118", $"Unsupported operand '{source.operand}' of type '{source.operand.GetType()}' for opcode '{source.opcode.Name}'."),
        };
    }

    private static bool IsArgumentOperand(Mono.Cecil.Cil.OpCode opcode) {
        return opcode.OperandType == OperandType.ShortInlineArg || opcode.OperandType == OperandType.InlineArg;
    }

    private static ParameterDefinition ResolveParameter(MethodDefinition definition, int rawSlot) {
        if (rawSlot < 0 || rawSlot >= definition.Parameters.Count) {
            throw new ConcordEmitException("CONC118", $"Argument slot {rawSlot} is out of range for a method with {definition.Parameters.Count} parameter(s).");
        }

        return definition.Parameters[rawSlot];
    }

    private static VariableDefinition ResolveLocal(LocalRef local, Dictionary<int, VariableDefinition> localsByIndex, CodeInstruction source) {
        if (localsByIndex.TryGetValue(local.Index, out VariableDefinition? variable)) {
            return variable;
        }

        throw new ConcordEmitException("CONC118", $"Local '{local}' referenced by opcode '{source.opcode.Name}' has no matching variable.");
    }

    private static void FixupOperand(Instruction emitted, CodeInstruction source, Dictionary<Label, Instruction> instructionByLabel) {
        if (source.operand is Label label) {
            emitted.Operand = ResolveLabelTarget(label, instructionByLabel, source);
        } else if (source.operand is Label[] labels) {
            Instruction[] targets = new Instruction[labels.Length];
            for (int i = 0; i < labels.Length; i++) {
                targets[i] = ResolveLabelTarget(labels[i], instructionByLabel, source);
            }

            emitted.Operand = targets;
        }
    }

    private static Instruction ResolveLabelTarget(Label label, Dictionary<Label, Instruction> map, CodeInstruction source) {
        if (map.TryGetValue(label, out Instruction? target)) {
            return target;
        }

        throw new ConcordEmitException("CONC118", $"Label '{label}' referenced by opcode '{source.opcode.Name}' is not attached to any instruction.");
    }

    private static void ApplyBlocks(MethodDefinition definition, List<Instruction> emitted, IReadOnlyList<CodeInstruction> instructions, TranspilerContext context) {
        List<ExceptionFrame> open = new List<ExceptionFrame>();
        definition.Body.ExceptionHandlers.Clear();

        for (int i = 0; i < instructions.Count; i++) {
            foreach (ExceptionBlock block in instructions[i].blocks) {
                ApplyBlock(definition, emitted[i], block, open, i, context);
            }
        }

        if (open.Count > 0) {
            throw new ConcordEmitException("CONC118", $"{open.Count} exception block(s) were opened and never closed.");
        }
    }

    private static void ApplyBlock(MethodDefinition definition, Instruction position, ExceptionBlock block, List<ExceptionFrame> open, int index, TranspilerContext context) {
        if (block.blockType == ExceptionBlockType.BeginExceptionBlock) {
            open.Add(new ExceptionFrame(position));
            return;
        }

        if (open.Count == 0) {
            throw new ConcordEmitException("CONC118", $"Exception block '{block.blockType}' at instruction {index} closes a region that was never opened.");
        }

        ExceptionFrame frame = open[open.Count - 1];

        if (block.blockType == ExceptionBlockType.EndExceptionBlock) {
            if (frame.TryEndPosition is null) {
                throw new ConcordEmitException("CONC118", $"Exception block at instruction {index} closes a try region that opened no handlers.");
            }

            // HandlerEnd is exclusive in Cecil - there is no instruction one past the end of the
            // body, so a handler that legitimately runs to end-of-body was recorded in
            // context.EndOfBodyBlocks (by the same ExceptionBlock object reference) when this stream
            // was produced by ToInstructions. Restore null rather than the carrier instruction the
            // marker happened to be attached to, or the handler is silently truncated by one
            // instruction.
            Instruction? handlerEnd = context.EndOfBodyBlocks.Contains(block) ? null : position;
            CloseCurrentHandler(definition, frame, handlerEnd, index);
            open.RemoveAt(open.Count - 1);
            return;
        }

        if (block.blockType == ExceptionBlockType.BeginExceptFilterBlock) {
            CloseCurrentHandler(definition, frame, position, index);
            ExceptionHandler handler = new ExceptionHandler(ExceptionHandlerType.Filter) {
                TryStart = frame.TryStart,
                FilterStart = position,
            };
            frame.TryEndPosition ??= position;
            handler.TryEnd = frame.TryEndPosition;
            frame.Current = handler;
            frame.CurrentAwaitsHandlerStart = true;
            return;
        }

        if (block.blockType == ExceptionBlockType.BeginCatchBlock && frame.CurrentAwaitsHandlerStart) {
            frame.Current!.HandlerStart = position;
            frame.CurrentAwaitsHandlerStart = false;
            return;
        }

        CloseCurrentHandler(definition, frame, position, index);

        ExceptionHandlerType handlerType = block.blockType switch {
            ExceptionBlockType.BeginCatchBlock => ExceptionHandlerType.Catch,
            ExceptionBlockType.BeginFaultBlock => ExceptionHandlerType.Fault,
            ExceptionBlockType.BeginFinallyBlock => ExceptionHandlerType.Finally,
            _ => throw new ConcordEmitException("CONC118", $"Unexpected exception block '{block.blockType}' at instruction {index}."),
        };

        ExceptionHandler newHandler = new ExceptionHandler(handlerType) {
            TryStart = frame.TryStart,
            HandlerStart = position,
        };
        frame.TryEndPosition ??= position;
        newHandler.TryEnd = frame.TryEndPosition;

        if (handlerType == ExceptionHandlerType.Catch && block.catchType is not null) {
            newHandler.CatchType = definition.Module.ImportReference(block.catchType);
        }

        frame.Current = newHandler;
        frame.CurrentAwaitsHandlerStart = false;
    }

    private static void CloseCurrentHandler(MethodDefinition definition, ExceptionFrame frame, Instruction? endPosition, int index) {
        if (frame.Current is null) {
            return;
        }

        if (frame.CurrentAwaitsHandlerStart) {
            throw new ConcordEmitException("CONC118", $"Filter block at instruction {index} was never followed by its BeginCatchBlock.");
        }

        frame.Current.HandlerEnd = endPosition;
        definition.Body.ExceptionHandlers.Add(frame.Current);
        frame.Current = null;
    }

    private sealed class ExceptionFrame {
        internal ExceptionFrame(Instruction tryStart) {
            TryStart = tryStart;
        }

        internal Instruction TryStart { get; }

        internal Instruction? TryEndPosition { get; set; }

        internal ExceptionHandler? Current { get; set; }

        internal bool CurrentAwaitsHandlerStart { get; set; }
    }

    private sealed class ExceptionEnvelope {
        internal ExceptionEnvelope(List<ExceptionHandler> handlers, Instruction tryStart, int tryStartIndex, Instruction endInstruction, int endIndex, bool endsAtBodyEnd) {
            Handlers = handlers;
            TryStart = tryStart;
            TryStartIndex = tryStartIndex;
            EndInstruction = endInstruction;
            EndIndex = endIndex;
            EndsAtBodyEnd = endsAtBodyEnd;
        }

        internal List<ExceptionHandler> Handlers { get; }

        internal Instruction TryStart { get; }

        internal int TryStartIndex { get; }

        internal Instruction EndInstruction { get; }

        internal int EndIndex { get; }

        internal bool EndsAtBodyEnd { get; }

        internal List<ExceptionEnvelope> Children { get; } = new List<ExceptionEnvelope>();
    }
}
