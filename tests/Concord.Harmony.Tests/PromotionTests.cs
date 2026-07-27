using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

// Serialized with the rest of the Harmony tests: the notifier hook and Harmony's own patch state are
// process-wide, so these cannot run alongside another test that patches.
[Collection("HarmonySerial")]
public class PromotionTests
{
    [Fact]
    public void ForeignPatchAfterConcordDetour_PromotesToBridge_AndBothRun()
    {
        PromotionLog.Entries.Clear();
        MethodBase target = typeof(PromotionTarget).GetMethod(nameof(PromotionTarget.Compute));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.promotion");

        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), Noop);
        HarmonyBridge bridge = new HarmonyBridge(Noop);

        try
        {
            router.ActivateHost(bridge);
            Assert.True(router.NotifierInstalled);

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(PromotionMods.ConcordHead)) });

            // Nothing foreign has touched it yet, so Concord owns the entry point outright.
            Assert.Equal(RouteState.Raw, router.GetRoute(target));
            Assert.Equal(1, PromotionTarget.Compute());
            Assert.Equal(new[] { "concord" }, PromotionLog.Entries);

            PromotionLog.Entries.Clear();

            // The notifier should fire inside this call and move the method onto the bridge before
            // Harmony finishes building its wrapper.
            foreign.Patch(
                target,
                prefix: new HarmonyMethod(typeof(PromotionMods).GetMethod(nameof(PromotionMods.ForeignPrefix))));

            Assert.Equal(RouteState.Bridge, router.GetRoute(target));

            Assert.Equal(1, PromotionTarget.Compute());
            Assert.Contains("concord", PromotionLog.Entries);
            Assert.Contains("foreign", PromotionLog.Entries);
        }
        finally
        {
            foreign.UnpatchAll("test.foreign.promotion");
            UpdateWrapperHook.Uninstall();
        }
    }

    [Fact]
    public void ForeignUnpatch_LeavesPromotedMethodOnTheBridge()
    {
        PromotionLog.Entries.Clear();
        MethodBase target = typeof(UnpatchTarget).GetMethod(nameof(UnpatchTarget.Compute));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.unpatch");

        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), Noop);
        HarmonyBridge bridge = new HarmonyBridge(Noop);

        try
        {
            router.ActivateHost(bridge);

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(PromotionMods.UnpatchHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            foreign.Patch(
                target,
                prefix: new HarmonyMethod(typeof(PromotionMods).GetMethod(nameof(PromotionMods.UnpatchForeignPrefix))));
            Assert.Equal(RouteState.Bridge, router.GetRoute(target));

            foreign.UnpatchAll("test.foreign.unpatch");

            // Route state is monotonic: once bridged, it stays bridged for the session.
            Assert.Equal(RouteState.Bridge, router.GetRoute(target));

            PromotionLog.Entries.Clear();
            Assert.Equal(2, UnpatchTarget.Compute());
            Assert.Contains("concord-unpatch", PromotionLog.Entries);
        }
        finally
        {
            foreign.UnpatchAll("test.foreign.unpatch");
            UpdateWrapperHook.Uninstall();
        }
    }

    [Fact]
    public void ConcordOwnRebuilds_DoNotRecurseThroughTheNotifier()
    {
        MethodBase target = typeof(RecursionTarget).GetMethod(nameof(RecursionTarget.Compute));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.recursion");

        CountingObserver observer = new CountingObserver();
        HarmonyBridge bridge = new HarmonyBridge(Noop);

        try
        {
            Assert.True(UpdateWrapperHook.TryInstall(observer, new MonoModDetourBackend(), Noop));

            // Routing installs Concord's own transpiler, which re-enters UpdateWrapper. Without the
            // suppression scope that re-entry would notify, promote, and recurse without end.
            ForeignRouteResult result = bridge.TryRoute(target, new[] { HeadOf(nameof(PromotionMods.RecursionHead)) }, true);
            Assert.Equal(ForeignRouteKind.Routed, result.Kind);

            Assert.Equal(0, observer.Count);
        }
        finally
        {
            foreign.UnpatchAll("test.foreign.recursion");
            UpdateWrapperHook.Uninstall();
        }
    }

    private static void Noop(string message)
    {
    }

    private static Injection HeadOf(string name)
    {
        return new Injection(typeof(PromotionMods).GetMethod(name), new InjectAt.Head(), "test.concord.promotion", 0);
    }

    private sealed class CountingObserver : IForeignPatchObserver
    {
        public int Count { get; private set; }

        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
            Count++;
        }
    }

    public static class PromotionLog
    {
        public static List<string> Entries { get; } = new List<string>();
    }

    public static class PromotionTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 1;
        }
    }

    public static class UnpatchTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 2;
        }
    }

    public static class RecursionTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 3;
        }
    }

    public static class PromotionMods
    {
        public static void ConcordHead(ControlHandle<int> ch)
        {
            PromotionLog.Entries.Add("concord");
        }

        public static void ForeignPrefix()
        {
            PromotionLog.Entries.Add("foreign");
        }

        public static void UnpatchHead(ControlHandle<int> ch)
        {
            PromotionLog.Entries.Add("concord-unpatch");
        }

        public static void UnpatchForeignPrefix()
        {
            PromotionLog.Entries.Add("foreign-unpatch");
        }

        public static void RecursionHead(ControlHandle<int> ch)
        {
        }
    }
}
