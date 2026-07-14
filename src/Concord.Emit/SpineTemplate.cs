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

    public static SpineTemplate Capture(List<Instruction> instructions, IList<ExceptionHandler> handlers, ISet<VariableDefinition> excludedLocals) {
        List<VariableDefinition> locals = [];
        HashSet<VariableDefinition> seen = [];
        foreach (Instruction instruction in instructions) {
            if (instruction.Operand is VariableDefinition variable && !excludedLocals.Contains(variable) && seen.Add(variable)) {
                locals.Add(variable);
            }
        }

        HashSet<Instruction> instructionSet = [.. instructions];
        List<ExceptionHandler> ownedHandlers = [];
        foreach (ExceptionHandler handler in handlers) {
            if (handler.TryStart is not null && instructionSet.Contains(handler.TryStart)) {
                ownedHandlers.Add(handler);
            }
        }

        return new SpineTemplate(new List<Instruction>(instructions), ownedHandlers, locals);
    }
}
