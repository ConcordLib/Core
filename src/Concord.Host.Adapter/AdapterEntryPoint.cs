using System.Diagnostics;
using System.Reflection;
using Concord.Detour;

namespace Concord.Host.Adapter;

/// <summary>What an environment variable's raw text asked for, once read.</summary>
public enum SwitchState {
    /// <summary>The variable is missing or blank, so whatever the host or the default decides wins.</summary>
    Unset,

    /// <summary>The text reads as on, one of <c>1</c>, <c>true</c>, <c>on</c>, <c>yes</c> or <c>enabled</c>.</summary>
    ForceWaylith,

    /// <summary>The text reads as off, one of <c>0</c>, <c>false</c>, <c>off</c>, <c>no</c> or <c>disabled</c>.</summary>
    ForceDetour,

    /// <summary>The text is neither on nor off. It is logged once and then treated as unset.</summary>
    Unreadable,
}

/// <summary>
///     What a host mod calls to put the body-replacement backend in front of the native detour. Call it once, before
///     the first patch applies: a swap after that would strand every patched target on the old backend.
/// </summary>
public static class AdapterEntryPoint {
    /// <summary>Environment variable that forces the backend either way, overriding the host's setting.</summary>
    public const string SwitchVariable = "CONCORD_WAYLITH_BACKEND";

    /// <summary>Environment variable that drops the offset map from every written body, so frames report raw offsets.</summary>
    public const string EmptyMapVariable = "CONCORD_WAYLITH_EMPTY_MAP";

    private static readonly string[] On = ["1", "true", "on", "yes", "enabled"];

    private static readonly string[] Off = ["0", "false", "off", "no", "disabled"];

    private static readonly object Gate = new object();

    private static readonly Func<int> ConcordPatchedTargets = static () => Patcher.PatchedTargetCount;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    private static string? warnedEmptyMap;

    private static Timer? poll;

    /// <summary>The choice a completed install made, or <c>null</c> before the first install. A second install returns this one unchanged.</summary>
    public static Selection? Current { get; private set; }

    /// <summary>Overrides the count of already-patched targets the install checks before swapping. Left <c>null</c>, Concord's own count is used.</summary>
    public static Func<int>? PatchedTargets { get; set; }

    /// <summary>How often to re-check for a foreign patcher taking a target. Set it to zero before installing to skip the poll. Defaults to five seconds.</summary>
    public static TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    /// <summary>Installs the backend, probing this runtime for body-replacement support.</summary>
    /// <param name="log">Where to write the one selection line. <c>null</c> sends it to <see cref="Trace" />.</param>
    /// <param name="setting">The host's own preference, or <c>null</c> to take the default for this runtime.</param>
    /// <param name="assemblyDirectories">Directories to find a byte-loaded mod assembly's file in. Empty or <c>null</c> logs a warning and leaves [Local(Name)] unresolvable.</param>
    /// <returns>The choice made. Inspect <see cref="Selection.Waylith" /> to see whether body replacement is live.</returns>
    public static Selection Install(Action<string>? log, bool? setting, string[]? assemblyDirectories) {
        return Install(log, setting, assemblyDirectories, RuntimeCapability.Support, null);
    }

    /// <summary>Installs the backend with the capability verdict and the backend factory supplied, for tests and unusual hosts.</summary>
    /// <param name="log">Where to write the one selection line. <c>null</c> sends it to <see cref="Trace" />.</param>
    /// <param name="setting">The host's own preference, or <c>null</c> to take the default for this runtime.</param>
    /// <param name="assemblyDirectories">Directories to find a byte-loaded mod assembly's file in.</param>
    /// <param name="support">The capability verdict to trust instead of probing the runtime.</param>
    /// <param name="backend">Wraps the current native backend into the one to install. <c>null</c> builds the standard one, which needs a registered body publisher.</param>
    /// <returns>The choice made, which falls back to the native detour when a target is already patched, another backend is installed, or no publisher is registered.</returns>
    public static Selection Install(
        Action<string>? log,
        bool? setting,
        string[]? assemblyDirectories,
        BodyReplacement support,
        Func<IDetourBackend, IDetourBackend>? backend) {
        lock (Gate) {
            if (Current is { } already) {
                return already;
            }

            Action<string> write = log ?? (message => Trace.WriteLine(message));
            RegisterLocalNameResolver(assemblyDirectories, write);

            Selection chosen = Choose(setting, support, write);
            if (chosen.Waylith) {
                chosen = Swap(chosen, backend, write);
            }

            write(chosen.Message);
            Current = chosen;
            if (chosen.Waylith && PollInterval > TimeSpan.Zero) {
                poll = new Timer(_ => Report(write), null, PollInterval, PollInterval);
            }

            return chosen;
        }
    }

