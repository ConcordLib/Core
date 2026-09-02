using System.Reflection;

namespace Concord.Detour;

/// <summary>
///     Reads back which patch owners are live on a target, across both places an injection can end up:
///     Concord's own detour registry, and a foreign patch host that took the target over.
/// </summary>
internal static class PatchOwners {
    internal static IForeignPatchHost? Host { get; set; }

    internal static IReadOnlyList<string> Of(MethodBase target) {
        IReadOnlyList<string> own = TargetDetourRegistry.OwnersFor(target);
        IForeignPatchHost? host = Host;
        IReadOnlyList<string> routed = host == null ? [] : host.ConcordOwners(target);

        if (routed.Count == 0) {
            return own;
        }

        if (own.Count == 0) {
            return routed;
        }

        List<string> both = new List<string>(own);
        foreach (string owner in routed) {
            if (!both.Contains(owner)) {
                both.Add(owner);
            }
        }

        return both;
    }
}
