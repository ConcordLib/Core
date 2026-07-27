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
