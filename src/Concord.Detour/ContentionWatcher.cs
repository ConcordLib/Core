using System.Reflection;

namespace Concord.Detour;

/// <summary>
///     Reports methods where Concord's injections are not running because a foreign patcher owns the
///     entry point. With the notifier hook installed this should only ever be a target whose promotion
///     failed; without it, every late foreign patch lands here and nothing can recover them.
/// </summary>
public sealed class ContentionWatcher {
    private readonly Func<IReadOnlyCollection<MethodBase>> rawPinnedTargets;
    private readonly Func<IReadOnlyCollection<MethodBase>> contestedLostTargets;
    private readonly Func<MethodBase, IReadOnlyList<string>> foreignOwners;
    private readonly Func<bool> notifierInstalled;
    private readonly Action<string> warn;
    private readonly Action<string> dialogOnce;
    private readonly HashSet<MethodBase> seenDialogTargets = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="ContentionWatcher" /> class.
    /// </summary>
    /// <param name="rawPinnedTargets">Supplies the targets Concord currently owns with its own detour.</param>
    /// <param name="contestedLostTargets">Supplies the targets whose promotion to the host failed.</param>
    /// <param name="foreignOwners">Looks up the foreign patch owners on a target.</param>
    /// <param name="notifierInstalled">Reports whether the host's notifier hook is active.</param>
    /// <param name="warn">Writes a warning to the host game's log.</param>
    /// <param name="dialogOnce">Shows a player-facing dialog, called at most once per target.</param>
    public ContentionWatcher(
        Func<IReadOnlyCollection<MethodBase>> rawPinnedTargets,
        Func<IReadOnlyCollection<MethodBase>> contestedLostTargets,
        Func<MethodBase, IReadOnlyList<string>> foreignOwners,
        Func<bool> notifierInstalled,
        Action<string> warn,
        Action<string> dialogOnce) {
        this.rawPinnedTargets = rawPinnedTargets;
        this.contestedLostTargets = contestedLostTargets;
        this.foreignOwners = foreignOwners;
        this.notifierInstalled = notifierInstalled;
        this.warn = warn;
        this.dialogOnce = dialogOnce;
    }

    /// <summary>
    ///     Checks once for methods Concord has lost, and reports each with the reason it was lost.
    /// </summary>
    public void RunCheckpoint() {
        foreach (MethodBase target in contestedLostTargets()) {
            Report(target, "Concord could not hand its injections to the foreign patcher");
        }

        // With the notifier installed, a raw target that a foreign patcher has claimed would have been
        // promoted already, so there is nothing here to find. Only the degraded path needs this sweep.
        if (notifierInstalled()) {
            return;
        }

        foreach (MethodBase target in rawPinnedTargets()) {
            Report(target, "the notifier hook is not installed, so Concord could not react when the foreign patch arrived");
        }
    }

    private void Report(MethodBase target, string reason) {
        try {
            IReadOnlyList<string> owners = foreignOwners(target);
            if (owners.Count == 0) {
                return;
            }

            string message = $"{CoexistenceLogMarkers.LateContention} {target} patched by [{string.Join(", ", owners)}]. " +
                             $"Concord injections on this method are not running: {reason}.";
            warn(message);

            if (seenDialogTargets.Add(target)) {
                dialogOnce(message);
            }
        } catch (Exception ex) {
            warn($"{CoexistenceLogMarkers.LateContention} foreign-owner lookup failed for {target}: {ex.Message}");
        }
    }
}
