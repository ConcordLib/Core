using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

[Collection("HarmonySerial")]
public class DetourMethodBackstopTests
{
    public static List<string> Ran { get; } = new List<string>();

    [Fact]
    public void ForeignDetourOnRawOwnedTarget_TripsTheBackstop()
    {
        Ran.Clear();
        List<string> log = new List<string>();
        MethodBase target = Method(typeof(RawTarget), nameof(RawTarget.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), log.Add);
        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.backstop.raw");
        HarmonyLib.Harmony late = new HarmonyLib.Harmony("test.foreign.backstop.raw.late");

        try
        {
            ArmWithTheRealDetector(router, bridge);
            StopPromoting();

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.RawHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            foreign.Patch(target, prefix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.ForeignPrefix))));

            string first = Assert.Single(Trips(log));
            Assert.Contains(nameof(RawTarget.Compute), first);

            late.Patch(target, postfix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.LateForeignPostfix))));

            List<string> trips = Trips(log);
            Assert.Equal(2, trips.Count);
            Assert.Contains("'test.foreign.backstop.raw'", trips[1]);
        }
        finally
        {
            TestUnpatch.Own(late, "test.foreign.backstop.raw.late");
            Disarm(foreign, "test.foreign.backstop.raw");
        }
    }

    [Fact]
    public void APostfixArrivingBeforeAPrefix_StillNamesBothOwners()
    {
        Ran.Clear();
        List<string> log = new List<string>();
        MethodBase target = Method(typeof(PostfixFirstTarget), nameof(PostfixFirstTarget.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), log.Add);
        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        HarmonyLib.Harmony postfixOwner = new HarmonyLib.Harmony("aaa.postfix.first");
        HarmonyLib.Harmony prefixOwner = new HarmonyLib.Harmony("zzz.prefix.second");
        HarmonyLib.Harmony thirdOwner = new HarmonyLib.Harmony("mmm.postfix.third");

        try
        {
            ArmWithTheRealDetector(router, bridge);
            StopPromoting();

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.PostfixFirstHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            postfixOwner.Patch(target, postfix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.LateForeignPostfix))));
            prefixOwner.Patch(target, prefix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.ForeignPrefix))));
            thirdOwner.Patch(target, postfix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.ThirdForeignPostfix))));

            List<string> trips = Trips(log);
            string last = trips[trips.Count - 1];
            Assert.Contains("'aaa.postfix.first'", last);
            Assert.Contains("'zzz.prefix.second'", last);
        }
        finally
        {
            TestUnpatch.Own(thirdOwner, "mmm.postfix.third");
            TestUnpatch.Own(prefixOwner, "zzz.prefix.second");
            Disarm(postfixOwner, "aaa.postfix.first");
        }
    }

    [Fact]
    public void ForeignDetourOnBridgeRoutedTarget_StaysSilent()
    {
        Ran.Clear();
        List<string> log = new List<string>();
        MethodBase target = Method(typeof(BridgeTarget), nameof(BridgeTarget.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), log.Add);
        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.backstop.bridge");
        HarmonyLib.Harmony second = new HarmonyLib.Harmony("test.foreign.backstop.bridge.second");

        try
        {
            Arm(router, bridge);

            foreign.Patch(target, prefix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.BridgeForeignPrefix))));

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.BridgeHead)) });
            Assert.Equal(RouteState.Bridge, router.GetRoute(target));
            Assert.Empty(Trips(log));

            second.Patch(target, postfix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.BridgeForeignPostfix))));
            Assert.Equal(RouteState.Bridge, router.GetRoute(target));
            Assert.Empty(Trips(log));

            Ran.Clear();
            Assert.Equal(12, BridgeTarget.Compute());
            Assert.Contains("concord-bridge", Ran);
            Assert.Contains("foreign-bridge", Ran);
        }
        finally
        {
            TestUnpatch.Own(second, "test.foreign.backstop.bridge.second");
            Disarm(foreign, "test.foreign.backstop.bridge");
        }
    }

    [Fact]
    public void ReversePatchAgainstRawOwnedTarget_StaysSilent_AndTheStubStillWorks()
    {
        Ran.Clear();
        List<string> log = new List<string>();
        MethodBase target = Method(typeof(ReverseTarget), nameof(ReverseTarget.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), log.Add);
        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.backstop.reverse");

        try
        {
            Arm(router, bridge);
            StopPromoting();

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.ReverseHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            foreign.CreateReversePatcher(
                target,
                new HarmonyMethod(typeof(ReverseStub).GetMethod(nameof(ReverseStub.Call)))).Patch();

            Assert.Empty(Trips(log));
            Assert.Equal(13, ReverseStub.Call());
        }
        finally
        {
            Disarm(foreign, "test.foreign.backstop.reverse");
        }
    }

    [Fact]
    public void ForeignDetourOnTargetConcordNeverTouched_StaysSilent()
    {
        Ran.Clear();
        List<string> log = new List<string>();
        MethodBase target = Method(typeof(UntouchedTarget), nameof(UntouchedTarget.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), log.Add);
        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.backstop.untouched");

        try
        {
            Arm(router, bridge);

            foreign.Patch(target, prefix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.UntouchedForeignPrefix))));

            Assert.Equal(RouteState.Unpinned, router.GetRoute(target));
            Assert.Empty(Trips(log));

            Ran.Clear();
            Assert.Equal(14, UntouchedTarget.Compute());
            Assert.Contains("foreign-untouched", Ran);
        }
        finally
        {
            Disarm(foreign, "test.foreign.backstop.untouched");
        }
    }

    [Fact]
    public void ConcordOwnBridgeRouteOfARawOwnedTarget_DoesNotTripTheBackstop()
    {
        Ran.Clear();
        List<string> log = new List<string>();
        MethodBase target = Method(typeof(SuppressTarget), nameof(SuppressTarget.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), log.Add);
        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.backstop.suppress");

        try
        {
            Arm(router, bridge);
            StopPromoting();

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.SuppressHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            ForeignRouteResult result = bridge.TryRoute(target, new[] { HeadOf(nameof(Mods.SuppressRouteHead)) }, true);
            Assert.Equal(ForeignRouteKind.Routed, result.Kind);
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            Assert.Empty(Trips(log));
        }
        finally
        {
            Disarm(foreign, "test.foreign.backstop.suppress");
        }
    }

    [Fact]
    public void ForeignDetourOnATargetANonMonoModBackendOwns_TripsTheBackstop()
    {
        Ran.Clear();
        List<string> log = new List<string>();
        MethodBase target = Method(typeof(OtherBackendTarget), nameof(OtherBackendTarget.Compute));
        FakeOwnsBodyBackend backend = new FakeOwnsBodyBackend(target);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.backstop.otherbackend");

        try
        {
            Assert.True(DetourMethodHook.TryInstall(backend, log.Add));

            foreign.Patch(target, prefix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.OtherBackendForeignPrefix))));

            string first = Assert.Single(Trips(log));
            Assert.Contains(nameof(OtherBackendTarget.Compute), first);
        }
        finally
        {
            TestUnpatch.Own(foreign, "test.foreign.backstop.otherbackend");
            DetourMethodHook.Detach();
        }
    }

    [Fact]
    public void AnOwnershipQueryThatThrows_DoesNotEscapeIntoHarmony()
    {
        Ran.Clear();
        List<string> log = new List<string>();
        MethodBase target = Method(typeof(ThrowTarget), nameof(ThrowTarget.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), log.Add);
        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.backstop.throwing");

        try
        {
            Arm(router, bridge);
            StopPromoting();
            DetourMethodHook.OwnsBodyOverride = method => throw new InvalidOperationException("the backstop's own detector failed");

            foreign.Patch(target, prefix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.ThrowForeignPrefix))));

            Assert.Empty(Trips(log));

            Ran.Clear();
            Assert.Equal(17, ThrowTarget.Compute());
            Assert.Contains("foreign-throwing", Ran);
        }
        finally
        {
            Disarm(foreign, "test.foreign.backstop.throwing");
        }
    }

    [Fact]
    public void NormalLoadOrderOnASupportedHost_NeverTripsTheBackstop()
    {
        Ran.Clear();
        List<string> log = new List<string>();
        MethodBase target = Method(typeof(LoadOrderTarget), nameof(LoadOrderTarget.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), log.Add);
        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.backstop.loadorder");

        try
        {
            Assert.Null(SupportMatrix.BackstopRefusal());
            Arm(router, bridge);
            Assert.True(router.NotifierInstalled);

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.LoadOrderHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            foreign.Patch(target, prefix: new HarmonyMethod(typeof(Mods).GetMethod(nameof(Mods.LoadOrderForeignPrefix))));

            Assert.Equal(RouteState.Bridge, router.GetRoute(target));
            Assert.True(
                Trips(log).Count == 0,
                "the backstop tripped under a normal load order, so a target Concord owned was detoured instead of promoted: " +
                string.Join(" | ", Trips(log)));

            Ran.Clear();
            Assert.Equal(15, LoadOrderTarget.Compute());
            Assert.Contains("concord-loadorder", Ran);
            Assert.Contains("foreign-loadorder", Ran);
        }
        finally
        {
            Disarm(foreign, "test.foreign.backstop.loadorder");
        }
    }

    private static void Arm(RoutingDetourBackend router, HarmonyBridge bridge)
    {
        DetourMethodHook.OwnsBodyOverride = method => router.GetRoute(method) == RouteState.Raw;
        router.ActivateHost(bridge);
        Assert.True(bridge.BackstopInstalled);
    }

    private static void ArmWithTheRealDetector(RoutingDetourBackend router, HarmonyBridge bridge)
    {
        DetourMethodHook.OwnsBodyOverride = null;
        router.ActivateHost(bridge);
        Assert.True(bridge.BackstopInstalled);
    }

    private static void StopPromoting()
    {
        UpdateWrapperHook.DetachObserver();
    }

    private static void Disarm(HarmonyLib.Harmony foreign, string id)
    {
        TestUnpatch.Own(foreign, id);
        DetourMethodHook.Detach();
        UpdateWrapperHook.DetachObserver();
    }

    private static List<string> Trips(List<string> log)
    {
        return log.FindAll(entry => entry.StartsWith(DetourMethodHook.TrippedMarker));
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static Injection HeadOf(string name)
    {
        return new Injection(Method(typeof(Mods), name), new InjectAt.Head(), "test.concord.backstop", 0);
    }

    public static class RawTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 11;
        }
    }

    public static class PostfixFirstTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 13;
        }
    }

    public static class BridgeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 12;
        }
    }

    public static class ReverseTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 13;
        }
    }

    public static class ReverseStub
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Call()
        {
            return 0;
        }
    }

    public static class UntouchedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 14;
        }
    }

    public static class LoadOrderTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 15;
        }
    }

    public static class SuppressTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 16;
        }
    }

    public static class ThrowTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 17;
        }
    }

    public static class OtherBackendTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 18;
        }
    }

    private sealed class FakeOwnsBodyBackend : IDetourBackend, IOwnsBodyQuery
    {
        private readonly MethodBase owned;

        public FakeOwnsBodyBackend(MethodBase owned)
        {
            this.owned = owned;
        }

        public bool OwnsBody(MethodBase target)
        {
            return target == owned;
        }

        public IDetourHandle Apply(MethodBase original, MethodInfo replacement)
        {
            throw new NotSupportedException();
        }

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added)
        {
            return new MonoModDetourBackend().ApplyComposed(target, added);
        }
    }

    public static class Mods
    {
        public static void RawHead(ControlHandle<int> ch)
        {
            Ran.Add("concord-raw");
        }

        public static void PostfixFirstHead(ControlHandle<int> ch)
        {
            Ran.Add("concord-postfix-first");
        }

        public static void ThirdForeignPostfix()
        {
            Ran.Add("foreign-third");
        }

        public static void ForeignPrefix()
        {
            Ran.Add("foreign-raw");
        }

        public static void LateForeignPostfix()
        {
            Ran.Add("foreign-raw-late");
        }

        public static void BridgeHead(ControlHandle<int> ch)
        {
            Ran.Add("concord-bridge");
        }

        public static void BridgeForeignPrefix()
        {
            Ran.Add("foreign-bridge");
        }

        public static void BridgeForeignPostfix()
        {
            Ran.Add("foreign-bridge-postfix");
        }

        public static void ReverseHead(ControlHandle<int> ch)
        {
            Ran.Add("concord-reverse");
        }

        public static void UntouchedForeignPrefix()
        {
            Ran.Add("foreign-untouched");
        }

        public static void LoadOrderHead(ControlHandle<int> ch)
        {
            Ran.Add("concord-loadorder");
        }

        public static void LoadOrderForeignPrefix()
        {
            Ran.Add("foreign-loadorder");
        }

        public static void SuppressHead(ControlHandle<int> ch)
        {
            Ran.Add("concord-suppress");
        }

        public static void SuppressRouteHead(ControlHandle<int> ch)
        {
            Ran.Add("concord-suppress-route");
        }

        public static void ThrowForeignPrefix()
        {
            Ran.Add("foreign-throwing");
        }

        public static void OtherBackendForeignPrefix()
        {
            Ran.Add("foreign-otherbackend");
        }
    }
}
