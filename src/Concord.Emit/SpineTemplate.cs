using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Concord.Emit;

internal sealed class SpineTemplate {
    public SpineTemplate(IReadOnlyList<Instruction> instructions, IReadOnlyList<ExceptionHandler> handlers, IReadOnlyList<VariableDefinition> locals) {
        this.Instructions = instructions;
        this.Handlers = handlers;
        this.Locals = locals;
    }

    public IReadOnlyList<Instruction> Instructions { get; }

    public IReadOnlyList<ExceptionHandler> Handlers { get; }

    public IReadOnlyList<VariableDefinition> Locals { get; }

    public static SpineTemplate Capture(
        List<Instruction> instructions,
        IList<ExceptionHandler> handlers,
        ISet<VariableDefinition> excludedLocals,
        IList<VariableDefinition> bodyVariables) {
        ExpandShortFormLocals(instructions, bodyVariables);

        List<VariableDefinition> locals = [];
        HashSet<VariableDefinition> seen = [];
        foreach (Instruction instruction in instructions) {
            if (instruction.Operand is VariableDefinition variable && !excludedLocals.Contains(variable) && seen.Add(variable)) {
                locals.Add(variable);
            }
        }

        HashSet<Instruction> instructionSet = [.. instructions];
        List<ExceptionHandler> ownedHandlers = new List<ExceptionHandler>();
        foreach (ExceptionHandler handler in handlers) {
            if (handler.TryStart is not null && instructionSet.Contains(handler.TryStart)) {
                ownedHandlers.Add(handler);
            }
        }

        return new SpineTemplate(new List<Instruction>(instructions), ownedHandlers, locals);
    }

    private static void ExpandShortFormLocals(List<Instruction> instructions, IList<VariableDefinition> bodyVariables) {
        foreach (Instruction instruction in instructions) {
            int loadIndex = MacroLocalIndex(instruction.OpCode, load: true);
            if (loadIndex >= 0 && loadIndex < bodyVariables.Count) {
                instruction.OpCode = OpCodes.Ldloc;
                instruction.Operand = bodyVariables[loadIndex];
                continue;
            }

            int storeIndex = MacroLocalIndex(instruction.OpCode, load: false);
            if (storeIndex >= 0 && storeIndex < bodyVariables.Count) {
                instruction.OpCode = OpCodes.Stloc;
                instruction.Operand = bodyVariables[storeIndex];
            }
        }
    }

    private static int MacroLocalIndex(OpCode opCode, bool load) {
        if (load) {
            if (opCode == OpCodes.Ldloc_0) {
                return 0;
            }

            if (opCode == OpCodes.Ldloc_1) {
                return 1;
            }

            if (opCode == OpCodes.Ldloc_2) {
                return 2;
            }

            return opCode == OpCodes.Ldloc_3 ? 3 : -1;
        }

        if (opCode == OpCodes.Stloc_0) {
            return 0;
        }

        if (opCode == OpCodes.Stloc_1) {
            return 1;
        }

        if (opCode == OpCodes.Stloc_2) {
            return 2;
        }

        return opCode == OpCodes.Stloc_3 ? 3 : -1;
    }
}
