using System.Diagnostics;

namespace Concord.Detour;

/// <summary>
///     Where Concord reports a patch event that is not fatal but that some mod author still has to
///     see. An evicted <c>[Local]</c> is the main one: the injection is gone and nothing throws, so
///     without this the owning mod has no way to learn why its code stopped running.
/// </summary>
internal static class PatchLog {
    /// <summary>
    ///     The host's log. <c>Patcher.UseLog</c> is the public door onto this; unset, reports go to
    ///     <see cref="Trace" /> instead.
    /// </summary>
    internal static Action<string>? Sink { get; set; }

    internal static void Write(string message) {
        Action<string>? sink = Sink;

        // Trace, not Debug: Release defines TRACE but not DEBUG, so [Conditional("DEBUG")] would
        // strip the fallback out of the build that ships and the report would vanish.
        if (sink is null) {
            Trace.WriteLine(message);
            return;
        }

        sink(message);
    }
}
