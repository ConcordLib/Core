namespace Concord.Detour;

/// <summary>
///     The outcome of asking a foreign patch host to take over a target method.
/// </summary>
public enum ForeignRouteKind {
    /// <summary>
    ///     No foreign patcher has claimed the target, so Concord can use its own detour.
    /// </summary>
    NotContested,

    /// <summary>
    ///     The host accepted the target and now owns its entry point.
    /// </summary>
    Routed,

    /// <summary>
    ///     The host refused the target because combining the patches could break the method.
    /// </summary>
    Rejected
}
