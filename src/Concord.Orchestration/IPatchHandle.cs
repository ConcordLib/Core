using Concord.Detour;

namespace Concord;

/// <summary>
///     A disposable handle over one or more applied detours. Disposing reverts every detour the handle owns.
/// </summary>
public interface IPatchHandle : IDisposable {
    /// <summary>
    ///     Gets a value indicating whether the handle's detours are currently applied.
    ///     Returns true only while the handle is undisposed and all underlying detours are applied.
    /// </summary>
    bool IsApplied { get; }

    /// <summary>
    ///     Gets the underlying detour handles, so callers can inspect or revert them individually.
    /// </summary>
    IReadOnlyList<IDetourHandle> Detours { get; }
}
