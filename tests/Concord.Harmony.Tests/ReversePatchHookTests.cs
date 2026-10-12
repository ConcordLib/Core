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
public sealed class ReversePatchHookTests
{
    public static List<string> Ran { get; } = new List<string>();

    [Fact]
    public void SnapshotOnARawConcordOwnedTarget_CopiesTheLiveBody()
    {
        List<string> logs = new List<string>();
        MethodBase target = Method(typeof(LiveTarget), nameof(LiveTarget.Compute));
        MethodInfo standin = Method(typeof(LiveStandin), nameof(LiveStandin.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), logs.Add);
        HarmonyBridge bridge = new HarmonyBridge(logs.Add);
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.reverse.hook.live");

        try
        {
            Arm(router, bridge, logs);

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.LiveHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));
            Assert.Equal(99, LiveTarget.Compute(1));

            harmony.CreateReversePatcher(target, new HarmonyMethod(standin)).Patch(HarmonyReversePatchType.Snapshot);

            Assert.Equal(99, LiveStandin.Compute(1));
            Assert.Single(Fired(logs));
        }
        finally
        {
            Disarm(harmony, "test.reverse.hook.live");
        }
    }

    [Fact]
    public void SnapshotOnARawConcordOwnedInstanceTarget_PassesTheInstanceThrough()
    {
        List<string> logs = new List<string>();
        MethodBase target = Method(typeof(Counter), nameof(Counter.Add));
        MethodInfo standin = Method(typeof(CounterStandin), nameof(CounterStandin.Add));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), logs.Add);
        HarmonyBridge bridge = new HarmonyBridge(logs.Add);
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.reverse.hook.instance");

        try
        {
            Arm(router, bridge, logs);

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.CounterHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            harmony.CreateReversePatcher(target, new HarmonyMethod(standin)).Patch(HarmonyReversePatchType.Snapshot);

            Counter counter = new Counter { Base = 10 };
            Ran.Clear();
            Assert.Equal(15, CounterStandin.Add(counter, 5));
            Assert.Equal(new[] { "concord-counter" }, Ran);
            Assert.Single(Fired(logs));
        }
        finally
        {
            Disarm(harmony, "test.reverse.hook.instance");
        }
    }

    [Fact]
    public void OriginalOnARawConcordOwnedTarget_KeepsTheUnpatchedBody()
    {
        List<string> logs = new List<string>();
        MethodBase target = Method(typeof(OriginalTarget), nameof(OriginalTarget.Compute));
        MethodInfo standin = Method(typeof(OriginalStandin), nameof(OriginalStandin.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), logs.Add);
        HarmonyBridge bridge = new HarmonyBridge(logs.Add);
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.reverse.hook.original");

        try
        {
            Arm(router, bridge, logs);

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.OriginalHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));
            Assert.Equal(99, OriginalTarget.Compute(1));

            harmony.CreateReversePatcher(target, new HarmonyMethod(standin)).Patch(HarmonyReversePatchType.Original);

            Assert.Equal(8, OriginalStandin.Compute(1));
            Assert.Empty(Fired(logs));
        }
        finally
        {
            Disarm(harmony, "test.reverse.hook.original");
        }
    }

    [Fact]
    public void SnapshotOnAHarmonyPatchedTargetConcordNeverTouched_StaysOutOfTheWay()
    {
        List<string> logs = new List<string>();
        MethodBase target = Method(typeof(UntouchedTarget), nameof(UntouchedTarget.Compute));
        MethodInfo standin = Method(typeof(UntouchedStandin), nameof(UntouchedStandin.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), logs.Add);
        HarmonyBridge bridge = new HarmonyBridge(logs.Add);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.reverse.hook.untouched.foreign");
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.reverse.hook.untouched");

        try
        {
            Arm(router, bridge, logs);
            foreign.Patch(target, prefix: new HarmonyMethod(Method(typeof(Mods), nameof(Mods.UntouchedForeignPrefix))));
            Assert.Equal(RouteState.Unpinned, router.GetRoute(target));

            harmony.CreateReversePatcher(target, new HarmonyMethod(standin)).Patch(HarmonyReversePatchType.Snapshot);

            Ran.Clear();
            Assert.Equal(9, UntouchedStandin.Compute(1));
            Assert.Empty(Ran);
            Assert.Empty(Fired(logs));
        }
        finally
        {
            TestUnpatch.Own(foreign, "test.reverse.hook.untouched.foreign");
            Disarm(harmony, "test.reverse.hook.untouched");
        }
    }

    [Fact]
    public void SnapshotOnATargetNobodyPatched_StillFailsTheWayHarmonyFailsToday()
    {
        List<string> logs = new List<string>();
        MethodBase target = Method(typeof(NobodyTarget), nameof(NobodyTarget.Compute));
        MethodInfo standin = Method(typeof(NobodyStandin), nameof(NobodyStandin.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), logs.Add);
        HarmonyBridge bridge = new HarmonyBridge(logs.Add);
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.reverse.hook.nobody");

        try
        {
            Arm(router, bridge, logs);

            Assert.Throws<NullReferenceException>(
                () => harmony.CreateReversePatcher(target, new HarmonyMethod(standin)).Patch(HarmonyReversePatchType.Snapshot));

            Assert.Empty(Fired(logs));
            Assert.Throws<NotImplementedException>(() => NobodyStandin.Compute(1));
        }
        finally
        {
            Disarm(harmony, "test.reverse.hook.nobody");
        }
    }

    [Fact]
    public void SnapshotOnABridgeRoutedTarget_LetsHarmonysOwnTranspilerPathCarryConcord()
    {
        List<string> logs = new List<string>();
        MethodBase target = Method(typeof(RoutedTarget), nameof(RoutedTarget.Compute));
        MethodInfo standin = Method(typeof(RoutedStandin), nameof(RoutedStandin.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), logs.Add);
        HarmonyBridge bridge = new HarmonyBridge(logs.Add);
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.reverse.hook.routed.foreign");
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.reverse.hook.routed");

        try
        {
            Arm(router, bridge, logs);

            foreign.Patch(target, prefix: new HarmonyMethod(Method(typeof(Mods), nameof(Mods.RoutedForeignPrefix))));
            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.RoutedHead)) });
            Assert.Equal(RouteState.Bridge, router.GetRoute(target));

            harmony.CreateReversePatcher(target, new HarmonyMethod(standin)).Patch(HarmonyReversePatchType.Snapshot);

            Ran.Clear();
            Assert.Equal(99, RoutedStandin.Compute(1));
            Assert.Equal(new[] { "concord-routed" }, Ran);
            Assert.Empty(Fired(logs));
        }
        finally
        {
            TestUnpatch.Own(foreign, "test.reverse.hook.routed.foreign");
            Disarm(harmony, "test.reverse.hook.routed");
        }
    }

    [Fact]
    public void SnapshotWithAStubTranspiler_OnARawConcordOwnedTarget_RefusesLoudly()
    {
        List<string> logs = new List<string>();
        MethodBase target = Method(typeof(TranspiledTarget), nameof(TranspiledTarget.Compute));
        MethodInfo standin = Method(typeof(TranspiledStandin), nameof(TranspiledStandin.Compute));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), logs.Add);
        HarmonyBridge bridge = new HarmonyBridge(logs.Add);
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.reverse.hook.transpiled");

        try
        {
            Arm(router, bridge, logs);

            using IDetourHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(Mods.TranspiledHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            NotSupportedException refused = Assert.Throws<NotSupportedException>(
                () => harmony.CreateReversePatcher(target, new HarmonyMethod(standin)).Patch(HarmonyReversePatchType.Snapshot));

            Assert.Contains(nameof(TranspiledTarget), refused.Message);
            Assert.Empty(Fired(logs));
            Assert.Throws<NotImplementedException>(() => TranspiledStandin.Compute(1));
        }
        finally
        {
            Disarm(harmony, "test.reverse.hook.transpiled");
        }
    }

    private static void Arm(RoutingDetourBackend router, HarmonyBridge bridge, List<string> logs)
    {
        router.ActivateHost(bridge);
        Assert.True(bridge.ReversePatchHookInstalled, string.Join(" | ", logs));
    }

    private static void Disarm(HarmonyLib.Harmony harmony, string id)
    {
        TestUnpatch.Own(harmony, id);
        ReversePatchHook.Detach();
        DetourMethodHook.Detach();
        UpdateWrapperHook.DetachObserver();
    }

    private static List<string> Fired(List<string> logs)
    {
        return logs.FindAll(line => line.StartsWith(ReversePatchHook.LiveMarker));
    }

    private static Injection HeadOf(string name)
    {
        return new Injection(Method(typeof(Mods), name), new InjectAt.Head(), "test.concord.reversehook", 0);
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    public static class LiveTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            return seed + 6;
        }
    }

    public static class LiveStandin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            throw new NotImplementedException("the reverse patch did not replace this body");
        }
    }

    public sealed class Counter
    {
        public int Base { get; set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Add(int amount)
        {
            return Base + amount;
        }
    }

    public static class CounterStandin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Add(Counter instance, int amount)
        {
            throw new NotImplementedException("the reverse patch did not replace this body");
        }
    }

    public static class OriginalTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            return seed + 7;
        }
    }

    public static class OriginalStandin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            throw new NotImplementedException("the reverse patch did not replace this body");
        }
    }

    public static class UntouchedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            return seed + 8;
        }
    }

    public static class UntouchedStandin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            throw new NotImplementedException("the reverse patch did not replace this body");
        }
    }

    public static class NobodyTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            return seed + 11;
        }
    }

    public static class NobodyStandin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            throw new NotImplementedException("the reverse patch did not replace this body");
        }
    }

    public static class RoutedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            return seed + 9;
        }
    }

    public static class RoutedStandin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            throw new NotImplementedException("the reverse patch did not replace this body");
        }
    }

    public static class TranspiledTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            return seed + 10;
        }
    }

    public static class TranspiledStandin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            IEnumerable<HarmonyLib.CodeInstruction> Transpiler(IEnumerable<HarmonyLib.CodeInstruction> instructions)
            {
                return instructions;
            }

            _ = Transpiler(Array.Empty<HarmonyLib.CodeInstruction>());
            throw new NotImplementedException("the reverse patch did not replace this body");
        }
    }

    public static class Mods
    {
        public static void LiveHead(ControlHandle<int> ch)
        {
            ch.ReturnValue = 99;
            ch.Cancel();
        }

        public static void CounterHead(ControlHandle<int> ch)
        {
            Ran.Add("concord-counter");
        }

        public static void OriginalHead(ControlHandle<int> ch)
        {
            ch.ReturnValue = 99;
            ch.Cancel();
        }

        public static void RoutedHead(ControlHandle<int> ch)
        {
            Ran.Add("concord-routed");
            ch.ReturnValue = 99;
            ch.Cancel();
        }

        public static void RoutedForeignPrefix()
        {
            Ran.Add("foreign-routed");
        }

        public static void UntouchedForeignPrefix()
        {
            Ran.Add("foreign-untouched");
        }

        public static void TranspiledHead(ControlHandle<int> ch)
        {
            ch.ReturnValue = 99;
            ch.Cancel();
        }
    }
}
