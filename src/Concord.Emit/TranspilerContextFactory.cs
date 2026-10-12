using System.Reflection;
using Concord.Emit;

namespace Concord;

/// <summary>
///     Creates a transpiler context outside a compose, for a detour backend that needs to mint branch
///     targets and locals of its own.
/// </summary>
public static class TranspilerContextFactory {
    /// <summary>
    ///     Creates a context for the given method.
    /// </summary>
    /// <param name="original">The method the context describes.</param>
    /// <returns>A context whose <see cref="ITranspilerContext.DefineLabel" /> mints distinct labels.</returns>
    public static ITranspilerContext For(MethodBase original) {
        return new TranspilerContext(original);
    }
}
