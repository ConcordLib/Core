using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Concord.Emit;

internal static class CecilToNeutralConverter {
    private static readonly Dictionary<string, string> ShortToLongBranch = new Dictionary<string, string> {
        ["br.s"] = "br",
        ["brtrue.s"] = "brtrue",
        ["brfalse.s"] = "brfalse",
        ["beq.s"] = "beq",
        ["bne.un.s"] = "bne.un",
        ["bge.s"] = "bge",
        ["bge.un.s"] = "bge.un",
        ["bgt.s"] = "bgt",
        ["bgt.un.s"] = "bgt.un",
        ["ble.s"] = "ble",
        ["ble.un.s"] = "ble.un",
        ["blt.s"] = "blt",
        ["blt.un.s"] = "blt.un",
        ["leave.s"] = "leave",
    };

    public static NeutralBody Convert(MethodDefinition definition) {
        return Convert(definition, null, 0);
    }

    public static NeutralBody Convert(MethodDefinition definition, Dictionary<Instruction, List<int>>? preservedLabels, int firstFreshLabelId) {
        MethodBody body = definition.Body;

        Dictionary<VariableDefinition, int> localIds = new Dictionary<VariableDefinition, int>();
        List<NeutralLocal> locals = new List<NeutralLocal>();
        foreach (VariableDefinition variable in body.Variables) {
            localIds[variable] = variable.Index;
            locals.Add(new NeutralLocal(variable.Index, ResolveClrType(variable.VariableType), variable.IsPinned));
        }

        Dictionary<Instruction, List<int>> labelIdsByInstruction = new Dictionary<Instruction, List<int>>();
        if (preservedLabels is not null) {
            foreach (KeyValuePair<Instruction, List<int>> pair in preservedLabels) {
                labelIdsByInstruction[pair.Key] = new List<int>(pair.Value);
            }
        }

        int nextLabelId = firstFreshLabelId;

        int LabelFor(Instruction target) {
            if (labelIdsByInstruction.TryGetValue(target, out List<int>? existing) && existing.Count > 0) {
                return existing[0];
            }

            int id = nextLabelId++;
            labelIdsByInstruction[target] = new List<int> { id };
            return id;
        }

        foreach (Instruction instruction in body.Instructions) {
            if (instruction.Operand is Instruction singleTarget) {
                LabelFor(singleTarget);
            } else if (instruction.Operand is Instruction[] switchTargets) {
                foreach (Instruction switchTarget in switchTargets) {
                    LabelFor(switchTarget);
                }
            }
        }

        foreach (ExceptionHandler handler in body.ExceptionHandlers) {
            if (handler.TryStart is not null) {
                LabelFor(handler.TryStart);
            }

            if (handler.TryEnd is not null) {
                LabelFor(handler.TryEnd);
            }

            if (handler.HandlerStart is not null) {
                LabelFor(handler.HandlerStart);
            }

            if (handler.HandlerEnd is not null) {
                LabelFor(handler.HandlerEnd);
            }
        }

        Dictionary<Instruction, int> instructionIndex = new Dictionary<Instruction, int>(body.Instructions.Count);
        int index = 0;
        foreach (Instruction instruction in body.Instructions) {
            instructionIndex[instruction] = index;
            index++;
        }

        List<NeutralInstruction> instructions = new List<NeutralInstruction>();
        foreach (Instruction instruction in body.Instructions) {
            NeutralInstruction neutral = ConvertInstruction(instruction, localIds, LabelFor);
            if (labelIdsByInstruction.TryGetValue(instruction, out List<int>? ownLabels)) {
                neutral.Labels.AddRange(ownLabels);
            }

            instructions.Add(neutral);
        }

        List<NeutralRegionEvent> regionEvents = BuildRegionEvents(body, LabelFor, instructionIndex, instructions.Count);

        return new NeutralBody(instructions, locals, body.InitLocals, regionEvents);
    }

    public static Type ResolveClrType(TypeReference typeReference) {
        return typeReference.ResolveReflection() ?? throw new NeutralConversionException($"Could not resolve CLR type for '{typeReference.FullName}'.");
    }

