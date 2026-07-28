using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Emit;
using HarmonyLib;

#nullable enable

namespace Concord.Harmony;

using CodeInstruction = HarmonyLib.CodeInstruction;
using ExceptionBlock = HarmonyLib.ExceptionBlock;
using ExceptionBlockType = HarmonyLib.ExceptionBlockType;
using Label = System.Reflection.Emit.Label;

internal static class CodeInstructionConverter {
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

    private static readonly Dictionary<string, OpCode> SreOpCodesByName = BuildSreOpCodeTable();

    internal static NeutralBody ToNeutral(List<CodeInstruction> stream, out HarmonyStreamContext context) {
        HarmonyStreamContext built = new HarmonyStreamContext();
        Dictionary<Label, int> idByLabel = new Dictionary<Label, int>();
        int nextId = 0;

        int IdFor(Label label) {
            if (idByLabel.TryGetValue(label, out int existing)) {
                return existing;
            }

            int id = nextId;
            nextId++;
            idByLabel[label] = id;
            built.LabelById[id] = label;
            return id;
        }

        int maxSlot = -1;
        Dictionary<int, Type> typeBySlot = new Dictionary<int, Type>();
        foreach (CodeInstruction instruction in stream) {
            foreach (Label label in instruction.labels) {
                IdFor(label);
            }

            if (instruction.operand is Label operandLabel) {
                IdFor(operandLabel);
            } else if (instruction.operand is Label[] operandLabels) {
                foreach (Label switchLabel in operandLabels) {
                    IdFor(switchLabel);
                }
            } else if (instruction.operand is LocalBuilder builder) {
                built.BuilderBySlot[builder.LocalIndex] = builder;
                typeBySlot[builder.LocalIndex] = builder.LocalType!;
                if (builder.LocalIndex > maxSlot) {
                    maxSlot = builder.LocalIndex;
                }
            }

            int compactSlot = CompactLocalSlot(instruction.opcode.Name!);
            if (compactSlot > maxSlot) {
                maxSlot = compactSlot;
            }
        }

        List<NeutralInstruction> instructions = new List<NeutralInstruction>();
        foreach (CodeInstruction instruction in stream) {
            NeutralInstruction neutral = ConvertIncoming(instruction, IdFor);
            foreach (Label label in instruction.labels) {
                neutral.Labels.Add(IdFor(label));
            }

            instructions.Add(neutral);
        }

        List<NeutralRegionEvent> regionEvents = BuildRegionEvents(stream, instructions, ref nextId);

        List<NeutralLocal> locals = new List<NeutralLocal>();
        for (int slot = 0; slot <= maxSlot; slot++) {
            Type localType = typeBySlot.TryGetValue(slot, out Type? known) ? known : typeof(object);
            locals.Add(new NeutralLocal(slot, localType, false, true));
        }

        built.IncomingLabelCount = nextId;
        built.IncomingLocalCount = maxSlot + 1;
        context = built;
        return new NeutralBody(instructions, locals, true, regionEvents);
    }

