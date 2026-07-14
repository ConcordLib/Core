using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

internal sealed class SpineCopy {
    public SpineCopy(List<Instruction> instructions, List<ExceptionHandler> handlers, Instruction exitJoin) {
        this.Instructions = instructions;
        this.Handlers = handlers;
        this.ExitJoin = exitJoin;
        this.ArgLocals = new Dictionary<int, VariableDefinition>();
    }

    public List<Instruction> Instructions { get; }

    public List<ExceptionHandler> Handlers { get; }

    public Instruction ExitJoin { get; }

    public Dictionary<int, VariableDefinition> ArgLocals { get; }

    public static SpineCopy Create(SpineTemplate template, MethodDefinition wrapperDefinition) {
        ModuleDefinition module = wrapperDefinition.Module;
        MethodBody wrapperBody = wrapperDefinition.Body;

        Dictionary<VariableDefinition, VariableDefinition> variableMap = new Dictionary<VariableDefinition, VariableDefinition>(template.Locals.Count);
        foreach (VariableDefinition local in template.Locals) {
            VariableDefinition copy = new VariableDefinition(local.VariableType);
            wrapperBody.Variables.Add(copy);
            variableMap[local] = copy;
        }

        if (template.Locals.Count > 0) {
            wrapperBody.InitLocals = true;
        }

        MethodDefinition scratchDefinition = new MethodDefinition("SpineCopyScratch", MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Void);
        MethodBody scratchBody = scratchDefinition.Body;

        Dictionary<Instruction, Instruction> instructionMap = new Dictionary<Instruction, Instruction>(template.Instructions.Count);
        foreach (Instruction source in template.Instructions) {
            Instruction copy = CloneInstruction(source, variableMap);
            instructionMap[source] = copy;
            scratchBody.Instructions.Add(copy);
        }

        foreach (Instruction copy in scratchBody.Instructions) {
            RemapBranchTargets(copy, instructionMap);
        }

        List<ExceptionHandler> handlers = new List<ExceptionHandler>(template.Handlers.Count);
        foreach (ExceptionHandler handler in template.Handlers) {
            handlers.Add(CloneHandler(handler, instructionMap));
        }

        List<Instruction> instructions = new List<Instruction>(scratchBody.Instructions);
        Instruction exitJoin = Instruction.Create(OpCodes.Nop);

        return new SpineCopy(instructions, handlers, exitJoin);
    }

    private static Instruction CloneInstruction(Instruction source, Dictionary<VariableDefinition, VariableDefinition> variableMap) {
        if (source.Operand is VariableDefinition variable) {
            VariableDefinition mapped = variableMap.TryGetValue(variable, out VariableDefinition? copy) ? copy : variable;
            return Instruction.Create(source.OpCode, mapped);
        }

        Instruction clone = Instruction.Create(OpCodes.Nop);
        clone.OpCode = source.OpCode;
        clone.Operand = source.Operand;
        return clone;
    }

    private static void RemapBranchTargets(Instruction instruction, Dictionary<Instruction, Instruction> map) {
        if (instruction.Operand is Instruction target) {
            if (map.TryGetValue(target, out Instruction? mapped)) {
                instruction.Operand = mapped;
            }

            return;
        }

        if (instruction.Operand is Instruction[] targets) {
            Instruction[] remapped = new Instruction[targets.Length];
            for (int i = 0; i < targets.Length; i++) {
                remapped[i] = map.TryGetValue(targets[i], out Instruction? mapped) ? mapped : targets[i];
            }

            instruction.Operand = remapped;
        }
    }

    private static ExceptionHandler CloneHandler(ExceptionHandler source, Dictionary<Instruction, Instruction> map) {
        ExceptionHandler copy = new ExceptionHandler(source.HandlerType) {
            TryStart = Resolve(source.TryStart, map),
            TryEnd = Resolve(source.TryEnd, map),
            HandlerStart = Resolve(source.HandlerStart, map),
            HandlerEnd = Resolve(source.HandlerEnd, map),
            FilterStart = Resolve(source.FilterStart, map),
            CatchType = source.CatchType,
        };

        return copy;
    }

    private static Instruction? Resolve(Instruction? source, Dictionary<Instruction, Instruction> map) {
        return source is null ? null : map[source];
    }
}