    private static List<NeutralRegionEvent> BuildRegionEvents(MethodBody body, Func<Instruction, int> labelFor, Dictionary<Instruction, int> instructionIndex, int instructionCount) {
        List<(int Position, NeutralRegionEvent Event)> positioned = new List<(int, NeutralRegionEvent)>();

        foreach (ExceptionHandler handler in body.ExceptionHandlers) {
            if (handler.HandlerType == ExceptionHandlerType.Fault) {
                throw new NeutralConversionException("Fault regions are not supported by the neutral body representation.");
            }

            if (handler.HandlerType == ExceptionHandlerType.Filter) {
                throw new NeutralConversionException("Filter regions are not supported by the neutral body representation.");
            }

            int tryStartPosition = instructionIndex.TryGetValue(handler.TryStart, out int t) ? t : instructionCount;
            int tryStartLabel = LabelForPosition(handler.TryStart, labelFor);
            positioned.Add((tryStartPosition, new NeutralRegionEvent(NeutralRegionEventKind.BeginTry, tryStartLabel, null)));

            int handlerStartPosition = instructionIndex.TryGetValue(handler.HandlerStart, out int h) ? h : instructionCount;
            int handlerStartLabel = LabelForPosition(handler.HandlerStart, labelFor);
            NeutralRegionEventKind beginKind = handler.HandlerType == ExceptionHandlerType.Catch ? NeutralRegionEventKind.BeginCatch : NeutralRegionEventKind.BeginFinally;
            Type? catchType = handler.HandlerType == ExceptionHandlerType.Catch ? ResolveClrType(handler.CatchType) : null;
            positioned.Add((handlerStartPosition, new NeutralRegionEvent(beginKind, handlerStartLabel, catchType)));

            int handlerEndPosition = handler.HandlerEnd is not null && instructionIndex.TryGetValue(handler.HandlerEnd, out int he) ? he : instructionCount;
            int handlerEndLabel = LabelForPosition(handler.HandlerEnd, labelFor);
            positioned.Add((handlerEndPosition, new NeutralRegionEvent(NeutralRegionEventKind.EndRegion, handlerEndLabel, null)));
        }

        positioned.Sort((a, b) => a.Position.CompareTo(b.Position));
        List<NeutralRegionEvent> events = new List<NeutralRegionEvent>(positioned.Count);
        foreach ((int _, NeutralRegionEvent regionEvent) in positioned) {
            events.Add(regionEvent);
        }

        return events;
    }

    private static int LabelForPosition(Instruction? position, Func<Instruction, int> labelFor) {
        return position is not null ? labelFor(position) : NeutralBody.EndOfBodyLabelId;
    }

    private static NeutralInstruction ConvertInstruction(Instruction instruction, Dictionary<VariableDefinition, int> localIds, Func<Instruction, int> labelFor) {
        OpCode opcode = instruction.OpCode;
        string name = opcode.Name!;

        if (TryConvertArgumentOpcode(instruction, name, out NeutralInstruction? argInstruction)) {
            return argInstruction!;
        }

        if (TryConvertLocalOpcode(instruction, name, localIds, out NeutralInstruction? localInstruction)) {
            return localInstruction!;
        }

        if (TryConvertBranchOpcode(instruction, name, labelFor, out NeutralInstruction? branchInstruction)) {
            return branchInstruction!;
        }

        if (TryConvertInt32ConstantOpcode(name, instruction.Operand, out NeutralInstruction? constantInstruction)) {
            return constantInstruction!;
        }

        NeutralOperand operand = instruction.Operand switch {
            null => NeutralOperand.None,
            int i32 => NeutralOperand.OfInt32(i32),
            long i64 => NeutralOperand.OfInt64(i64),
            float f32 => NeutralOperand.OfSingle(f32),
            double f64 => NeutralOperand.OfDouble(f64),
            string s => NeutralOperand.OfString(s),
            TypeReference typeRef => NeutralOperand.OfType(ResolveClrType(typeRef)),
            FieldReference fieldRef => NeutralOperand.OfField(ResolveClrField(fieldRef)),
            MethodReference methodRef => NeutralOperand.OfMethod(ResolveClrMethod(methodRef)),
            _ => throw new NeutralConversionException($"Unsupported operand kind '{instruction.Operand.GetType().Name}' on opcode '{name}'."),
        };

        return new NeutralInstruction(name, operand);
    }

    private static bool TryConvertArgumentOpcode(Instruction instruction, string name, out NeutralInstruction? result) {
        result = null;
        switch (name) {
            case "ldarg.0": result = new NeutralInstruction("ldarg", NeutralOperand.OfArgument(0)); return true;
            case "ldarg.1": result = new NeutralInstruction("ldarg", NeutralOperand.OfArgument(1)); return true;
            case "ldarg.2": result = new NeutralInstruction("ldarg", NeutralOperand.OfArgument(2)); return true;
            case "ldarg.3": result = new NeutralInstruction("ldarg", NeutralOperand.OfArgument(3)); return true;
            case "ldarg.s":
            case "ldarg":
                result = new NeutralInstruction("ldarg", NeutralOperand.OfArgument(((ParameterDefinition)instruction.Operand).Index));
                return true;
            case "ldarga.s":
            case "ldarga":
                result = new NeutralInstruction("ldarga", NeutralOperand.OfArgument(((ParameterDefinition)instruction.Operand).Index));
                return true;
            case "starg.s":
            case "starg":
                result = new NeutralInstruction("starg", NeutralOperand.OfArgument(((ParameterDefinition)instruction.Operand).Index));
                return true;
            default:
                return false;
        }
    }

