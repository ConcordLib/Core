namespace Concord.Detour;

/// <summary>
///     Stable log prefixes for coexistence events, so players and bug reports can grep one string.
/// </summary>
public static class CoexistenceLogMarkers {
    /// <summary>
    ///     A contested target was handed to the foreign patch host.
    /// </summary>
    public const string RoutedContested = "[Concord.Coex] routed-contested";

    /// <summary>
    ///     A foreign patcher claimed a target after Concord had already detoured it.
    /// </summary>
    public const string LateContention = "[Concord.Coex] late-contention";

    /// <summary>
    ///     The foreign patch host loaded and found a supported patcher version.
    /// </summary>
    public const string BridgeActive = "[Concord.Coex] bridge-active";

    /// <summary>
    ///     The host's instruction stream could not be converted safely and was left unchanged.
    /// </summary>
    public const string StreamRejected = "[Concord.Coex] stream-rejected";

    /// <summary>
    ///     The notifier hook installed, so late contention is recoverable rather than merely reported.
    /// </summary>
    public const string HookInstalled = "[Concord.Coex] hook-installed";

    /// <summary>
    ///     The notifier hook could not install; Concord falls back to poll-based detection.
    /// </summary>
    public const string HookUnavailable = "[Concord.Coex] hook-unavailable";

    /// <summary>
    ///     A target Concord had detoured itself moved onto the host after a foreign patch arrived.
    /// </summary>
    public const string Promoted = "[Concord.Coex] promoted";

    /// <summary>
    ///     Promotion was refused before anything changed, so the target keeps Concord's detour until the
    ///     host's rebuild replaces it.
    /// </summary>
    public const string PromoteRejected = "[Concord.Coex] promote-rejected";

    /// <summary>
    ///     Promotion passed validation but the handover itself failed.
    /// </summary>
    public const string PromoteFailed = "[Concord.Coex] promote-failed";
}
