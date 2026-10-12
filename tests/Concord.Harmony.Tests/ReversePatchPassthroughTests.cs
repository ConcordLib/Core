using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

[Collection("HarmonySerial")]
public sealed class ReversePatchPassthroughTests
{
    private const string SilentId = "test.reverse.silent";

    private const string TripId = "test.reverse.trip";

    [Fact]
    public void ReversePatchOnAConcordOwnedTarget_RunsAndLeavesTheBackstopSilent()
    {
        List<string> logs = new List<string>();
        MethodBase target = Method(typeof(SilentTarget), nameof(SilentTarget.Compute));
        MethodInfo standin = Method(typeof(SilentStandin), nameof(SilentStandin.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), logs.Add);
        HarmonyBridge bridge = new HarmonyBridge(logs.Add);
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(SilentId);

        try
        {
            router.ActivateHost(bridge);
            Assert.True(bridge.BackstopInstalled, string.Join(" | ", logs));

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(ReverseMods.SilentHead)) });

            ReverseLog.Entries.Clear();
            Assert.Equal(7, SilentTarget.Compute());
            Assert.Equal(new[] { "concord" }, ReverseLog.Entries);

            logs.Clear();
            harmony.CreateReversePatcher(target, new HarmonyMethod(standin)).Patch();

            Assert.Equal(7, SilentStandin.Compute());
            Assert.DoesNotContain(logs, line => line.Contains(DetourMethodHook.TrippedMarker));
            Assert.Equal(RouteState.Raw, router.GetRoute(target));
        }
        finally
        {
            TestUnpatch.Own(harmony, SilentId);
            UpdateWrapperHook.DetachObserver();
            DetourMethodHook.Detach();
        }
    }

    [Fact]
    public void ReversePatchWithAWrongOwnershipAnswer_TripsTheBackstop()
    {
        List<string> logs = new List<string>();
        MethodBase target = Method(typeof(TripTarget), nameof(TripTarget.Compute));
        MethodInfo standin = Method(typeof(TripStandin), nameof(TripStandin.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), logs.Add);
        HarmonyBridge bridge = new HarmonyBridge(logs.Add);
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(TripId);

        try
        {
            DetourMethodHook.OwnsBodyOverride = method => method == standin;
            router.ActivateHost(bridge);
            Assert.True(bridge.BackstopInstalled, string.Join(" | ", logs));

            logs.Clear();
            harmony.CreateReversePatcher(target, new HarmonyMethod(standin)).Patch();

            string tripped = Assert.Single(logs, line => line.Contains(DetourMethodHook.TrippedMarker));
            Assert.Contains(typeof(TripStandin).FullName + "." + nameof(TripStandin.Compute), tripped);
        }
        finally
        {
            TestUnpatch.Own(harmony, TripId);
            UpdateWrapperHook.DetachObserver();
            DetourMethodHook.Detach();
        }
    }

    private static Injection HeadOf(string name)
    {
        return new Injection(Method(typeof(ReverseMods), name), new InjectAt.Head(), "test.concord.reverse", 0);
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    public static class ReverseLog
    {
        public static List<string> Entries { get; } = new List<string>();
    }

    public static class SilentTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 7;
        }
    }

    public static class SilentStandin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            throw new NotImplementedException("the reverse patch did not replace this body");
        }
    }

    public static class TripTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 8;
        }
    }

    public static class TripStandin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            throw new NotImplementedException("the reverse patch did not replace this body");
        }
    }

    public static class ReverseMods
    {
        public static void SilentHead(ControlHandle<int> ch)
        {
            ReverseLog.Entries.Add("concord");
        }
    }
}
