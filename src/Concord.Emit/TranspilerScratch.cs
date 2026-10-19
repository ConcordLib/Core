using System.Reflection.Emit;
using MonoMod.Utils;

namespace Concord.Emit;

/// <summary>
///     A throwaway method to hand a foreign transpiler as its generator, whose declared locals stay readable.
/// </summary>
public sealed class TranspilerScratch : IDisposable {
    private readonly DynamicMethodDefinition definition;

    private readonly ILGenerator generator;

    /// <summary>Initializes a new instance of the <see cref="TranspilerScratch" /> class.</summary>
    public TranspilerScratch() {
        definition = new DynamicMethodDefinition("ConcordTranspilerScratch", typeof(void), Type.EmptyTypes);
        generator = definition.GetILGenerator();
    }

    /// <summary>The generator to hand the transpiler. The same instance every time.</summary>
    public ILGenerator Generator => generator;

    /// <summary>The type of every local declared on <see cref="Generator" />, by slot, null where the type does not resolve.</summary>
    public IReadOnlyList<Type?> DeclaredLocalTypes {
        get {
            List<Type?> types = [];
            foreach (Mono.Cecil.Cil.VariableDefinition variable in definition.Definition.Body.Variables) {
                types.Add(ResolveOrNull(variable));
            }

            return types;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        definition.Dispose();
    }

    private static Type? ResolveOrNull(Mono.Cecil.Cil.VariableDefinition variable) {
        try {
            return variable.VariableType.ResolveReflection();
        } catch (Exception) {
            return null;
        }
    }
}
