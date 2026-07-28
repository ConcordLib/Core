using System.Reflection;
using Mono.Cecil.Cil;

namespace Concord.Emit;

internal sealed class TranspilerContext : ITranspilerContext {
    private readonly List<Type> declaredLocals = new List<Type>();

    internal TranspilerContext(MethodBase original) {
        Original = original;
    }

    public MethodBase Original { get; }

    internal int NextLabelId { get; set; }

    internal int ExistingLocalCount { get; set; }

    // Recorded so GetLocal can hand back a typed handle for a local the body already declares,
    // not just for the ones a transpiler declares itself.
    internal List<Type> ExistingLocalTypes { get; } = new List<Type>();

    internal IReadOnlyList<Type> DeclaredLocals => declaredLocals;

    internal Dictionary<Instruction, Label> LabelsByInstruction { get; } = new Dictionary<Instruction, Label>();

    // Cecil's HandlerEnd is null when a handler runs to the literal end of the method body - there is
    // no instruction to attach a closing ExceptionBlock to, so the marker is still attached to a real
    // instruction (the body's last one) but tracked here by reference. WriteBack consults this set to
    // restore null instead of truncating the handler by one instruction.
    internal HashSet<ExceptionBlock> EndOfBodyBlocks { get; } = new HashSet<ExceptionBlock>();

    public Label DefineLabel() {
        return LabelFactory.Create(NextLabelId++);
    }

    public LocalRef DeclareLocal(Type type) {
        if (type is null) {
            throw new ConcordEmitException("CONC116", "DeclareLocal was called with a null type; a transpiler-declared local must name a concrete type.");
        }

        declaredLocals.Add(type);
        return LocalRefFactory.Create(ExistingLocalCount + declaredLocals.Count - 1, type);
    }

    public LocalRef GetLocal(int index) {
        int total = ExistingLocalCount + declaredLocals.Count;
        if (index < 0 || index >= total) {
            throw new ConcordEmitException(
                "CONC121",
                $"Local slot {index} is out of range for a body with {total} local(s).");
        }

        Type type = index < ExistingLocalCount
            ? ExistingLocalTypes[index]
            : declaredLocals[index - ExistingLocalCount];

        return LocalRefFactory.Create(index, type);
    }
}
