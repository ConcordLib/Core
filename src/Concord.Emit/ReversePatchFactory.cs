using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Binds delegates that call original target method bodies directly.
/// </summary>
public static class ReversePatchFactory {
    /// <summary>
    ///     Creates a delegate for a copy of an original method body.
    /// </summary>
    /// <param name="original">The original method to clone.</param>
    /// <param name="delegateType">
    ///     The delegate type to create. For instance methods, the instance is represented by the first
    ///     delegate parameter.
    /// </param>
    /// <returns>A delegate that invokes the cloned original body and bypasses patched wrappers.</returns>
    public static Delegate Bind(MethodBase original, Type delegateType) {
        MethodInfo clone = OriginalBody.Clone(original);
        return clone.CreateDelegate(delegateType);
    }
}