    internal static List<CodeInstruction> FromNeutral(NeutralBody neutralBody, HarmonyStreamContext context, ILGenerator generator) {
        Dictionary<int, int> instructionIndexByLabel = new Dictionary<int, int>();
        for (int i = 0; i < neutralBody.Instructions.Count; i++) {
            foreach (int labelId in neutralBody.Instructions[i].Labels) {
                instructionIndexByLabel[labelId] = i;
            }
        }

        HashSet<int> referencedLabels = new HashSet<int>();
        foreach (NeutralInstruction instruction in neutralBody.Instructions) {
            if (instruction.Operand.Kind == NeutralOperandKind.Label) {
                referencedLabels.Add(instruction.Operand.AsLabelId());
            } else if (instruction.Operand.Kind == NeutralOperandKind.SwitchLabels) {
                foreach (int labelId in instruction.Operand.AsSwitchLabelIds()) {
                    referencedLabels.Add(labelId);
                }
            }
        }

        foreach (int referenced in referencedLabels) {
            if (!instructionIndexByLabel.ContainsKey(referenced)) {
                throw new NeutralConversionException(
                    $"Label {referenced} is referenced by a branch but its carrier instruction did not survive Concord composition; refusing to emit an unmarked label.");
            }
        }

        Dictionary<int, Label> labelById = new Dictionary<int, Label>();
        foreach (int attachedId in instructionIndexByLabel.Keys) {
            labelById[attachedId] = context.LabelById.TryGetValue(attachedId, out Label incoming) ? incoming : generator.DefineLabel();
        }

        Dictionary<int, LocalBuilder> builderById = new Dictionary<int, LocalBuilder>(context.BuilderBySlot);
        foreach (NeutralLocal local in neutralBody.Locals) {
            if (local.Id >= context.IncomingLocalCount && !builderById.ContainsKey(local.Id)) {
                builderById[local.Id] = generator.DeclareLocal(local.Type, local.Pinned);
            }
        }

        List<CodeInstruction> outgoing = new List<CodeInstruction>(neutralBody.Instructions.Count);
        foreach (NeutralInstruction instruction in neutralBody.Instructions) {
            CodeInstruction emitted = ConvertOutgoing(instruction, context, labelById, builderById);
            foreach (int labelId in instruction.Labels) {
                emitted.labels.Add(labelById[labelId]);
            }

            outgoing.Add(emitted);
        }

        ApplyOutgoingBlocks(neutralBody, instructionIndexByLabel, outgoing);
        return outgoing;
    }

    private static List<NeutralRegionEvent> BuildRegionEvents(List<CodeInstruction> stream, List<NeutralInstruction> instructions, ref int nextId) {
        List<(int Position, int BeginRank, int Seq, NeutralRegionEvent Event)> positioned = new List<(int, int, int, NeutralRegionEvent)>();
        int seq = 0;

        for (int i = 0; i < stream.Count; i++) {
            foreach (ExceptionBlock block in stream[i].blocks) {
                if (block.blockType == ExceptionBlockType.BeginExceptFilterBlock || block.blockType == ExceptionBlockType.BeginFaultBlock) {
                    throw new NeutralConversionException($"Unsupported exception block kind '{block.blockType}'.");
                }

                if (block.blockType == ExceptionBlockType.EndExceptionBlock) {
                    int endLabel = i + 1 < stream.Count ? EnsurePositionLabel(instructions, i + 1, ref nextId) : NeutralBody.EndOfBodyLabelId;
                    positioned.Add((i + 1, 0, seq, new NeutralRegionEvent(NeutralRegionEventKind.EndRegion, endLabel, null)));
                    seq++;
                    continue;
                }

                int beginLabel = EnsurePositionLabel(instructions, i, ref nextId);
                NeutralRegionEventKind kind = block.blockType switch {
                    ExceptionBlockType.BeginExceptionBlock => NeutralRegionEventKind.BeginTry,
                    ExceptionBlockType.BeginCatchBlock => NeutralRegionEventKind.BeginCatch,
                    ExceptionBlockType.BeginFinallyBlock => NeutralRegionEventKind.BeginFinally,
                    _ => throw new NeutralConversionException($"Unsupported exception block kind '{block.blockType}'."),
                };
                Type? catchType = kind == NeutralRegionEventKind.BeginCatch ? block.catchType ?? typeof(object) : null;
                positioned.Add((i, 1, seq, new NeutralRegionEvent(kind, beginLabel, catchType)));
                seq++;
            }
        }

        positioned.Sort((a, b) => {
            int byPosition = a.Position.CompareTo(b.Position);
            if (byPosition != 0) {
                return byPosition;
            }

            int byRank = a.BeginRank.CompareTo(b.BeginRank);
            if (byRank != 0) {
                return byRank;
            }

            return a.Seq.CompareTo(b.Seq);
        });

        List<NeutralRegionEvent> events = new List<NeutralRegionEvent>(positioned.Count);
        foreach ((int _, int _, int _, NeutralRegionEvent regionEvent) in positioned) {
            events.Add(regionEvent);
        }

        return events;
    }

    private static int EnsurePositionLabel(List<NeutralInstruction> instructions, int index, ref int nextId) {
        NeutralInstruction instruction = instructions[index];
        if (instruction.Labels.Count > 0) {
            return instruction.Labels[0];
        }

        int id = nextId;
        nextId++;
        instruction.Labels.Add(id);
        return id;
    }

