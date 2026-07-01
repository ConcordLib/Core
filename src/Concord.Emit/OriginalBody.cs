using System.Reflection;
using MonoMod.Utils;

namespace Concord.Emit;

/// <summary>
///     Creates callable clones of original target method bodies.
/// </summary>
public static class OriginalBody {
    /// <summary>
    ///     Clones an original method into an unmanaged dynamic method that is not affected by Concord detours.
    /// </summary>
    /// <param name="original">The method body to clone.</param>
    /// <returns>
    ///     A callable method with the same effective signature. Instance targets receive the instance as
    ///     the first delegate argument.
    /// </returns>
    public static MethodInfo Clone(MethodBase original) {
        using DynamicMethodDefinition dmd = new DynamicMethodDefinition(original);
        return dmd.Generate();
    }
}
