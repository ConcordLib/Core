using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony;

/// <summary>
///     Hears about a patch before it lands. <c>PatchFunctions.UpdateWrapper</c> is the one place every
///     path that patches the original goes, so unpatch comes free. <c>ReversePatch</c> never arrives.
/// </summary>
internal static class UpdateWrapperHook
{
    internal const string SuppressBlockedMarker = "[Concord.Coex] suppress-blocked";

    internal const string SuppressFailedMarker = "[Concord.Coex] suppress-failed";

    internal const string ComposeRefusedMarker = "[Concord.Coex] compose-refused";

    internal const string ComposeUnownedMarker = "[Concord.Coex] compose-unowned";

    private static readonly MethodInfo? NotifyMethod =
        typeof(UpdateWrapperHook).GetMethod(nameof(Notify), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo? NotifyForeignCopyMethod =
        typeof(UpdateWrapperHook).GetMethod(nameof(NotifyForeignCopy), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly HashSet<string> SuppressionRefusalsLogged = new HashSet<string>(StringComparer.Ordinal);

    private static readonly Dictionary<MethodBase, IDetourHandle> ComposedForeign = new Dictionary<MethodBase, IDetourHandle>();

    private static readonly Dictionary<Assembly, IDetourHandle> HookHandles = new Dictionary<Assembly, IDetourHandle>();

    private static readonly HashSet<MethodBase> HarmonyBuilt = new HashSet<MethodBase>();

    private static SharedStateWrite? sharedStateWrite;

    // Concord's own rebuilds re-enter UpdateWrapper (TryRoute -> harmony.Patch -> UpdateWrapper). At that
    // moment the route state is still Raw, because Bridge is only set once TryRoute returns, so a guard on
    // the router side would recurse forever. It has to live here.
    [ThreadStatic]
    private static int suppressDepth;

    private static bool watchingForCopies;

    private static IForeignPatchObserver? observer;

    private static IForeignPatchDecider? decider;

    private static Action<string>? logger;

    private static IDetourBackend? composeBackend;

    /// <summary>
    ///     Installs the notifier. Safe to call more than once. Later calls only re-point the observer,
    ///     because the detour itself is never removed once it lands.
    /// </summary>
    /// <param name="target">The observer to notify before each Harmony rebuild.</param>
    /// <param name="rawBackend">The plain backend, never the routing one.</param>
    /// <param name="log">Writes coexistence messages to the host game's log.</param>
    /// <returns>True when the hook is installed.</returns>
    internal static bool TryInstall(IForeignPatchObserver target, IDetourBackend rawBackend, Action<string> log)
    {
        observer = target;
        decider = target as IForeignPatchDecider;
        logger = log;
        composeBackend = rawBackend;

        if (rawBackend is ISuppressionProbes probes)
        {
            SuppressionGate.WireCapability(probes.BodyReplacementUnavailable);
            SuppressionGate.WireProfiler(probes.ProfilerMayRewriteBodies);
        }

        if (NotifyMethod == null || NotifyForeignCopyMethod == null)
        {
            log(CoexistenceLogMarkers.HookUnavailable + " the notify injection method was not found");
            return false;
        }

        WatchForLaterCopies();

        int armed;
        if (HarmonyLockScope.Available)
        {
            using (HarmonyLockScope.Enter())
            {
                armed = InstallEveryLoadedCopy(rawBackend, log);
            }
        }
        else
        {
            armed = InstallEveryLoadedCopy(rawBackend, log);
        }

        Assembly[] hooked;
        lock (HookHandles)
        {
            hooked = new Assembly[HookHandles.Count];
            HookHandles.Keys.CopyTo(hooked, 0);
        }

        foreach (Assembly copy in hooked)
        {
            SeedAlreadyPatched(copy, log);
        }

        if (armed == 0)
        {
            log(CoexistenceLogMarkers.HookUnavailable +
                " no loaded assembly carries HarmonyLib.PatchFunctions.UpdateWrapper. Late contention stays unrecoverable");
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Suppresses the notifier on this thread for the duration of the scope, so Concord's own calls
    ///     into Harmony do not report themselves as foreign patches.
    /// </summary>
    /// <returns>A scope that restores notification when disposed.</returns>
    internal static IDisposable Suppress()
    {
        suppressDepth++;
        return new SuppressScope();
    }

    /// <summary>
    ///     Stops notifying, without touching the detour. Undoing and re-applying a detour on
    ///     <c>UpdateWrapper</c> SIGSEGVs mono once HarmonyX has patched anything through it, and nothing
    ///     in a running game ever needs the hook gone, so the detour stays for the process lifetime.
    /// </summary>
    internal static void DetachObserver()
    {
        observer = null;
        decider = null;
        composeBackend = null;
        DropComposedForeign();

        lock (SuppressionRefusalsLogged)
        {
            SuppressionRefusalsLogged.Clear();
        }
    }

    internal static string HarmonyVersionText()
    {
        return VersionTextOf(typeof(HarmonyLib.Harmony).Assembly);
    }

    internal static bool IsHookedOn(Assembly copy)
    {
        if (copy == null)
        {
            return false;
        }

        lock (HookHandles)
        {
            return HookHandles.ContainsKey(copy);
        }
    }

    internal static void StopWatchingForCopies()
    {
        lock (HookHandles)
        {
            if (!watchingForCopies)
            {
                return;
            }

            watchingForCopies = false;
        }

        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
    }

    private static string VersionTextOf(Assembly copy)
    {
        const string marker = "Version=";
        string? full = copy.FullName;
        if (full == null)
        {
            return "unknown";
        }

        int start = full.IndexOf(marker, StringComparison.Ordinal);

        if (start < 0)
        {
            return "unknown";
        }

        start += marker.Length;
        int end = full.IndexOf(',', start);
        return end < 0 ? full.Substring(start) : full.Substring(start, end - start);
    }

    private static int InstallEveryLoadedCopy(IDetourBackend rawBackend, Action<string> log)
    {
        int armed = 0;
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (TryInstallFor(loaded, rawBackend, log))
            {
                armed++;
            }
        }

        return armed;
    }

    private static void WatchForLaterCopies()
    {
        lock (HookHandles)
        {
            if (watchingForCopies)
            {
                return;
            }

            watchingForCopies = true;
        }

        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        IDetourBackend? backend = composeBackend;
        Action<string>? log = logger;

        if (backend == null || log == null)
        {
            return;
        }

        try
        {
            TryInstallFor(args.LoadedAssembly, backend, log);
        }
        catch (Exception ex)
        {
            log(CoexistenceLogMarkers.HookUnavailable + " arming a late Harmony copy failed: " + ExceptionReason.Of(ex));
        }
    }

    private static bool TryInstallFor(Assembly copy, IDetourBackend rawBackend, Action<string> log)
    {
        lock (HookHandles)
        {
            if (HookHandles.ContainsKey(copy))
            {
                return true;
            }
        }

        MethodInfo? updateWrapper;
        try
        {
            if (copy.GetType("HarmonyLib.Harmony") == null)
            {
                return false;
            }

            string? refusal = SupportMatrix.BackstopRefusal(VersionTextOf(copy), copy);
            if (refusal != null)
            {
                log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) + ": " + refusal);
                return false;
            }

            updateWrapper = ResolveUpdateWrapper(copy);
        }
        catch (Exception ex)
        {
            log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) + ": " + ExceptionReason.Of(ex));
            return false;
        }

