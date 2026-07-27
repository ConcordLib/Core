namespace Concord.Detour;

/// <summary>
///     What a foreign patch host decided when asked to take over a target method.
/// </summary>
public sealed class ForeignRouteResult {
    private ForeignRouteResult(ForeignRouteKind kind, IDetourHandle? handle, string? reason) {
        Kind = kind;
        Handle = handle;
        Reason = reason;
    }

    /// <summary>
    ///     Which of the three outcomes this result carries.
    /// </summary>
    public ForeignRouteKind Kind { get; }

    /// <summary>
    ///     The handle for the routed injections, set only when <see cref="Kind" /> is
    ///     <see cref="ForeignRouteKind.Routed" />.
    /// </summary>
    public IDetourHandle? Handle { get; }

    /// <summary>
    ///     Why the host refused, set only when <see cref="Kind" /> is <see cref="ForeignRouteKind.Rejected" />.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    ///     Builds a result saying no foreign patcher has claimed the target.
    /// </summary>
    /// <returns>A <see cref="ForeignRouteKind.NotContested" /> result.</returns>
    public static ForeignRouteResult NotContested() => new(ForeignRouteKind.NotContested, null, null);

    /// <summary>
    ///     Builds a result saying the host accepted the target.
    /// </summary>
    /// <param name="handle">The handle that removes the routed injections when disposed.</param>
    /// <returns>A <see cref="ForeignRouteKind.Routed" /> result.</returns>
    public static ForeignRouteResult Routed(IDetourHandle handle) => new(ForeignRouteKind.Routed, handle, null);

    /// <summary>
    ///     Builds a result saying the host refused the target.
    /// </summary>
    /// <param name="reason">Why the target could not be routed.</param>
    /// <returns>A <see cref="ForeignRouteKind.Rejected" /> result.</returns>
    public static ForeignRouteResult Rejected(string reason) => new(ForeignRouteKind.Rejected, null, reason);
}
