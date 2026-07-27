using System.Reflection;

namespace Concord;

/// <summary>
///     Services available to a transpiler while it rewrites a method body: the method being patched,
///     plus factories for new branch targets and local variables.
/// </summary>
public interface ITranspilerContext {
    /// <summary>The method being patched, after async and iterator state-machine resolution.</summary>
    MethodBase Original { get; }

    /// <summary>Mints a new branch target. Attach it to an instruction's labels to fix its position.</summary>
    /// <returns>A label that no instruction carries yet.</returns>
    Label DefineLabel();

    /// <summary>Declares a new local variable in the method body.</summary>
    /// <param name="type">The local's type.</param>
    /// <returns>A handle usable as the operand of a load or store instruction.</returns>
    LocalRef DeclareLocal(Type type);
}