    private static int CompactLocalSlot(string name) {
        return name switch {
            "ldloc.0" or "stloc.0" => 0,
            "ldloc.1" or "stloc.1" => 1,
            "ldloc.2" or "stloc.2" => 2,
            "ldloc.3" or "stloc.3" => 3,
            _ => -1,
        };
    }

    private static NeutralInstruction ConvertIncoming(CodeInstruction instruction, Func<Label, int> idFor) {
        string name = instruction.opcode.Name!;

        switch (name) {
            case "ldarg.0": return new NeutralInstruction("ldarg", NeutralOperand.OfArgument(0));
            case "ldarg.1": return new NeutralInstruction("ldarg", NeutralOperand.OfArgument(1));
            case "ldarg.2": return new NeutralInstruction("ldarg", NeutralOperand.OfArgument(2));
            case "ldarg.3": return new NeutralInstruction("ldarg", NeutralOperand.OfArgument(3));
            case "ldarg.s":
            case "ldarg":
                return new NeutralInstruction("ldarg", NeutralOperand.OfArgument(ArgSlot(instruction.operand, name)));
            case "ldarga.s":
            case "ldarga":
                return new NeutralInstruction("ldarga", NeutralOperand.OfArgument(ArgSlot(instruction.operand, name)));
            case "starg.s":
            case "starg":
                return new NeutralInstruction("starg", NeutralOperand.OfArgument(ArgSlot(instruction.operand, name)));
            case "ldloc.0": return new NeutralInstruction("ldloc", NeutralOperand.OfLocal(0));
            case "ldloc.1": return new NeutralInstruction("ldloc", NeutralOperand.OfLocal(1));
            case "ldloc.2": return new NeutralInstruction("ldloc", NeutralOperand.OfLocal(2));
            case "ldloc.3": return new NeutralInstruction("ldloc", NeutralOperand.OfLocal(3));
            case "stloc.0": return new NeutralInstruction("stloc", NeutralOperand.OfLocal(0));
            case "stloc.1": return new NeutralInstruction("stloc", NeutralOperand.OfLocal(1));
            case "stloc.2": return new NeutralInstruction("stloc", NeutralOperand.OfLocal(2));
            case "stloc.3": return new NeutralInstruction("stloc", NeutralOperand.OfLocal(3));
            case "ldloc.s":
            case "ldloc":
                return new NeutralInstruction("ldloc", NeutralOperand.OfLocal(LocalSlot(instruction.operand, name)));
            case "stloc.s":
            case "stloc":
                return new NeutralInstruction("stloc", NeutralOperand.OfLocal(LocalSlot(instruction.operand, name)));
            case "ldloca.s":
            case "ldloca":
                return new NeutralInstruction("ldloca", NeutralOperand.OfLocal(LocalSlot(instruction.operand, name)));
            case "ldc.i4.m1": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(-1));
            case "ldc.i4.0": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(0));
            case "ldc.i4.1": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(1));
            case "ldc.i4.2": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(2));
            case "ldc.i4.3": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(3));
            case "ldc.i4.4": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(4));
            case "ldc.i4.5": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(5));
            case "ldc.i4.6": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(6));
            case "ldc.i4.7": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(7));
            case "ldc.i4.8": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(8));
            case "ldc.i4.s": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(Int32Value(instruction.operand, name)));
            case "ldc.i4": return new NeutralInstruction("ldc.i4", NeutralOperand.OfInt32(Int32Value(instruction.operand, name)));
            default:
                break;
        }

        if (instruction.operand is Label targetLabel) {
            string canonicalName = ShortToLongBranch.TryGetValue(name, out string? mapped) ? mapped : name;
            return new NeutralInstruction(canonicalName, NeutralOperand.OfLabel(idFor(targetLabel)));
        }

        if (instruction.operand is Label[] switchLabels) {
            int[] ids = new int[switchLabels.Length];
            for (int i = 0; i < switchLabels.Length; i++) {
                ids[i] = idFor(switchLabels[i]);
            }

            return new NeutralInstruction("switch", NeutralOperand.OfSwitchLabels(ids));
        }

        NeutralOperand operand = instruction.operand switch {
            null => NeutralOperand.None,
            int i32 => NeutralOperand.OfInt32(i32),
            long i64 => NeutralOperand.OfInt64(i64),
            float f32 => NeutralOperand.OfSingle(f32),
            double f64 => NeutralOperand.OfDouble(f64),
            string s => NeutralOperand.OfString(s),
            Type type => NeutralOperand.OfType(type),
            FieldInfo field => NeutralOperand.OfField(field),
            MethodBase method => NeutralOperand.OfMethod(method),
            _ => throw new NeutralConversionException($"Unsupported incoming operand kind '{instruction.operand.GetType().Name}' on opcode '{name}'."),
        };

        return new NeutralInstruction(name, operand);
    }

    private static int ArgSlot(object? operand, string name) {
        return operand switch {
            byte b => b,
            sbyte sb => sb,
            short i16 => i16,
            int i32 => i32,
            _ => throw new NeutralConversionException($"Unsupported argument operand '{operand?.GetType().Name ?? "null"}' on opcode '{name}'."),
        };
    }

    private static int LocalSlot(object? operand, string name) {
        if (operand is LocalBuilder builder) {
            return builder.LocalIndex;
        }

        throw new NeutralConversionException($"Local opcode '{name}' carries '{operand?.GetType().Name ?? "null"}' instead of a LocalBuilder.");
    }

    private static int Int32Value(object? operand, string name) {
        return operand switch {
            sbyte sb => sb,
            byte b => b,
            int i32 => i32,
            _ => throw new NeutralConversionException($"Unsupported int constant operand '{operand?.GetType().Name ?? "null"}' on opcode '{name}'."),
        };
    }

    private static void ApplyOutgoingBlocks(NeutralBody neutralBody, Dictionary<int, int> instructionIndexByLabel, List<CodeInstruction> outgoing) {
        foreach (NeutralRegionEvent regionEvent in neutralBody.RegionEvents) {
            if (regionEvent.Kind == NeutralRegionEventKind.EndRegion) {
                int endIndex = regionEvent.PositionLabelId == NeutralBody.EndOfBodyLabelId
                    ? outgoing.Count
                    : instructionIndexByLabel[regionEvent.PositionLabelId];
                if (endIndex == 0) {
                    throw new NeutralConversionException("Exception region ends before any instruction.");
                }

                outgoing[endIndex - 1].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
                continue;
            }

            int beginIndex = instructionIndexByLabel[regionEvent.PositionLabelId];
            ExceptionBlockType blockType = regionEvent.Kind switch {
                NeutralRegionEventKind.BeginTry => ExceptionBlockType.BeginExceptionBlock,
                NeutralRegionEventKind.BeginCatch => ExceptionBlockType.BeginCatchBlock,
                NeutralRegionEventKind.BeginFinally => ExceptionBlockType.BeginFinallyBlock,
                _ => throw new NeutralConversionException($"Unsupported region event kind '{regionEvent.Kind}'."),
            };
            outgoing[beginIndex].blocks.Add(new ExceptionBlock(blockType, regionEvent.CatchType));
        }
    }

    private static CodeInstruction ConvertOutgoing(NeutralInstruction instruction, HarmonyStreamContext context, Dictionary<int, Label> labelById, Dictionary<int, LocalBuilder> builderById) {
        string name = instruction.OpcodeName;

        if (name == "ldarg") {
            int slot = instruction.Operand.AsArgumentSlot();
            return slot switch {
                0 => new CodeInstruction(OpCodes.Ldarg_0),
                1 => new CodeInstruction(OpCodes.Ldarg_1),
                2 => new CodeInstruction(OpCodes.Ldarg_2),
                3 => new CodeInstruction(OpCodes.Ldarg_3),
                <= 255 => new CodeInstruction(OpCodes.Ldarg_S, (byte)slot),
                _ => throw new NeutralConversionException($"Argument slot {slot} exceeds the short-form range."),
            };
        }

        if (name == "starg") {
            int slot = instruction.Operand.AsArgumentSlot();
            if (slot > 255) {
                throw new NeutralConversionException($"Argument slot {slot} exceeds the short-form range.");
            }

            return new CodeInstruction(OpCodes.Starg_S, (byte)slot);
        }

        if (name == "ldarga") {
            int slot = instruction.Operand.AsArgumentSlot();
            if (slot > 255) {
                throw new NeutralConversionException($"Argument slot {slot} exceeds the short-form range.");
            }

            return new CodeInstruction(OpCodes.Ldarga_S, (byte)slot);
        }

        if (name == "ldloc" || name == "stloc" || name == "ldloca") {
            return ConvertOutgoingLocal(name, instruction.Operand.AsLocalId(), context, builderById);
        }

        if (instruction.Operand.Kind == NeutralOperandKind.Label) {
            return new CodeInstruction(SreOpCode(name), labelById[instruction.Operand.AsLabelId()]);
        }

        if (instruction.Operand.Kind == NeutralOperandKind.SwitchLabels) {
            int[] ids = instruction.Operand.AsSwitchLabelIds();
            Label[] targets = new Label[ids.Length];
            for (int i = 0; i < ids.Length; i++) {
                targets[i] = labelById[ids[i]];
            }

            return new CodeInstruction(SreOpCode(name), targets);
        }

        OpCode opcode = SreOpCode(name);
        return instruction.Operand.Kind switch {
            NeutralOperandKind.None => new CodeInstruction(opcode),
            NeutralOperandKind.Int32 => new CodeInstruction(opcode, instruction.Operand.AsInt32()),
            NeutralOperandKind.Int64 => new CodeInstruction(opcode, instruction.Operand.AsInt64()),
            NeutralOperandKind.Single => new CodeInstruction(opcode, instruction.Operand.AsSingle()),
            NeutralOperandKind.Double => new CodeInstruction(opcode, instruction.Operand.AsDouble()),
            NeutralOperandKind.String => new CodeInstruction(opcode, instruction.Operand.AsString()),
            NeutralOperandKind.Type => new CodeInstruction(opcode, instruction.Operand.AsType()),
            NeutralOperandKind.Field => new CodeInstruction(opcode, instruction.Operand.AsField()),
            NeutralOperandKind.Method => new CodeInstruction(opcode, instruction.Operand.AsMethod()),
            _ => throw new NeutralConversionException($"Unsupported outgoing operand kind '{instruction.Operand.Kind}' on opcode '{name}'."),
        };
    }

    private static CodeInstruction ConvertOutgoingLocal(string name, int slot, HarmonyStreamContext context, Dictionary<int, LocalBuilder> builderById) {
        if (builderById.TryGetValue(slot, out LocalBuilder? builder)) {
            OpCode viaBuilder = name switch {
                "ldloc" => OpCodes.Ldloc_S,
                "stloc" => OpCodes.Stloc_S,
                _ => OpCodes.Ldloca_S,
            };
            return new CodeInstruction(viaBuilder, builder);
        }

        if (slot >= context.IncomingLocalCount || name == "ldloca" || slot > 3) {
            throw new NeutralConversionException($"No LocalBuilder is available for local slot {slot} used by '{name}'.");
        }

        if (name == "ldloc") {
            return slot switch {
                0 => new CodeInstruction(OpCodes.Ldloc_0),
                1 => new CodeInstruction(OpCodes.Ldloc_1),
                2 => new CodeInstruction(OpCodes.Ldloc_2),
                _ => new CodeInstruction(OpCodes.Ldloc_3),
            };
        }

        return slot switch {
            0 => new CodeInstruction(OpCodes.Stloc_0),
            1 => new CodeInstruction(OpCodes.Stloc_1),
            2 => new CodeInstruction(OpCodes.Stloc_2),
            _ => new CodeInstruction(OpCodes.Stloc_3),
        };
    }

    private static Dictionary<string, OpCode> BuildSreOpCodeTable() {
        Dictionary<string, OpCode> table = new Dictionary<string, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (field.GetValue(null) is OpCode code) {
                table[code.Name!] = code;
            }
        }

        return table;
    }

    private static OpCode SreOpCode(string name) {
        if (SreOpCodesByName.TryGetValue(name, out OpCode code)) {
            return code;
        }

        throw new NeutralConversionException($"Unknown SRE opcode '{name}'.");
    }
}
