using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Concord.Emit;

internal static class NeutralToCecilConverter {
    private static readonly Dictionary<string, OpCode> OpCodesByName = BuildOpCodeTable();

    public static Dictionary<Instruction, List<int>> Populate(NeutralBody neutralBody, MethodDefinition definition) {
        MethodBody body = definition.Body;
        ModuleDefinition module = definition.Module;

        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();
        body.InitLocals = neutralBody.InitLocals;

        Dictionary<int, VariableDefinition> localsById = new Dictionary<int, VariableDefinition>();
        foreach (NeutralLocal local in neutralBody.Locals) {
            TypeReference variableType = module.ImportReference(local.Type);
            if (local.Pinned) {
                variableType = new PinnedType(variableType);
            }

            VariableDefinition variable = new VariableDefinition(variableType);
            body.Variables.Add(variable);
            localsById[local.Id] = variable;
        }

        List<ParameterDefinition> parameters = new List<ParameterDefinition>(definition.Parameters);

        Instruction[] created = new Instruction[neutralBody.Instructions.Count];
        for (int i = 0; i < neutralBody.Instructions.Count; i++) {
            created[i] = CreateInstruction(neutralBody.Instructions[i], parameters, localsById, module);
        }

        Dictionary<int, Instruction> instructionByLabel = new Dictionary<int, Instruction>();
        for (int i = 0; i < neutralBody.Instructions.Count; i++) {
            foreach (int labelId in neutralBody.Instructions[i].Labels) {
                instructionByLabel[labelId] = created[i];
            }
        }

        for (int i = 0; i < neutralBody.Instructions.Count; i++) {
            NeutralInstruction source = neutralBody.Instructions[i];
            Instruction target = created[i];
            if (source.Operand.Kind == NeutralOperandKind.Label) {
                target.Operand = ResolveLabel(source.Operand.AsLabelId(), instructionByLabel);
            } else if (source.Operand.Kind == NeutralOperandKind.SwitchLabels) {
                int[] labelIds = source.Operand.AsSwitchLabelIds();
                Instruction[] switchTargets = new Instruction[labelIds.Length];
                for (int t = 0; t < labelIds.Length; t++) {
                    switchTargets[t] = ResolveLabel(labelIds[t], instructionByLabel);
                }

                target.Operand = switchTargets;
            }
        }

        ILProcessor il = body.GetILProcessor();
        foreach (Instruction instruction in created) {
            il.Append(instruction);
        }

        ApplyRegionEvents(neutralBody, instructionByLabel, body, module);

        Dictionary<Instruction, List<int>> labelProvenance = new Dictionary<Instruction, List<int>>();
        for (int i = 0; i < neutralBody.Instructions.Count; i++) {
            if (neutralBody.Instructions[i].Labels.Count > 0) {
                labelProvenance[created[i]] = new List<int>(neutralBody.Instructions[i].Labels);
            }
        }

        return labelProvenance;
    }

    private static Instruction ResolveLabel(int labelId, Dictionary<int, Instruction> instructionByLabel) {
        if (labelId == NeutralBody.EndOfBodyLabelId) {
            throw new NeutralConversionException("A branch cannot target the end-of-body sentinel label.");
        }

        return instructionByLabel[labelId];
    }

