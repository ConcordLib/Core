using System.Reflection;

namespace Concord.Detour;

/// <summary>
///     Applies native method detours for Concord.
/// </summary>
public interface IDetourBackend {
    /// <summary>
    ///     Redirects calls from a target method to its wrapper.
    /// </summary>
    /// <param name="original">The method to redirect.</param>
    /// <param name="replacement">The wrapper to execute instead.</param>
    /// <returns>A handle that reports detour state and restores the original method when disposed.</returns>
    IDetourHandle Apply(MethodBase original, MethodInfo replacement);
}
