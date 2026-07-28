using System.Reflection;
using Concord.Emit;

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
    /// <exception cref="ConcordEmitException">Thrown with code <c>CONC116</c> when <paramref name="type" /> is <see langword="null" />.</exception>
    LocalRef DeclareLocal(Type type);

    /// <summary>
    ///     Gets a handle to a local the body already declares, by slot index. Use this to reference a
    ///     local the target computed, where a Harmony transpiler would have written a bare slot number
    ///     such as <c>Ldloc_S, 4</c>. Transpiler-declared locals follow the body's own.
    /// </summary>
    /// <param name="index">The local's slot index.</param>
    /// <returns>A handle usable as the operand of a load or store instruction.</returns>
    /// <exception cref="ConcordEmitException">Thrown with code <c>CONC121</c> when <paramref name="index" /> is out of range.</exception>
    LocalRef GetLocal(int index);
}
