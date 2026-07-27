using System.Reflection;

namespace Concord.Detour;

/// <summary>
///     Resolves a method to one canonical <see cref="MethodBase" /> so the same target reached through
///     different reflection paths compares and hashes as a single key.
/// </summary>
public static class MethodIdentity {
    /// <summary>
    ///     Returns the canonical <see cref="MethodBase" /> for a target, or the target itself when it has no
    ///     canonical form (open generics, module-level methods, runtimes without handle resolution).
    /// </summary>
    /// <param name="target">The method to normalize.</param>
    /// <returns>The canonical instance to use as a routing key.</returns>
    public static MethodBase Normalize(MethodBase target) {
        if (target.DeclaringType == null || target.DeclaringType.IsGenericTypeDefinition || target.IsGenericMethodDefinition) {
            return target;
        }

        try {
            return MethodBase.GetMethodFromHandle(target.MethodHandle, target.DeclaringType.TypeHandle) ?? target;
        } catch (NotSupportedException) {
            return target;
        }
    }
}