    private static void ApplyRegionEvents(NeutralBody neutralBody, Dictionary<int, Instruction> instructionByLabel, MethodBody body, ModuleDefinition module) {
        Instruction? PositionFor(int labelId) {
            return labelId == NeutralBody.EndOfBodyLabelId ? null : instructionByLabel[labelId];
        }

        List<ExceptionHandler> openTry = new List<ExceptionHandler>();
        List<ExceptionHandler> pendingEnd = new List<ExceptionHandler>();

        foreach (NeutralRegionEvent regionEvent in neutralBody.RegionEvents) {
            switch (regionEvent.Kind) {
                case NeutralRegionEventKind.BeginTry: {
                    ExceptionHandler handler = new ExceptionHandler(ExceptionHandlerType.Catch) {
                        TryStart = PositionFor(regionEvent.PositionLabelId),
                    };
                    openTry.Add(handler);
                    break;
                }

                case NeutralRegionEventKind.BeginCatch: {
                    ExceptionHandler handler = openTry[openTry.Count - 1];
                    openTry.RemoveAt(openTry.Count - 1);
                    handler.HandlerType = ExceptionHandlerType.Catch;
                    handler.CatchType = module.ImportReference(regionEvent.CatchType);
                    handler.HandlerStart = PositionFor(regionEvent.PositionLabelId);
                    handler.TryEnd = handler.HandlerStart;
                    body.ExceptionHandlers.Add(handler);
                    pendingEnd.Add(handler);
                    break;
                }

                case NeutralRegionEventKind.BeginFinally: {
                    ExceptionHandler handler = openTry[openTry.Count - 1];
                    openTry.RemoveAt(openTry.Count - 1);
                    handler.HandlerType = ExceptionHandlerType.Finally;
                    handler.HandlerStart = PositionFor(regionEvent.PositionLabelId);
                    handler.TryEnd = handler.HandlerStart;
                    body.ExceptionHandlers.Add(handler);
                    pendingEnd.Add(handler);
                    break;
                }

                case NeutralRegionEventKind.EndRegion: {
                    ExceptionHandler handler = pendingEnd[pendingEnd.Count - 1];
                    pendingEnd.RemoveAt(pendingEnd.Count - 1);
                    handler.HandlerEnd = PositionFor(regionEvent.PositionLabelId);
                    break;
                }
            }
        }
    }

    private static Instruction CreateInstruction(NeutralInstruction source, List<ParameterDefinition> parameters, Dictionary<int, VariableDefinition> localsById, ModuleDefinition module) {
        OpCode opcode = OpCodeByName(source.OpcodeName);

        switch (source.Operand.Kind) {
            case NeutralOperandKind.None:
                return Instruction.Create(opcode);
            case NeutralOperandKind.Int32:
                return Instruction.Create(opcode, source.Operand.AsInt32());
            case NeutralOperandKind.Int64:
                return Instruction.Create(opcode, source.Operand.AsInt64());
            case NeutralOperandKind.Single:
                return Instruction.Create(opcode, source.Operand.AsSingle());
            case NeutralOperandKind.Double:
                return Instruction.Create(opcode, source.Operand.AsDouble());
            case NeutralOperandKind.String:
                return Instruction.Create(opcode, source.Operand.AsString());
            case NeutralOperandKind.Argument:
                return Instruction.Create(opcode, parameters[source.Operand.AsArgumentSlot()]);
            case NeutralOperandKind.Local:
                return Instruction.Create(opcode, localsById[source.Operand.AsLocalId()]);
            case NeutralOperandKind.Label:
                return Instruction.Create(opcode, Instruction.Create(OpCodes.Nop));
            case NeutralOperandKind.SwitchLabels:
                return Instruction.Create(opcode, Array.Empty<Instruction>());
            case NeutralOperandKind.Type:
                return Instruction.Create(opcode, module.ImportReference(source.Operand.AsType()));
            case NeutralOperandKind.Field:
                return Instruction.Create(opcode, module.ImportReference(source.Operand.AsField()));
            case NeutralOperandKind.Method:
                return Instruction.Create(opcode, module.ImportReference(source.Operand.AsMethod()));
            default:
                throw new NeutralConversionException($"Unsupported neutral operand kind '{source.Operand.Kind}'.");
        }
    }

    private static Dictionary<string, OpCode> BuildOpCodeTable() {
        Dictionary<string, OpCode> table = new Dictionary<string, OpCode>();
        foreach (System.Reflection.FieldInfo field in typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)) {
            if (field.GetValue(null) is OpCode code) {
                table[code.Name!] = code;
            }
        }

        return table;
    }

    private static OpCode OpCodeByName(string name) {
        if (OpCodesByName.TryGetValue(name, out OpCode code)) {
            return code;
        }

        throw new NeutralConversionException($"Unknown opcode '{name}'.");
    }
}
