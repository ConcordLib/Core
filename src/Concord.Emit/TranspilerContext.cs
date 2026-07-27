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
            throw new ArgumentNullException(nameof(type));
        }

        declaredLocals.Add(type);
        return LocalRefFactory.Create(ExistingLocalCount + declaredLocals.Count - 1, type);
    }
}