        if (updateWrapper == null)
        {
            log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) +
                " carries no HarmonyLib.PatchFunctions.UpdateWrapper, so patches made through that copy stay invisible");
            return false;
        }

        IDetourHandle handle;
        try
        {
            MethodInfo notifyMethod = (copy == typeof(HarmonyLib.Harmony).Assembly ? NotifyMethod : NotifyForeignCopyMethod)!;
            Injection notify = new Injection(notifyMethod, new InjectAt.Head(), "concord.hook", 0);
            handle = rawBackend.ApplyComposed(updateWrapper, new[] { notify });
        }
        catch (Exception ex)
        {
            log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) + ": " + ExceptionReason.Of(ex));
            return false;
        }

        lock (HookHandles)
        {
            if (HookHandles.ContainsKey(copy))
            {
                handle.Dispose();
                return true;
            }

            HookHandles[copy] = handle;
        }

        log(CoexistenceLogMarkers.HookInstalled + " " + Describe(copy));
        SeedAlreadyPatched(copy, log);
        return true;
    }

    private static void SeedAlreadyPatched(Assembly copy, Action<string> log)
    {
        try
        {
            Type? harmonyType = copy.GetType("HarmonyLib.Harmony");
            MethodInfo? all = harmonyType?.GetMethod("GetAllPatchedMethods", BindingFlags.Public | BindingFlags.Static);
            MethodInfo? info = harmonyType?.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static);
            if (all == null || info == null)
            {
                return;
            }

            if (all.Invoke(null, null) is not System.Collections.IEnumerable patched)
            {
                return;
            }

            List<MethodBase> poisoned = new List<MethodBase>();
            foreach (object? entry in patched)
            {
                if (entry is MethodBase method && !SuppressedRebuilds.Holds(method) && StillDetoured(info, method))
                {
                    poisoned.Add(MethodIdentity.Normalize(method));
                }
            }

            int seeded = 0;
            lock (HarmonyBuilt)
            {
                foreach (MethodBase method in poisoned)
                {
                    if (HarmonyBuilt.Add(method))
                    {
                        seeded++;
                    }
                }
            }

            if (seeded > 0)
            {
                log(CoexistenceLogMarkers.HookInstalled + " " + Describe(copy) + ": " + seeded +
                    " target(s) it detoured before the hook landed stay Harmony's, because a body write under a live detour never runs");
            }
        }
        catch (Exception ex)
        {
            log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) +
                ": its already-patched targets could not be read, so one may be suppressed into a dead body write: " + ExceptionReason.Of(ex));
        }
    }

    private static bool StillDetoured(MethodInfo getPatchInfo, MethodBase method)
    {
        object? patches = getPatchInfo.Invoke(null, new object?[] { method });
        if (patches == null)
        {
            return false;
        }

        return patches.GetType().GetProperty("Owners")?.GetValue(patches) is System.Collections.ICollection owners &&
               owners.Count > 0;
    }

    private static MethodInfo? ResolveUpdateWrapper(Assembly copy)
    {
        Type? patchFunctions = copy.GetType("HarmonyLib.PatchFunctions");
        return patchFunctions?.GetMethod("UpdateWrapper", BindingFlags.NonPublic | BindingFlags.Static);
    }

    // Mirrors internal static MethodInfo PatchFunctions.UpdateWrapper(MethodBase, PatchInfo). Concord binds
    // target parameters positionally after the ControlHandle, so the full arity has to be declared even
    // though only `original` is read.
    private static void Notify(ControlHandle<MethodInfo> ch, MethodBase? original, PatchInfo patchInfo)
    {
        if (suppressDepth > 0 || original == null)
        {
            return;
        }

        MethodInfo? stub = StubForSuppressedRebuild(original, patchInfo);
        if (stub != null)
        {
            ch.ReturnValue = stub;
            ch.Cancel();
            return;
        }

        IForeignPatchObserver? current = observer;
        current?.OnForeignPatchPending(original, patchInfo);
    }

    private static void NotifyForeignCopy(MethodBase? original)
    {
        if (suppressDepth > 0 || original == null)
        {
            return;
        }

        IForeignPatchObserver? current = observer;
        current?.OnForeignPatchPending(original, null!);
    }

    private static MethodInfo? StubForSuppressedRebuild(MethodBase original, PatchInfo patchInfo)
    {
        IForeignPatchDecider? current = decider;
        if (current == null)
        {
            LogSuppressionRefusal(original, "no suppression decider is wired, so Harmony keeps every body it builds");
            return null;
        }

        string? refused = SuppressionGate.Validate(original)
                         ?? HarmonyApiHonesty.SuppressionReason(SharedStateWriteHere())
                         ?? HarmonyBuiltRefusal(original);
        if (refused != null)
        {
            LogSuppressionRefusal(original, refused);
            return MarkHarmonyBuiltAndFail(original);
        }

        try
        {
            MethodInfo stub = OriginalStubs.For(original);
            if (!current.TrySuppressForeignRebuild(original, patchInfo))
            {
                LogSuppressionRefusal(original, "the router declined to hold this target, so Harmony owns the body");
                return MarkHarmonyBuiltAndFail(original);
            }

            SuppressedRebuilds.Mark(original);

            bool replicate = SharedStateWriteHere() == SharedStateWrite.InsideUpdateWrapper;
            if (replicate)
            {
                HarmonyApiHonesty.ValidateSurvivingMetadata(patchInfo);
            }

            if (!ComposeForeignPatches(original, patchInfo))
            {
                return MarkHarmonyBuiltAndFail(original);
            }

            if (replicate)
            {
                HarmonyApiHonesty.ReplicateSharedStateWrite(original, stub, patchInfo);
            }

            return stub;
        }
        catch (Exception ex)
        {
            logger?.Invoke(SuppressFailedMarker + " " + Describe(original) + ": " + ExceptionReason.Of(ex));
            return MarkHarmonyBuiltAndFail(original);
        }
    }

    private static void LogSuppressionRefusal(MethodBase original, string reason)
    {
        bool firstForThisReason;
        lock (SuppressionRefusalsLogged)
        {
            firstForThisReason = SuppressionRefusalsLogged.Add(reason);
        }

        if (firstForThisReason)
        {
            logger?.Invoke(SuppressBlockedMarker + " " + Describe(original) + ": " + reason);
        }
    }

    private static MethodInfo? MarkHarmonyBuiltAndFail(MethodBase original)
    {
        MethodBase key = MethodIdentity.Normalize(original);
        SuppressedRebuilds.Forget(original);

        lock (HarmonyBuilt)
        {
            HarmonyBuilt.Add(key);
        }

        DropComposedFor(key);
        return null;
    }

    private static void DropComposedFor(MethodBase key)
    {
        IDetourHandle? stale;
        lock (ComposedForeign)
        {
            if (!ComposedForeign.TryGetValue(key, out stale))
            {
                return;
            }

            ComposedForeign.Remove(key);
        }

        try
        {
            using (Suppress())
            {
                stale.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger?.Invoke(SuppressFailedMarker + " " + Describe(key) +
                ": dropping the previous composed patch set after a refused rebuild failed: " + ExceptionReason.Of(ex));
        }
    }

    private static SharedStateWrite SharedStateWriteHere()
    {
        SharedStateWrite? known = sharedStateWrite;
        if (known == null)
        {
            known = HarmonyApiHonesty.Classify();
            sharedStateWrite = known;
        }

        return known.Value;
    }

    private static string? HarmonyBuiltRefusal(MethodBase original)
    {
        bool built;
        lock (HarmonyBuilt)
        {
            built = HarmonyBuilt.Contains(MethodIdentity.Normalize(original));
        }

        if (!built)
        {
            return null;
        }

        return "Harmony already built a wrapper for " + Describe(original) +
               " and a body written after that never runs, so Harmony keeps building this method";
    }

    private static bool ComposeForeignPatches(MethodBase original, PatchInfo patchInfo)
    {
        IDetourBackend? backend = composeBackend;
        if (backend == null)
        {
            logger?.Invoke(ComposeUnownedMarker + " " + Describe(original) +
                ": the decider claimed this target but no backend is wired, so Harmony's patches were not folded into a body");
            return false;
        }

        ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(original, patchInfo);
        if (converted.Refusals.Count > 0)
        {
            ForeignPatchConverter.Refusal first = converted.Refusals[0];
            logger?.Invoke(ComposeRefusedMarker + " " + Describe(original) + " owner '" + first.Owner + "': " + first.Reason +
                ". Harmony keeps this method, so its own patches still run, but Concord's injections on it do not");
            return false;
        }

        MethodBase key = MethodIdentity.Normalize(original);

        using (Suppress())
        {
            lock (ComposedForeign)
            {
                if (ComposedForeign.TryGetValue(key, out IDetourHandle? previous))
                {
                    ComposedForeign.Remove(key);
                    previous.Dispose();
                }

                if (converted.Injections.Count > 0)
                {
                    ComposedForeign[key] = backend.ApplyComposed(original, converted.Injections);
                }
            }
        }

        return true;
    }

    private static void DropComposedForeign()
    {
        List<IDetourHandle> held;
        lock (ComposedForeign)
        {
            held = new List<IDetourHandle>(ComposedForeign.Values);
            ComposedForeign.Clear();
        }

        foreach (IDetourHandle handle in held)
        {
            try
            {
                handle.Dispose();
            }
            catch (Exception ex)
            {
                logger?.Invoke(SuppressFailedMarker + " releasing a composed Harmony patch set failed: " + ExceptionReason.Of(ex));
            }
        }
    }

    private static string Describe(MethodBase target)
    {
        return (target.DeclaringType?.Name ?? "<module>") + "." + target.Name;
    }

    private static string Describe(Assembly copy)
    {
        return VersionTextOf(copy) + " #" + copy.GetHashCode();
    }

    private sealed class SuppressScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            suppressDepth--;
        }
    }
}
