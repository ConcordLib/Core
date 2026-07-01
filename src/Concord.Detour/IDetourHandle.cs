using System.Reflection;

namespace Concord.Detour;

/// <summary>
///     Represents an applied method detour.
/// </summary>
public interface IDetourHandle : IDisposable {
    /// <summary>
    ///     Gets the method that was redirected by this handle.
    /// </summary>
    MethodBase Original { get; }

    /// <summary>
    ///     Gets a value indicating whether the detour is currently applied.
    /// </summary>
    bool IsApplied { get; }
}
