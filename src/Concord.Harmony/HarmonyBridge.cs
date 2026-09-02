#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony;

/// <summary>
///     Shares a method between Concord and Harmony. Harmony keeps the entry point; Concord's injections are
///     woven into the instruction stream Harmony builds, through one transpiler registered at the lowest
///     priority Harmony supports.
/// </summary>
public sealed class HarmonyBridge : IForeignPatchHost
{
    private const string BridgeOwner = "concord.bridge";

    private readonly HarmonyLib.Harmony harmony;
    private readonly Action<string> log;
    private bool lockUnavailableLogged;
    private bool foreignOwnersFailureLogged;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HarmonyBridge" /> class.
    /// </summary>
    /// <param name="log">Writes coexistence messages to the host game's log.</param>
    public HarmonyBridge(Action<string> log)
    {
        this.log = log;
        harmony = new HarmonyLib.Harmony(BridgeOwner);
        TranspilerParticipant.Log = log;

        // Suppressed: these calls re-enter UpdateWrapper, and Concord's own rebuild is not a foreign patch.
        InstallParticipant = target =>
        {
            using (UpdateWrapperHook.Suppress())
            {
                harmony.Patch(target, transpiler: new HarmonyMethod(TranspilerParticipant.TranspileMethod) { priority = Priority.Last });
            }
        };

        RemoveParticipant = target =>
        {
            using (UpdateWrapperHook.Suppress())
            {
                harmony.Unpatch(target, TranspilerParticipant.TranspileMethod);
            }
        };
    }

    internal Action<MethodBase> InstallParticipant { get; set; }

    internal Action<MethodBase> RemoveParticipant { get; set; }

    /// <inheritdoc />
    public IDisposable EnterHostLock()
    {
        return HarmonyLockScope.Available ? HarmonyLockScope.Enter() : null;
    }

    /// <inheritdoc />
    public string ValidateRoute(MethodBase target, IReadOnlyList<Injection> added)
    {
        target = MethodIdentity.Normalize(target);

        if (!HarmonyLockScope.Available)
        {
            return SupportMatrix.Validate(target, added, PatchProcessor.GetPatchInfo(target));
        }

        using (HarmonyLockScope.Enter())
        {
            return SupportMatrix.Validate(target, added, PatchProcessor.GetPatchInfo(target));
        }
    }

    /// <inheritdoc />
    public bool TryInstallNotifier(IForeignPatchObserver observer, IDetourBackend rawBackend)
    {
        return UpdateWrapperHook.TryInstall(observer, rawBackend, log);
    }

    /// <inheritdoc />
    public ForeignRouteResult RouteInto(MethodBase target, IReadOnlyList<Injection> added, object hostPatchState)
    {
        target = MethodIdentity.Normalize(target);

        if (hostPatchState is not PatchInfo patchInfo)
        {
            return ForeignRouteResult.Rejected(
                "Concord received host patch state of type '" + (hostPatchState?.GetType().Name ?? "null") + "' instead of a Harmony PatchInfo.");
        }

        string reason = SupportMatrix.Validate(target, added, PatchProcessor.GetPatchInfo(target));
        if (reason != null)
        {
            return ForeignRouteResult.Rejected(reason);
        }

        long[] owned = TranspilerParticipant.Registry.Add(target, added);

        // Written straight into the PatchInfo that Harmony is about to build this wrapper from, so the
        // build in progress picks the transpiler up and Harmony's own UpdatePatchInfo persists it.
        // Calling harmony.Patch here instead would be silently discarded when this rebuild returns.
        EnsureTranspilerRegistered(patchInfo);

        return ForeignRouteResult.Routed(new BridgeDetourHandle(this, target, owned));
    }