    /// <summary>Reads one environment variable's text, case-insensitively and ignoring surrounding space.</summary>
    /// <param name="raw">The variable's value, or <c>null</c> when it is not set.</param>
    /// <returns>Unset for null or blank, ForceWaylith or ForceDetour for a word it knows, Unreadable for anything else.</returns>
    public static SwitchState ReadSwitch(string? raw) {
        if (raw is null) {
            return SwitchState.Unset;
        }

        string trimmed = raw.Trim();
        if (trimmed.Length == 0) {
            return SwitchState.Unset;
        }

        if (Matches(On, trimmed)) {
            return SwitchState.ForceWaylith;
        }

        return Matches(Off, trimmed) ? SwitchState.ForceDetour : SwitchState.Unreadable;
    }

    /// <summary>Whether <see cref="EmptyMapVariable" /> asks for bodies to be written with no offset map.</summary>
    /// <param name="log">Where to warn about text that reads as neither on nor off. <c>null</c> sends it to <see cref="Trace" />.</param>
    /// <returns><c>true</c> only when the variable reads as on. Text it cannot read warns once and returns <c>false</c>.</returns>
    public static bool EmptyMapRequested(Action<string>? log) {
        string raw = Environment.GetEnvironmentVariable(EmptyMapVariable) ?? string.Empty;
        SwitchState requested = ReadSwitch(raw);

        if (requested != SwitchState.Unreadable) {
            return requested == SwitchState.ForceWaylith;
        }

        if (!string.Equals(warnedEmptyMap, raw, StringComparison.Ordinal)) {
            warnedEmptyMap = raw;
            (log ?? (message => Trace.WriteLine(message)))(
                Selection.Prefix + EmptyMapVariable + " is set to '" + raw + "', which is neither " +
                string.Join("/", On) + " nor " + string.Join("/", Off) + ". Ignoring it, so every body keeps " +
                "the sentinel offset map that tells injected code apart from the target's own il.");
        }

        return false;
    }

    /// <summary>Builds the lookup that turns a module into the file on disk it was loaded from, searching the directories in order.</summary>
    /// <param name="directories">Directories to search. Copied, so a later change to the array is not picked up.</param>
    /// <returns>A lookup returning the first path that exists, or <c>null</c> when the module's file is in none of them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directories" /> is <c>null</c>.</exception>
    public static Func<Module, string?> LocalNameResolver(string[] directories) {
        ArgumentNullException.ThrowIfNull(directories);
        string[] copy = (string[])directories.Clone();

        return module => {
            foreach (string directory in copy) {
                string candidate = Path.Combine(directory, module.ScopeName);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }

            return null;
        };
    }

    /// <summary>Forgets the installed selection and stops the poll, waiting for a callback already running. For tests, never for a running game.</summary>
    public static void ResetForTests() {
        lock (Gate) {
            Timer? running = Interlocked.Exchange(ref poll, null);
            if (running is not null) {
                using ManualResetEvent drained = new ManualResetEvent(false);
                if (running.Dispose(drained)) {
                    drained.WaitOne();
                }
            }

            Current = null;
            warnedEmptyMap = null;
            PollInterval = DefaultPollInterval;
        }
    }

    /// <summary>Weighs the environment variable, the host setting and the runtime's support into one choice, installing nothing.</summary>
    /// <param name="setting">The host's own preference, or <c>null</c> to default to on.</param>
    /// <param name="support">What this runtime can do, as probed.</param>
    /// <param name="log">Where to warn about unreadable variable text. <c>null</c> sends it to <see cref="Trace" />.</param>
    /// <returns>The choice, with a reason naming which of the three decided it.</returns>
    public static Selection Choose(bool? setting, BodyReplacement support, Action<string>? log) {
        Action<string> write = log ?? (message => Trace.WriteLine(message));
        string raw = Environment.GetEnvironmentVariable(SwitchVariable) ?? string.Empty;
        SwitchState requested = ReadSwitch(raw);

        if (requested == SwitchState.Unreadable) {
            write(
                Selection.Prefix + SwitchVariable + " is set to '" + raw + "', which is neither " +
                string.Join("/", On) + " nor " + string.Join("/", Off) + ". Ignoring it.");
            requested = SwitchState.Unset;
        }

        string source = requested switch {
            SwitchState.ForceWaylith or SwitchState.ForceDetour => SwitchVariable + "=" + raw.Trim(),
            _ => setting.HasValue ? "the mod setting" : "the default for this runtime",
        };

        bool wanted = requested switch {
            SwitchState.ForceWaylith => true,
            SwitchState.ForceDetour => false,
            _ => setting ?? true,
        };

        if (!wanted) {
            return new Selection(false, "turned off by " + source + ".");
        }

        string explained = RuntimeCapability.Explain(support, RuntimeCapability.ProbeFailure) + ".";
        if (support == BodyReplacement.Available) {
            return new Selection(true, "turned on by " + source + ", and " + explained);
        }

        return new Selection(
            false,
            requested == SwitchState.ForceWaylith
                ? source + " asked for it, but it cannot run here: " + explained
                : explained);
    }