    private static bool TryConvertLocalOpcode(Instruction instruction, string name, Dictionary<VariableDefinition, int> localIds, out NeutralInstruction? result) {
        result = null;
        switch (name) {
            case "ldloc.0": result = new NeutralInstruction("ldloc", NeutralOperand.OfLocal(0)); return true;
            case "ldloc.1": result = new NeutralInstruction("ldloc", NeutralOperand.OfLocal(1)); return true;
            case "ldloc.2": result = new NeutralInstruction("ldloc", NeutralOperand.OfLocal(2)); return true;
            case "ldloc.3": result = new NeutralInstruction("ldloc", NeutralOperand.OfLocal(3)); return true;
            case "ldloc.s":
            case "ldloc":
                result = new NeutralInstruction("ldloc", NeutralOperand.OfLocal(localIds[(VariableDefinition)instruction.Operand]));
                return true;
            case "stloc.0": result = new NeutralInstruction("stloc", NeutralOperand.OfLocal(0)); return true;
            case "stloc.1": result = new NeutralInstruction("stloc", NeutralOperand.OfLocal(1)); return true;
            case "stloc.2": result = new NeutralInstruction("stloc", NeutralOperand.OfLocal(2)); return true;
            case "stloc.3": result = new NeutralInstruction("stloc", NeutralOperand.OfLocal(3)); return true;
            case "stloc.s":
            case "stloc":
                result = new NeutralInstruction("stloc", NeutralOperand.OfLocal(localIds[(VariableDefinition)instruction.Operand]));
                return true;
            case "ldloca.s":
            case "ldloca":
                result = new NeutralInstruction("ldloca", NeutralOperand.OfLocal(localIds[(VariableDefinition)instruction.Operand]));
                return true;
            default:
                return false;
        }
    }

    private static bool TryConvertInt32ConstantOpcode(string name, object? operand, out NeutralInstruction? result) {
        result = null;
        switch (name) {
            case "ldc.i4.m1": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(-1)); return true;
            case "ldc.i4.0": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(0)); return true;
            case "ldc.i4.1": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(1)); return true;
            case "ldc.i4.2": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(2)); return true;
            case "ldc.i4.3": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(3)); return true;
            case "ldc.i4.4": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(4)); return true;
            case "ldc.i4.5": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(5)); return true;
            case "ldc.i4.6": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(6)); return true;
            case "ldc.i4.7": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(7)); return true;
            case "ldc.i4.8": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(8)); return true;
            case "ldc.i4.s": result = new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32((sbyte)operand!)); return true;
            default: return false;
        }
    }

    private static bool TryConvertBranchOpcode(Instruction instruction, string name, Func<Instruction, int> labelFor, out NeutralInstruction? result) {
        result = null;

        if (name == "switch") {
            Instruction[] targets = (Instruction[])instruction.Operand;
            int[] ids = new int[targets.Length];
            for (int i = 0; i < targets.Length; i++) {
                ids[i] = labelFor(targets[i]);
            }

            result = new NeutralInstruction("switch", NeutralOperand.OfSwitchLabels(ids));
            return true;
        }

        bool isBranch = instruction.Operand is Instruction && instruction.OpCode.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget;
        if (!isBranch) {
            return false;
        }

        string canonicalName = ShortToLongBranch.TryGetValue(name, out string? mapped) ? mapped : name;
        int labelId = labelFor((Instruction)instruction.Operand);
        result = new NeutralInstruction(canonicalName, NeutralOperand.OfLabel(labelId));
        return true;
    }

    private static System.Reflection.FieldInfo ResolveClrField(FieldReference fieldReference) {
        return fieldReference.ResolveReflection() ?? throw new NeutralConversionException($"Could not resolve CLR field for '{fieldReference.FullName}'.");
    }

    private static System.Reflection.MethodBase ResolveClrMethod(MethodReference methodReference) {
        return methodReference.ResolveReflection() ?? throw new NeutralConversionException($"Could not resolve CLR method for '{methodReference.FullName}'.");
    }
}