    /// <inheritdoc />
    public ForeignRouteResult TryRoute(MethodBase target, IReadOnlyList<Injection> added, bool forceRoute)
    {
        target = MethodIdentity.Normalize(target);

        if (!HarmonyLockScope.Available)
        {
            if (!lockUnavailableLogged)
            {
                lockUnavailableLogged = true;
                log("PatchProcessor.locker not found - routing raw with watchdog coverage; contested-check race admitted");
            }

            return ForeignRouteResult.NotContested();
        }

        using (HarmonyLockScope.Enter())
        {
            Patches patchInfo = PatchProcessor.GetPatchInfo(target);
            bool foreign = HasForeignPatch(patchInfo);

            if (!foreign && !forceRoute)
            {
                return ForeignRouteResult.NotContested();
            }

            string reason = SupportMatrix.Validate(target, added, patchInfo);
            if (reason != null)
            {
                return ForeignRouteResult.Rejected(reason);
            }

            long[] owned = TranspilerParticipant.Registry.Add(target, added);
            Exception failure;
            try
            {
                failure = RunRebuild(target);
            }
            catch
            {
                AbandonFirstApplication(target, owned);
                throw;
            }

            if (failure != null)
            {
                AbandonFirstApplication(target, owned);
                return ForeignRouteResult.Rejected(failure.Message);
            }

            return ForeignRouteResult.Routed(new BridgeDetourHandle(this, target, owned));
        }
    }