    private static void Report(Action<string> write) {
        try {
            foreach (string line in ForeignPatcherPolicy.Contentions()) {
                write(line);
            }
        } catch (Exception failure) when (failure is not OutOfMemoryException) {
            Stop();
            Trace.WriteLine(
                Selection.Prefix + "the poll that asks whether a foreign patcher has taken a method Concord " +
                "published a body to threw " + failure.GetType().Name + ", " + failure.Message.TrimEnd('.') +
                ". It is stopped, so a foreign patcher arriving from here on is not reported.");
        }
    }

    private static void Stop() {
        Timer? existing = Interlocked.Exchange(ref poll, null);
        existing?.Dispose();
    }

    private static Selection Swap(
        Selection chosen, Func<IDetourBackend, IDetourBackend>? backend, Action<string> write) {
        int patched = (PatchedTargets ?? ConcordPatchedTargets)();
        if (patched != 0) {
            return new Selection(
                false,
                patched + " target(s) already carry a composed wrapper, and a swap now strands every one of them on " +
                "the native detour with nothing recording that it happened. Install before the first patch applies. " +
                chosen.Reason);
        }

        IDetourBackend existing = DetourBackend.Current;
        RoutingDetourBackend? router = existing as RoutingDetourBackend;
        IDetourBackend inner = router is null ? existing : router.Inner;

        if (inner is not MonoModDetourBackend) {
            return new Selection(
                false,
                "another backend, '" + inner.GetType().FullName + "', is already installed" +
                (router is null ? string.Empty : " under '" + typeof(RoutingDetourBackend).FullName + "'") +
                ", and installing over it would strip whatever wrapped it. " + chosen.Reason);
        }

        if (backend is null && WaylithDetourBackend.Publisher is null) {
            return new Selection(
                false,
                "no body publisher is registered on " + nameof(WaylithDetourBackend) + ", so the backend has " +
                "nothing to write a method body with. " + chosen.Reason);
        }

        WaylithDetourBackend.Log = write;
        IDetourBackend installed = backend is null ? new WaylithDetourBackend(inner) : backend(inner);
        if (router is null) {
            DetourBackend.Current = installed;
        } else {
            router.ReplaceInner(installed);
        }

        return chosen;
    }

    private static void RegisterLocalNameResolver(string[]? assemblyDirectories, Action<string> write) {
        if (assemblyDirectories is null || assemblyDirectories.Length == 0) {
            write(
                Selection.Prefix + "no assembly directories were supplied, so a mod assembly this host byte-loads " +
                "has no file on disk for Concord to read symbols or a method body from. [Local(Name = ...)] on a " +
                "target in one reports CONC151, and any check that needs the file cannot run.");
            return;
        }

        Patcher.UseLocalNameResolver(LocalNameResolver(assemblyDirectories));
    }

    private static bool Matches(string[] known, string value) {
        foreach (string candidate in known) {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Which backend <see cref="AdapterEntryPoint.Install(System.Action{string},bool?,string[])" /> settled on, and why.</summary>
public sealed class Selection {
    internal const string Prefix = "[Concord.Waylith] ";

    /// <summary>Initializes a new instance.</summary>
    /// <param name="waylith">Whether body replacement was chosen.</param>
    /// <param name="reason">The sentence explaining the choice, ending in a period.</param>
    public Selection(bool waylith, string reason) {
        Waylith = waylith;
        Reason = reason;
    }

    /// <summary>Whether bodies get replaced in place. When <c>false</c>, patches go through the native detour backend instead.</summary>
    public bool Waylith { get; }

    /// <summary>Why this backend was picked, or why the one asked for could not run here.</summary>
    public string Reason { get; }

    /// <summary>The single line a host logs: marker, which backend, then <see cref="Reason" />.</summary>
    public string Message => Prefix + "backend: " + (Waylith ? "body replacement" : "native detour") + ". " + Reason;
}
