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

        AttachExceptionBlocks(body, byInstruction);

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

        ApplyBlocks(definition, emitted, instructions);

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

    private static void AttachExceptionBlocks(MethodBody body, Dictionary<Instruction, CodeInstruction> byInstruction) {
        foreach (List<ExceptionHandler> group in GroupHandlersByTry(body.ExceptionHandlers)) {
            ExceptionHandler first = group[0];
            byInstruction[first.TryStart].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));

            foreach (ExceptionHandler handler in group) {
                AttachHandlerOpen(byInstruction, handler);
            }

            ExceptionHandler last = group[group.Count - 1];
            byInstruction[last.HandlerEnd].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        }
    }

    private static void AttachHandlerOpen(Dictionary<Instruction, CodeInstruction> byInstruction, ExceptionHandler handler) {
        if (handler.HandlerType == ExceptionHandlerType.Filter) {
            byInstruction[handler.FilterStart].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptFilterBlock));
            byInstruction[handler.HandlerStart].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock));
            return;
        }

        if (handler.HandlerType == ExceptionHandlerType.Fault) {
            byInstruction[handler.HandlerStart].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFaultBlock));
            return;
        }

        if (handler.HandlerType == ExceptionHandlerType.Finally) {
            byInstruction[handler.HandlerStart].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock));
            return;
        }

        Type? catchType = handler.CatchType is null ? null : ResolveType(handler.CatchType);
        byInstruction[handler.HandlerStart].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, catchType));
    }

    private static List<List<ExceptionHandler>> GroupHandlersByTry(IEnumerable<ExceptionHandler> handlers) {
        List<List<ExceptionHandler>> groups = new List<List<ExceptionHandler>>();
        Dictionary<Instruction, List<ExceptionHandler>> byTryStart = new Dictionary<Instruction, List<ExceptionHandler>>();

        foreach (ExceptionHandler handler in handlers) {
            if (!byTryStart.TryGetValue(handler.TryStart, out List<ExceptionHandler>? group)) {
                group = new List<ExceptionHandler>();
                byTryStart[handler.TryStart] = group;
                groups.Add(group);
            }

            group.Add(handler);
        }

        return groups;
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

    private static void ApplyBlocks(MethodDefinition definition, List<Instruction> emitted, IReadOnlyList<CodeInstruction> instructions) {
        List<ExceptionFrame> open = new List<ExceptionFrame>();
        definition.Body.ExceptionHandlers.Clear();

        for (int i = 0; i < instructions.Count; i++) {
            foreach (ExceptionBlock block in instructions[i].blocks) {
                ApplyBlock(definition, emitted[i], block, open, i);
            }
        }

        if (open.Count > 0) {
            throw new ConcordEmitException("CONC118", $"{open.Count} exception block(s) were opened and never closed.");
        }
    }

    private static void ApplyBlock(MethodDefinition definition, Instruction position, ExceptionBlock block, List<ExceptionFrame> open, int index) {
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

            CloseCurrentHandler(definition, frame, position, index);
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

    private static void CloseCurrentHandler(MethodDefinition definition, ExceptionFrame frame, Instruction endPosition, int index) {
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
}
