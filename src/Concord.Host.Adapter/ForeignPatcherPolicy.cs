using System.Collections.Concurrent;
using System.Reflection;
using Concord.Detour;

namespace Concord.Host.Adapter;

/// <summary>
///     Whether this backend may publish a body for a target, given who else has patched it. Harmony's claim is the
///     only one checked, because it is the only patcher known to make a method reject a replaced body afterwards.
/// </summary>
public enum ForeignHandling {
    /// <summary>Nobody else holds the target, so a composed body may be written to it.</summary>
    Allow,

    /// <summary>Harmony holds the target, so no body is written and the caller gets an inert handle.</summary>
    Disown,
}

/// <summary>
///     Judges whether Harmony has a claim on a target. A method Harmony has patched never accepts a replaced body
///     again, in either order, so one it owns is disowned here instead of patched.
/// </summary>
public static class ForeignPatcherPolicy {
    /// <summary>The prefix on every log line about a target shared with Harmony.</summary>
    public const string ContestedMarker = "[Concord.Waylith] foreign-contested";

    /// <summary>The prefix on the one log line written when Harmony is loaded but cannot be questioned.</summary>
    public const string ProbeFailedMarker = "[Concord.Waylith] foreign-probe-failed";

    private const string HarmonyAssemblyPrefix = "0Harmony,";

    private const string NoBody = "Concord has not patched it";

    private const string WithdrawnBody = "Concord published a composed body for it and has withdrawn that body";

    private const string StrandedBody = "Concord's composed body is still installed on it, sits under the foreign " +
        "detour, never runs, and comes off at the next patch change on that target";

    private static readonly ForeignVerdict Allowed = new ForeignVerdict(ForeignHandling.Allow, string.Empty, null);

    private static readonly ConcurrentDictionary<MethodBase, MethodBase> Published = new ConcurrentDictionary<MethodBase, MethodBase>();

    private static readonly ConcurrentDictionary<MethodBase, byte> Withdrawn = new ConcurrentDictionary<MethodBase, byte>();

    private static readonly ConcurrentDictionary<MethodBase, byte> Announced = new ConcurrentDictionary<MethodBase, byte>();

    private static Func<MethodBase, IReadOnlyList<string>?>? bound;

    private static int probeFailed;

    private static int scans;

    /// <summary>Overrides how foreign owners are looked up. Left <c>null</c>, Harmony is found and questioned by reflection. Tests set it.</summary>
    public static Func<MethodBase, IReadOnlyList<string>?>? Owners { get; set; }

    /// <summary>Where the probe-failure line goes. Defaults to the backend's own log sink.</summary>
    public static Action<string> Log { get; set; } = static message => WaylithDetourBackend.Log(message);

    /// <summary>Whether asking Harmony threw once. From then on no target is judged contested, and that is reported once.</summary>
    public static bool ProbeFailed => Volatile.Read(ref probeFailed) != 0;

    /// <summary>How many times the loaded assemblies have been swept looking for Harmony. One sweep per judged target until it is found.</summary>
    public static int Scans => Volatile.Read(ref scans);

    /// <summary>Decides whether a body may be published for <paramref name="target" />, and remembers the answer.</summary>
    /// <param name="target">The method about to be patched.</param>
    /// <returns>Allow when the target is free, Disown once Harmony holds it or it has been disowned before.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static ForeignVerdict Judge(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        MethodBase key = Key(target);
        if (Withdrawn.TryGetValue(key, out byte hadBody)) {
            return new ForeignVerdict(
                ForeignHandling.Disown, Describe(target, null, hadBody != 0 ? WithdrawnBody : NoBody), null);
        }

        IReadOnlyList<string>? owners = Probe().Owners(target);
        if (owners is null) {
            Published.TryAdd(key, target);
            return Allowed;
        }

