using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public sealed class HarmonyProbeBox<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Ping()
        {
            return "orig";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string StaticPing()
        {
            return "orig";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Describe()
        {
            return typeof(T).Name;
        }
    }

    public static class BridgeProbeLog
    {
        public static List<string> Entries = new List<string>();
    }

    public sealed class BridgeProbeBox<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute()
        {
            return 1;
        }
    }

    public static class BridgeProbeMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            BridgeProbeLog.Entries.Add("head");
        }

        public static void ConcordHeadB(ControlHandle ch)
        {
            BridgeProbeLog.Entries.Add("headB");
        }

        public static void HarmonyPostfix()
        {
            BridgeProbeLog.Entries.Add("postfix");
        }
    }

    public static class HarmonyProbePatches
    {
        public static void Postfix(ref string __result)
        {
            __result = "patched";
        }

        public static void Append(ref string __result)
        {
            __result = __result + "+p";
        }
    }

    [Collection("HarmonySerial")]
    public sealed class HarmonySharedGenericProbeTests
    {
        [Fact]
        public void HarmonyPostfixOnRefTypeInstantiation()
        {
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("probe.shared.generic");
            MethodInfo target = typeof(HarmonyProbeBox<string>).GetMethod(nameof(HarmonyProbeBox<string>.Ping));
            MethodInfo postfix = typeof(HarmonyProbePatches).GetMethod(nameof(HarmonyProbePatches.Postfix));
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            try
            {
                string instance = new HarmonyProbeBox<string>().Ping();
                string other = new HarmonyProbeBox<Version>().Ping();
                string valueType = new HarmonyProbeBox<int>().Ping();
                Assert.Equal("patched/patched/orig", instance + "/" + other + "/" + valueType);
            }
            finally
            {
                TestUnpatch.Own(harmony, "probe.shared.generic");
            }
        }

        [Fact]
        public void BridgeRoutedInjectionIsIsolatedToRequestedInstantiation()
        {
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("probe.bridge.shared.generic");
            MethodInfo target = typeof(BridgeProbeBox<string>).GetMethod(nameof(BridgeProbeBox<string>.Compute));
            MethodInfo postfix = typeof(BridgeProbeMods).GetMethod(nameof(BridgeProbeMods.HarmonyPostfix));
            MethodInfo head = typeof(BridgeProbeMods).GetMethod(nameof(BridgeProbeMods.ConcordHead));
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));

            HarmonyBridge bridge = new HarmonyBridge(_ => { });
            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { new Injection(head, new InjectAt.Head(), "test.bridge", 0) }, forceRoute: true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                BridgeProbeLog.Entries.Clear();
                new BridgeProbeBox<string>().Compute();
                Assert.Equal(new List<string> { "head", "postfix" }, BridgeProbeLog.Entries);

                BridgeProbeLog.Entries.Clear();
                new BridgeProbeBox<Version>().Compute();
                Assert.Equal(new List<string> { "postfix" }, BridgeProbeLog.Entries);

                BridgeProbeLog.Entries.Clear();
                new BridgeProbeBox<int>().Compute();
                Assert.Equal(new List<string>(), BridgeProbeLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmony, "probe.bridge.shared.generic");
            }
        }

        [Fact]
        public void BridgeKeepsBothInstantiationsPatchesOnOneSharedBody()
        {
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("probe.bridge.shared.generic.two");
            MethodInfo text = typeof(BridgeProbeBox<string>).GetMethod(nameof(BridgeProbeBox<string>.Compute));
            MethodInfo version = typeof(BridgeProbeBox<Version>).GetMethod(nameof(BridgeProbeBox<Version>.Compute));
            MethodInfo postfix = typeof(BridgeProbeMods).GetMethod(nameof(BridgeProbeMods.HarmonyPostfix));
            MethodInfo headA = typeof(BridgeProbeMods).GetMethod(nameof(BridgeProbeMods.ConcordHead));
            MethodInfo headB = typeof(BridgeProbeMods).GetMethod(nameof(BridgeProbeMods.ConcordHeadB));
            harmony.Patch(text, postfix: new HarmonyMethod(postfix));
            harmony.Patch(version, postfix: new HarmonyMethod(postfix));

            HarmonyBridge bridge = new HarmonyBridge(_ => { });
            ForeignRouteResult a = null;
            ForeignRouteResult b = null;
            try
            {
                a = bridge.TryRoute(text, new[] { new Injection(headA, new InjectAt.Head(), "test.bridge.a", 0) }, forceRoute: true);
                b = bridge.TryRoute(version, new[] { new Injection(headB, new InjectAt.Head(), "test.bridge.b", 0) }, forceRoute: true);
                Assert.Equal(ForeignRouteKind.Routed, a.Kind);
                Assert.Equal(ForeignRouteKind.Routed, b.Kind);

                BridgeProbeLog.Entries.Clear();
                new BridgeProbeBox<string>().Compute();
                Assert.Equal(new List<string> { "head", "postfix" }, BridgeProbeLog.Entries);

                BridgeProbeLog.Entries.Clear();
                new BridgeProbeBox<Version>().Compute();
                Assert.Equal(new List<string> { "headB", "postfix" }, BridgeProbeLog.Entries);

                BridgeProbeLog.Entries.Clear();
                new BridgeProbeBox<int>().Compute();
                Assert.Equal(new List<string>(), BridgeProbeLog.Entries);
            }
            finally
            {
                a?.Handle?.Dispose();
                b?.Handle?.Dispose();
                TestUnpatch.Own(harmony, "probe.bridge.shared.generic.two");
            }
        }

        [Fact]
        public void HarmonyDictionaryReadingBodyOnRefTypeInstantiation()
        {
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("probe.shared.generic.dict");
            MethodInfo target = typeof(HarmonyProbeBox<string>).GetMethod(nameof(HarmonyProbeBox<string>.Describe));
            MethodInfo postfix = typeof(HarmonyProbePatches).GetMethod(nameof(HarmonyProbePatches.Append));
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            try
            {
                string a = new HarmonyProbeBox<string>().Describe();
                string b = new HarmonyProbeBox<Version>().Describe();
                Assert.Equal("String+p/String+p", a + "/" + b);
            }
            finally
            {
                TestUnpatch.Own(harmony, "probe.shared.generic.dict");
            }
        }
    }
}
