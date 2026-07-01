using System.Reflection;
using Concord.Emit;

namespace Concord.Orchestration;

/// <summary>
///     Runtime-adapter-supplied applier that applies an injection for a target method. Concord's scanner
///     calls this; the runtime adapter owns wrapper composition and detour application, so the scanner
///     stays target-runtime-agnostic.
/// </summary>
public interface IPatchApplier {
    /// <summary>
    ///     Applies the given injection as a patch on <paramref name="target" />.
    /// </summary>
    /// <param name="target">The target method to patch.</param>
    /// <param name="injection">The injection composed from the declared injection method.</param>
    void ApplyPatch(MethodBase target, Injection injection);
}