        bool published = Published.TryRemove(key, out _);
        Withdrawn.TryAdd(key, published ? (byte)1 : (byte)0);
        Announced.TryAdd(key, 0);
        return new ForeignVerdict(
            ForeignHandling.Disown, Describe(target, owners, published ? WithdrawnBody : NoBody), owners);
    }

    /// <summary>Re-checks every target this backend has published a body to, catching the ones Harmony took afterwards.</summary>
    /// <returns>One line per newly contested target, each reported only once. Empty when nothing new is contested or Harmony is absent.</returns>
    public static IReadOnlyList<string> Contentions() {
        if (Published.IsEmpty) {
            return [];
        }

        HarmonyProbe probe = Probe();
        if (!probe.Bound) {
            return [];
        }

        List<string> found = [];
        foreach (KeyValuePair<MethodBase, MethodBase> entry in Published) {
            IReadOnlyList<string>? owners = probe.Owners(entry.Value);
            if (owners is null) {
                continue;
            }

            bool held = Published.TryRemove(entry.Key, out _);
            Withdrawn.TryAdd(entry.Key, held ? (byte)1 : (byte)0);
            if (held && Announced.TryAdd(entry.Key, 0)) {
                found.Add(Describe(entry.Value, owners, StrandedBody));
            }
        }

        return found;
    }

    /// <summary>Stops watching <paramref name="target" />. Call it when a body comes off, so a later Harmony patch is not reported as a clash.</summary>
    /// <param name="target">The method no longer carrying a composed body.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static void Forget(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        Published.TryRemove(Key(target), out _);
    }

    /// <summary>Asks Harmony who owns <paramref name="target" />, recording nothing.</summary>
    /// <param name="target">The method to ask about.</param>
    /// <returns>The owner ids, empty when Harmony patched and unpatched it, or <c>null</c> when it never did, Harmony is absent, or the probe failed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static IReadOnlyList<string>? HarmonyOwners(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Probe().Owners(target);
    }

    /// <summary>Whether <paramref name="target" /> has already been given up on. Once disowned it stays disowned for the process.</summary>
    /// <param name="target">The method to test.</param>
    /// <returns><c>true</c> when an earlier judgement disowned it, so no body will be written to it again.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static bool IsDisowned(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Withdrawn.ContainsKey(Key(target));
    }

    /// <summary>The handle to hand a caller whose target was disowned, so the patch call still returns something usable.</summary>
    /// <param name="target">The method that was left alone.</param>
    /// <returns>A handle that reports <c>IsApplied</c> as <c>false</c> and does nothing when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static IDetourHandle Disowned(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return new DisownedHandle(Key(target));
    }

    /// <summary>Clears every table and setting back to a fresh process's state. For tests, never for a running game.</summary>
    public static void ResetForTests() {
        Published.Clear();
        Withdrawn.Clear();
        Announced.Clear();
        Owners = null;
        Log = static message => WaylithDetourBackend.Log(message);
        bound = null;
        Volatile.Write(ref probeFailed, 0);
        Volatile.Write(ref scans, 0);
    }

    internal static HarmonyProbe Probe() {
        if (Volatile.Read(ref probeFailed) != 0) {
            return default;
        }

        return new HarmonyProbe(Owners ?? Bind());
    }

    private static MethodBase Key(MethodBase target) {
        return BodyScope.KeyOf(target);
    }

    private static IReadOnlyList<string>? Ask(Func<MethodBase, IReadOnlyList<string>?> probe, MethodBase target) {
        if (Volatile.Read(ref probeFailed) != 0) {
            return null;
        }

        try {
            return probe(target);
        } catch (Exception failure) when (failure is not OutOfMemoryException) {
            Fail(failure);
            return null;
        }
    }

    private static Func<MethodBase, IReadOnlyList<string>?>? Bind() {
        if (bound is not null || Volatile.Read(ref probeFailed) != 0) {
            return bound;
        }

        try {
            Assembly? harmony = FindHarmony();
            if (harmony is null) {
                return null;
            }

            MethodInfo? info = harmony
                .GetType("HarmonyLib.PatchProcessor", false)
                ?.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static, [typeof(MethodBase)]);
            PropertyInfo? owners = harmony.GetType("HarmonyLib.Patches", false)?.GetProperty("Owners");
            if (info is null || owners is null) {
                Fail(new MissingMemberException(
                    $"'{harmony.FullName}' has no 'HarmonyLib.PatchProcessor.GetPatchInfo(MethodBase)' or no " +
                    "'HarmonyLib.Patches.Owners', so it is not a Harmony build Concord knows how to question"));
                return null;
            }

            bound = target => {
                object? patches = info.Invoke(null, [target]);
                return patches is null ? null : owners.GetValue(patches) as IReadOnlyList<string> ?? [];
            };

            return bound;
        } catch (Exception failure) when (failure is not OutOfMemoryException) {
            Fail(failure);
            return null;
        }
    }

    private static Assembly? FindHarmony() {
        Interlocked.Increment(ref scans);
        foreach (Assembly candidate in AppDomain.CurrentDomain.GetAssemblies()) {
            if ((candidate.FullName ?? string.Empty).StartsWith(HarmonyAssemblyPrefix, StringComparison.Ordinal)) {
                return candidate;
            }
        }

        return null;
    }

    private static void Fail(Exception failure) {
        if (Interlocked.Exchange(ref probeFailed, 1) != 0) {
            return;
        }

        bound = null;
        Log(
            $"{ProbeFailedMarker} Harmony is loaded but Concord could not ask it which methods it owns: " +
            $"{failure.GetType().Name}, {failure.Message.TrimEnd('.')}. Concord cannot tell whether a target is " +
            "shared with Harmony from here on, and a method Harmony owns will keep a replaced body that never " +
            $"runs. Set {AdapterEntryPoint.SwitchVariable}=0 to run on the native detour backend, which coexists " +
            "with Harmony through Concord's bridge.");
    }

    private static string Describe(MethodBase target, IReadOnlyList<string>? owners, string didAction) {
        string named = $"'{target.DeclaringType?.FullName ?? target.Module.Name}.{target.Name}'";
        string who = owners switch {
            null => "Harmony owns it",
            { Count: 0 } => "Harmony patched it and then unpatched it, and its wrapper stays on the method for the " +
                "rest of the process",
            _ => $"Harmony owns it on behalf of [{string.Join(", ", owners)}]",
        };

        return $"{ContestedMarker} {named}: {who}. {didAction}. A method Harmony has touched never accepts a " +
            "replaced body again, in either order, and a second detour over Harmony's would leave one method with " +
            $"two owners, so Concord leaves it alone. Set {AdapterEntryPoint.SwitchVariable}=0 to run this game on " +
            "the native detour backend, which shares a method with Harmony through Concord's bridge.";
    }

    internal readonly struct HarmonyProbe {
        private readonly Func<MethodBase, IReadOnlyList<string>?>? probe;

        internal HarmonyProbe(Func<MethodBase, IReadOnlyList<string>?>? probe) {
            this.probe = probe;
        }

        internal bool Bound => probe is not null;

        internal IReadOnlyList<string>? Owners(MethodBase target) {
            return probe is null ? null : Ask(probe, target);
        }
    }

    private sealed class DisownedHandle : IDetourHandle {
        internal DisownedHandle(MethodBase original) {
            Original = original;
        }

        public MethodBase Original { get; }

        public bool IsApplied => false;

        public void Dispose() {
        }
    }
}

/// <summary>The answer <see cref="ForeignPatcherPolicy.Judge" /> gives for one target.</summary>
public sealed class ForeignVerdict {
    internal ForeignVerdict(ForeignHandling handling, string message, IReadOnlyList<string>? owners) {
        Handling = handling;
        Message = message;
        Owners = owners;
    }

    /// <summary>Either <see cref="ForeignHandling.Allow" /> or <see cref="ForeignHandling.Disown" />.</summary>
    public ForeignHandling Handling { get; }

    /// <summary>The log line for a disowned target: the clash, what became of any body on it, and the way to patch it anyway. Empty when allowed.</summary>
    public string Message { get; }

    /// <summary>The Harmony owner ids holding the target. <c>null</c> when none hold it, and empty when it was patched and then unpatched.</summary>
    public IReadOnlyList<string>? Owners { get; }
}
