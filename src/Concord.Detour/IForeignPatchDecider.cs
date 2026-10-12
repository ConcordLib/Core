using System.Reflection;

namespace Concord.Detour;

/// <summary>
///     Decides whether a foreign patch host's pending rebuild can be skipped because Concord already
///     owns the target's body. Implemented alongside <see cref="IForeignPatchObserver" />.
/// </summary>
public interface IForeignPatchDecider {
    /// <summary>
    ///     Called before <see cref="IForeignPatchObserver.OnForeignPatchPending" />, while the host holds
    ///     its own lock. Implementations must not throw: an exception escapes into the foreign library.
    /// </summary>
    /// <param name="target">The method the host is about to rebuild.</param>
    /// <param name="hostPatchState">The host's patch record for this rebuild.</param>
    /// <returns>True when Concord's body already runs every patch in that record.</returns>
    bool TrySuppressForeignRebuild(MethodBase target, object hostPatchState);
}
