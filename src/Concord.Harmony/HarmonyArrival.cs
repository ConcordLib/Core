using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;

namespace Concord.Harmony;

/// <summary>
///     Arms the notifier hook before any Harmony copy can patch: at once for every copy already loaded,
///     and inside the assembly load for every copy that arrives later.
/// </summary>
public static class HarmonyArrival
{
    internal const string ArmedMarker = "[Concord.Coex] arrival-armed";

    internal const string ArmFailedMarker = "[Concord.Coex] arrival-failed";

    internal const string ProbeMissingMarker = "[Concord.Coex] arrival-probe-missing";

    private const string HarmonyName = "0Harmony";

    private static readonly object Gate = new object();

    private static readonly HashSet<Assembly> Armed = new HashSet<Assembly>();

    private static IForeignPatchObserver? observer;

    private static IDetourBackend? backend;

    private static Action<string>? log;

    private static AssemblyLoadEventHandler? subscription;

    internal static int ArmedCount
    {
        get
        {
            lock (Gate)
            {
                return Armed.Count;
            }
        }
    }

    /// <summary>
    ///     Installs the hook on every Harmony already in the process and on each one that loads later.
    ///     Safe to call again: a later call re-points the observer, backend, log and probes.
    /// </summary>
    /// <param name="target">Receives each pending Harmony rebuild, or null until the host has one.</param>
    /// <param name="rawBackend">The plain backend, never the routing one.</param>
    /// <param name="hostLog">Writes coexistence messages to the host game's log.</param>
    /// <param name="capability">Null when this host can replace a method body, else why not. Null here leaves suppression off.</param>
    /// <param name="profiler">True while a profiler may rewrite bodies. Null here leaves suppression off.</param>
    public static void Install(
        IForeignPatchObserver? target,
        IDetourBackend rawBackend,
        Action<string> hostLog,
        Func<string?>? capability,
        Func<bool>? profiler)
    {
        if (rawBackend == null)
        {
            throw new ArgumentNullException(nameof(rawBackend));
        }

        if (hostLog == null)
        {
            throw new ArgumentNullException(nameof(hostLog));
        }

        lock (Gate)
        {
            observer = target;
            backend = rawBackend;
            log = hostLog;

            WireProbes(capability, profiler, hostLog);

            if (Armed.Count > 0)
            {
                UpdateWrapperHook.TryInstall(target!, rawBackend, hostLog);
            }

            if (subscription == null)
            {
                subscription = OnAssemblyLoad;
                AppDomain.CurrentDomain.AssemblyLoad += subscription;
            }

            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loaded.Length; i++)
            {
                if (IsHarmony(loaded[i]))
                {
                    Arm(loaded[i]);
                }
            }
        }
    }

    internal static bool IsArmed(Assembly harmony)
    {
        lock (Gate)
        {
            return Armed.Contains(harmony);
        }
    }

    internal static void Reset()
    {
        lock (Gate)
        {
            if (subscription != null)
            {
                AppDomain.CurrentDomain.AssemblyLoad -= subscription;
                subscription = null;
            }

            UpdateWrapperHook.StopWatchingForCopies();

            Armed.Clear();
            observer = null;
            backend = null;
            log = null;
        }
    }

    internal static bool IsHarmony(Assembly assembly)
    {
        string? full = assembly.FullName;
        if (full == null)
        {
            return false;
        }

        int comma = full.IndexOf(',');
        string simple = comma < 0 ? full : full.Substring(0, comma);
        return string.Equals(simple, HarmonyName, StringComparison.Ordinal);
    }

    private static void WireProbes(Func<string?>? capability, Func<bool>? profiler, Action<string> hostLog)
    {
        if (capability == null)
        {
            hostLog(ProbeMissingMarker + " no body-replacement capability probe was supplied, so suppression stays off on this host");
        }
        else
        {
            SuppressionGate.WireCapability(capability);
        }

        if (profiler == null)
        {
            hostLog(ProbeMissingMarker + " no profiler probe was supplied, so suppression stays off on this host");
        }
        else
        {
            SuppressionGate.WireProfiler(profiler);
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        Assembly loaded = args.LoadedAssembly;
        if (!IsHarmony(loaded))
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Arm(loaded);
            }
        }
        catch (Exception thrown)
        {
            ReportQuietly(ArmFailedMarker + " #" + loaded.GetHashCode() + " " + loaded.FullName + ": " + thrown);
        }
    }

    private static void ReportQuietly(string line)
    {
        try
        {
            log?.Invoke(line);
        }
        catch (Exception)
        {
            return;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Arm(Assembly harmony)
    {
        if (!Armed.Add(harmony))
        {
            return;
        }

        UpdateWrapperHook.TryInstall(observer!, backend!, log!);
        bool installed = UpdateWrapperHook.IsHookedOn(harmony);
        log!((installed ? ArmedMarker : ArmFailedMarker) + " #" + harmony.GetHashCode() + " " + harmony.FullName);
    }
}