    /// <inheritdoc />
    public IDetourHandle ApplyToRouted(MethodBase target, IReadOnlyList<Injection> added)
    {
        target = MethodIdentity.Normalize(target);

        long[] owned;

        if (HarmonyLockScope.Available)
        {
            using (HarmonyLockScope.Enter())
            {
                owned = ApplyToRoutedCore(target, added);
            }
        }
        else
        {
            owned = ApplyToRoutedCore(target, added);
        }

        return new BridgeDetourHandle(this, target, owned);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ForeignOwners(MethodBase target)
    {
        target = MethodIdentity.Normalize(target);

        try
        {
            if (HarmonyLockScope.Available)
            {
                using (HarmonyLockScope.Enter())
                {
                    return CollectForeignOwners(target);
                }
            }

            return CollectForeignOwners(target);
        }
        catch (Exception ex)
        {
            if (!foreignOwnersFailureLogged)
            {
                foreignOwnersFailureLogged = true;
                log($"ForeignOwners read failed for {target}: {ex.Message}");
            }

            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ConcordOwners(MethodBase target)
    {
        return TranspilerParticipant.Registry.OwnersFor(target);
    }

    internal void DisposeHandle(MethodBase target, long[] owned)
    {
        if (HarmonyLockScope.Available)
        {
            using (HarmonyLockScope.Enter())
            {
                DisposeHandleLocked(target, owned);
            }

            return;
        }

        DisposeHandleLocked(target, owned);
    }

    // Idempotent: a target can be routed into more than once (several mods patching it, or a later
    // rebuild) and Harmony would otherwise stack duplicate transpilers.
    private static void EnsureTranspilerRegistered(PatchInfo patchInfo)
    {
        Patch[] transpilers = patchInfo.transpilers;
        for (int i = 0; i < transpilers.Length; i++)
        {
            if (transpilers[i].PatchMethod == TranspilerParticipant.TranspileMethod)
            {
                return;
            }
        }

        // Harmony marks this obsolete because the non-obsolete overload is internal, not because it is
        // going away inside the version range the bridge supports. Reflecting at the internal one instead
        // would be strictly more fragile.
#pragma warning disable CS0618
        patchInfo.AddTranspiler(TranspilerParticipant.TranspileMethod, BridgeOwner, Priority.Last, null, null, false);
#pragma warning restore CS0618
    }

    private static bool HasForeignPatch(Patches patchInfo)
    {
        if (patchInfo == null)
        {
            return false;
        }

        if (patchInfo.InnerPrefixes.Count > 0 || patchInfo.InnerPostfixes.Count > 0)
        {
            return true;
        }

        return HasForeignEntry(patchInfo.Prefixes) ||
               HasForeignEntry(patchInfo.Postfixes) ||
               HasForeignEntry(patchInfo.Transpilers) ||
               HasForeignEntry(patchInfo.Finalizers);
    }

    private static bool HasForeignEntry(IReadOnlyList<Patch> patches)
    {
        for (int i = 0; i < patches.Count; i++)
        {
            if (patches[i].PatchMethod != TranspilerParticipant.TranspileMethod)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> CollectForeignOwners(MethodBase target)
    {
        Patches patchInfo = PatchProcessor.GetPatchInfo(target);
        if (patchInfo == null)
        {
            return Array.Empty<string>();
        }

        HashSet<string> owners = new HashSet<string>();
        CollectForeignOwners(patchInfo.Prefixes, owners);
        CollectForeignOwners(patchInfo.Postfixes, owners);
        CollectForeignOwners(patchInfo.Transpilers, owners);
        CollectForeignOwners(patchInfo.Finalizers, owners);
        CollectForeignOwners(patchInfo.InnerPrefixes, owners);
        CollectForeignOwners(patchInfo.InnerPostfixes, owners);

        return new List<string>(owners);
    }

    private static void CollectForeignOwners(IReadOnlyList<Patch> patches, HashSet<string> owners)
    {
        for (int i = 0; i < patches.Count; i++)
        {
            if (patches[i].PatchMethod != TranspilerParticipant.TranspileMethod)
            {
                owners.Add(patches[i].owner);
            }
        }
    }

    private long[] ApplyToRoutedCore(MethodBase target, IReadOnlyList<Injection> added)
    {
        Patches patchInfo = PatchProcessor.GetPatchInfo(target);
        string reason = SupportMatrix.Validate(target, added, patchInfo);
        if (reason != null)
        {
            throw new InvalidOperationException(reason);
        }

        long[] owned = TranspilerParticipant.Registry.Add(target, added);
        Exception failure;
        try
        {
            failure = RunRebuild(target);
        }
        catch
        {
            RecoverSurvivors(target, owned);
            throw;
        }

        if (failure != null)
        {
            RecoverSurvivors(target, owned);
            throw new InvalidOperationException(failure.Message);
        }

        return owned;
    }

    private void DisposeHandleLocked(MethodBase target, long[] owned)
    {
        (long Seq, Injection Injection)[] removed = TranspilerParticipant.Registry.Remove(target, owned);

        Exception failure;
        try
        {
            failure = RunRebuild(target);
        }
        catch
        {
            TranspilerParticipant.Registry.Restore(target, removed);

            try
            {
                Exception restoreFailure = RunRebuild(target);
                if (restoreFailure != null)
                {
                    LoseParticipant(target, restoreFailure);
                }
            }
            catch (Exception restoreEx)
            {
                LoseParticipant(target, restoreEx);
            }

            throw;
        }

        if (failure != null)
        {
            LoseParticipant(target, failure);
        }
    }

    private void LoseParticipant(MethodBase target, Exception failure)
    {
        TranspilerParticipant.Registry.Clear(target);

        try
        {
            RemoveParticipant(target);
        }
        catch (Exception ex)
        {
            log("removing the concord participant failed for " + target.Name + ": " + ex.Message);
        }

        log("bridge participant lost for " + target.Name + "; its Concord injections are disabled: " + failure.Message);
    }

    private void RecoverSurvivors(MethodBase target, long[] droppedOwned)
    {
        TranspilerParticipant.Registry.Remove(target, droppedOwned);
        Exception survivorFailure;
        try
        {
            survivorFailure = RunRebuild(target);
        }
        catch (Exception ex)
        {
            survivorFailure = ex;
        }

        if (survivorFailure != null)
        {
            LoseParticipant(target, survivorFailure);
        }
    }

    private void AbandonFirstApplication(MethodBase target, long[] owned)
    {
        TranspilerParticipant.Registry.Remove(target, owned);

        try
        {
            RemoveParticipant(target);
        }
        catch (Exception ex)
        {
            log("abandoning the concord participant failed for " + target.Name + ": " + ex.Message);
        }
    }

    private void ReconcileParticipant(MethodBase target)
    {
        RemoveParticipant(target);
        if (TranspilerParticipant.Registry.HasInjections(target))
        {
            InstallParticipant(target);
        }
    }

    private Exception RunRebuild(MethodBase target)
    {
        TranspilerParticipant.LastStreamFailure = null;
        ReconcileParticipant(target);
        Exception failure = TranspilerParticipant.LastStreamFailure;
        TranspilerParticipant.LastStreamFailure = null;
        return failure;
    }
}
