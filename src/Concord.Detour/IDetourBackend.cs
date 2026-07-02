using System.Reflection;
using Concord.Emit;

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

    /// <summary>
    ///     Installs or replaces the single detour for <paramref name="target" />, composing one wrapper from
    ///     every currently-live injection on that target plus <paramref name="added" />. Disposing the returned
    ///     handle removes the injections it owns and recomposes (or removes) the target's detour.
    /// </summary>
    /// <param name="target">The method to redirect.</param>
    /// <param name="added">The injections this call contributes to the target's composed wrapper.</param>
    /// <returns>A handle that reports detour state and removes only the injections it owns when disposed.</returns>
    IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added);
}
