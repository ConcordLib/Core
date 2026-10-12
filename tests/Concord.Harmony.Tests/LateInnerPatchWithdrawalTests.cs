using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class LateWithdrawalLog
    {
        public static List<string> Entries { get; } = new List<string>();
    }

    public static class LateWithdrawalHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Step(int x)
        {
            LateWithdrawalLog.Entries.Add("step:" + x);
            return x + 1;
        }
    }

    public static class LateWithdrawalHazardTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            LateWithdrawalLog.Entries.Add("body");
            return LateWithdrawalHelper.Step(1) + LateWithdrawalHelper.Step(2);
        }
    }

    public static class LateWithdrawalDisposeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            LateWithdrawalLog.Entries.Add("body");
            return LateWithdrawalHelper.Step(1) + LateWithdrawalHelper.Step(2);
        }
    }

    public static class LateWithdrawalCompatibleTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            LateWithdrawalLog.Entries.Add("body");
            return LateWithdrawalHelper.Step(1) + LateWithdrawalHelper.Step(2);
        }
    }

    public static class LateWithdrawalPostfixTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            LateWithdrawalLog.Entries.Add("body");
            return 1;
        }
    }

    public static class LateWithdrawalMods
    {
        public static void HeadThatCallsStep(ControlHandle<int> ch)
        {
            LateWithdrawalLog.Entries.Add("concord-head-calls:" + LateWithdrawalHelper.Step(9));
        }

        public static void PlainHead(ControlHandle<int> ch)
        {
            LateWithdrawalLog.Entries.Add("concord-head");
        }

        public static void InnerPrefix()
        {
            LateWithdrawalLog.Entries.Add("harmony-inner");
        }

        public static void ForeignPostfix()
        {
            LateWithdrawalLog.Entries.Add("foreign-postfix");
        }
    }

    [Collection("HarmonySerial")]
    public sealed class LateInnerPatchWithdrawalTests
    {
        private static readonly MethodInfo Step = Method(typeof(LateWithdrawalHelper), nameof(LateWithdrawalHelper.Step));

        private static readonly MethodInfo InnerPrefix = Method(typeof(LateWithdrawalMods), nameof(LateWithdrawalMods.InnerPrefix));

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        [Fact]
        public void LateInfixOnRoutedTarget_WithdrawsConcord_AndTheInfixLandsOnTheBodysCall()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodInfo target = Method(typeof(LateWithdrawalHazardTarget), nameof(LateWithdrawalHazardTarget.Run));
            HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.late.withdraw");
            List<string> messages = new List<string>();
            RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), messages.Add);
            HarmonyBridge bridge = new HarmonyBridge(messages.Add);
            router.RouteEverything = true;

            try
            {
                router.ActivateHost(bridge);
                Assert.True(router.NotifierInstalled);

                using IDetourHandle handle = router.ApplyComposed(
                    target,
                    new[] { HeadOf(nameof(LateWithdrawalMods.HeadThatCallsStep)) });
                Assert.Equal(RouteState.Bridge, router.GetRoute(target));

                LateWithdrawalLog.Entries.Clear();
                LateWithdrawalHazardTarget.Run();
                Assert.Contains("concord-head-calls:10", LateWithdrawalLog.Entries);

                new PatchProcessor(foreign, target)
                    .AddInnerPrefix(HarmonyVersions.Fix(InnerPrefix, HarmonyVersions.InnerMethodSelector(Step, 1), "innerMethod"))
                    .Patch();

                Assert.Equal(RouteState.ContestedLost, router.GetRoute(target));
                Assert.Contains(target, router.ContestedLostTargets());
                Assert.False(handle.IsApplied);

                string? marker = messages.Find(m => m.StartsWith(CoexistenceLogMarkers.RouteWithdrawn));
                Assert.NotNull(marker);
                Assert.Contains("counts by position", marker);

                Assert.DoesNotContain(
                    PatchProcessor.GetPatchInfo(target).Transpilers,
                    p => p.PatchMethod == TranspilerParticipant.TranspileMethod);
                Assert.Empty(bridge.ConcordOwners(target));

                LateWithdrawalLog.Entries.Clear();
                Assert.Equal(5, LateWithdrawalHazardTarget.Run());

                Assert.DoesNotContain(LateWithdrawalLog.Entries, e => e.StartsWith("concord-head-calls"));
                Assert.Equal(
                    new List<string> { "body", "harmony-inner", "step:1", "step:2" },
                    LateWithdrawalLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(foreign, "test.late.withdraw");
                UpdateWrapperHook.DetachObserver();
            }
        }

        [Fact]
        public void CompatibleLateInfix_KeepsBothRunning_AndLogsNoWithdrawal()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodInfo target = Method(typeof(LateWithdrawalCompatibleTarget), nameof(LateWithdrawalCompatibleTarget.Run));
            HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.late.compatible");
            List<string> messages = new List<string>();
            RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), messages.Add);
            HarmonyBridge bridge = new HarmonyBridge(messages.Add);
            router.RouteEverything = true;

            try
            {
                router.ActivateHost(bridge);

                using IDetourHandle handle = router.ApplyComposed(
                    target,
                    new[] { HeadOf(nameof(LateWithdrawalMods.PlainHead)) });
                Assert.Equal(RouteState.Bridge, router.GetRoute(target));

                new PatchProcessor(foreign, target)
                    .AddInnerPrefix(HarmonyVersions.Fix(InnerPrefix, HarmonyVersions.InnerMethodSelector(Step), "innerMethod"))
                    .Patch();

                Assert.Equal(RouteState.Bridge, router.GetRoute(target));
                Assert.DoesNotContain(messages, m => m.StartsWith(CoexistenceLogMarkers.RouteWithdrawn));

                LateWithdrawalLog.Entries.Clear();
                Assert.Equal(5, LateWithdrawalCompatibleTarget.Run());
                Assert.Equal(
                    new List<string> { "concord-head", "body", "harmony-inner", "step:1", "harmony-inner", "step:2" },
                    LateWithdrawalLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(foreign, "test.late.compatible");
                UpdateWrapperHook.DetachObserver();
            }
        }

        [Fact]
        public void DisposingAWithdrawnHandle_IsANoOp_AndLeavesHarmonysPatchesAlone()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodInfo target = Method(typeof(LateWithdrawalDisposeTarget), nameof(LateWithdrawalDisposeTarget.Run));
            HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.late.dispose");
            List<string> messages = new List<string>();
            RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), messages.Add);
            HarmonyBridge bridge = new HarmonyBridge(messages.Add);
            router.RouteEverything = true;
            IDetourHandle? handle = null;

            try
            {
                router.ActivateHost(bridge);

                handle = router.ApplyComposed(
                    target,
                    new[] { HeadOf(nameof(LateWithdrawalMods.HeadThatCallsStep)) });

                new PatchProcessor(foreign, target)
                    .AddInnerPrefix(HarmonyVersions.Fix(InnerPrefix, HarmonyVersions.InnerMethodSelector(Step, 1), "innerMethod"))
                    .Patch();
                Assert.Equal(RouteState.ContestedLost, router.GetRoute(target));

                int rebuilds = 0;
                bridge.RemoveParticipant = _ => rebuilds++;
                bridge.InstallParticipant = _ => rebuilds++;

                handle.Dispose();
                handle = null;

                Assert.Equal(0, rebuilds);

                LateWithdrawalLog.Entries.Clear();
                Assert.Equal(5, LateWithdrawalDisposeTarget.Run());
                Assert.Equal(
                    new List<string> { "body", "harmony-inner", "step:1", "step:2" },
                    LateWithdrawalLog.Entries);
            }
            finally
            {
                handle?.Dispose();
                TestUnpatch.Own(foreign, "test.late.dispose");
                UpdateWrapperHook.DetachObserver();
            }
        }

        [Fact]
        public void LatePlainPostfixOnRoutedTarget_DoesNotWithdraw()
        {
            MethodInfo target = Method(typeof(LateWithdrawalPostfixTarget), nameof(LateWithdrawalPostfixTarget.Run));
            HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.late.postfix");
            List<string> messages = new List<string>();
            RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), messages.Add);
            HarmonyBridge bridge = new HarmonyBridge(messages.Add);
            router.RouteEverything = true;

            try
            {
                router.ActivateHost(bridge);

                using IDetourHandle handle = router.ApplyComposed(
                    target,
                    new[] { HeadOf(nameof(LateWithdrawalMods.PlainHead)) });
                Assert.Equal(RouteState.Bridge, router.GetRoute(target));

                foreign.Patch(
                    target,
                    postfix: new HarmonyMethod(Method(typeof(LateWithdrawalMods), nameof(LateWithdrawalMods.ForeignPostfix))));

                Assert.Equal(RouteState.Bridge, router.GetRoute(target));
                Assert.DoesNotContain(messages, m => m.StartsWith(CoexistenceLogMarkers.RouteWithdrawn));

                LateWithdrawalLog.Entries.Clear();
                Assert.Equal(1, LateWithdrawalPostfixTarget.Run());
                Assert.Equal(
                    new List<string> { "concord-head", "body", "foreign-postfix" },
                    LateWithdrawalLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(foreign, "test.late.postfix");
                UpdateWrapperHook.DetachObserver();
            }
        }

        private static Injection HeadOf(string name)
        {
            return new Injection(Method(typeof(LateWithdrawalMods), name), new InjectAt.Head(), "test.concord.late", 0);
        }
    }
}
