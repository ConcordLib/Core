namespace Concord.Detour;

/// <summary>
///     How a target method's detour is owned once Concord has made a routing decision about it.
/// </summary>
public enum RouteState {
    /// <summary>
    ///     No routing decision has been made for the target yet.
    /// </summary>
    Unpinned,

    /// <summary>
    ///     Concord's own detour owns the target's entry point.
    /// </summary>
    Raw,

    /// <summary>
    ///     The target is routed through a foreign patch host, which owns its entry point.
    /// </summary>
    Bridge,

    /// <summary>
    ///     The host refused to route the target and Concord cannot patch it. Later applies throw.
    /// </summary>
    Rejected,

    /// <summary>
    ///     A foreign patcher claimed the target after Concord had detoured it, and promotion to the host
    ///     failed. The host owns the entry point and Concord's injections are not running. Unlike
    ///     <see cref="Rejected" /> this does not throw on later applies: the caller did nothing wrong, an
    ///     unrelated mod arrived late.
    /// </summary>
    ContestedLost
}
