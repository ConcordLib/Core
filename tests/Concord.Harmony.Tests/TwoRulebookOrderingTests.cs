using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class TwoRulebookLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class TwoRulebookTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int PriorityPair()
        {
            TwoRulebookLog.Entries.Add("body");
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int DependencyPair()
        {
            TwoRulebookLog.Entries.Add("body");
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Layering()
        {
            TwoRulebookLog.Entries.Add("body");
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CycleShape()
        {
            TwoRulebookLog.Entries.Add("body");
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ReturnLayering()
        {
            TwoRulebookLog.Entries.Add("body");
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CatchLayering(int seed)
        {
            TwoRulebookLog.Entries.Add("body");
            if (seed < 0)
            {
                throw new InvalidOperationException("boom");
            }

            return seed + 1;
        }
    }

    public static class TwoRulebookHarmonyMods
    {
        public static void PrefixA()
        {
            TwoRulebookLog.Entries.Add("harmonyA");
        }

        public static void PrefixB()
        {
            TwoRulebookLog.Entries.Add("harmonyB");
        }

        public static void PostfixA()
        {
            TwoRulebookLog.Entries.Add("harmonyPostfixA");
        }

        public static void PostfixResult(ref int __result)
        {
            TwoRulebookLog.Entries.Add("harmonyPostfix:" + __result);
            __result = __result + 1;
        }

        public static Exception? FinalizerResult(Exception? __exception, ref int __result)
        {
            TwoRulebookLog.Entries.Add("harmonyFinalizer:" + __result);
            __result = __result * 2;
            return null;
        }
    }

    public static class TwoRulebookConcordMods
    {
        public static void Head(ControlHandle ch)
        {
            TwoRulebookLog.Entries.Add("concordHead");
        }

        public static void Return(ControlHandle<int> ch)
        {
            TwoRulebookLog.Entries.Add("concordReturn:" + ch.ReturnValue);
            ch.ReturnValue = 100;
        }

        public static void Catch(ControlHandle<int> ch)
        {
            TwoRulebookLog.Entries.Add("concordCatch:" + ch.ReturnValue);
            ch.ReturnValue = 42;
        }
    }

    [Collection("HarmonySerial")]
    public sealed class TwoRulebookOrderingTests
    {
        private const string OwnerA = "test.tworulebook.a";
        private const string OwnerB = "test.tworulebook.b";
        private const string ConcordOwner = "test.tworulebook.concord";
        private const string ForeignOwner = "test.harmony";

        [Fact]
        public void TwoHarmonyPrefixes_Priority_KeepRecordedOrderWhileConcordIsRouted()
        {
            MethodInfo target = Method(typeof(TwoRulebookTargets), nameof(TwoRulebookTargets.PriorityPair));
            HarmonyLib.Harmony modA = new HarmonyLib.Harmony(OwnerA);
            HarmonyLib.Harmony modB = new HarmonyLib.Harmony(OwnerB);
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult? result = null;
            try
            {
                modA.Patch(target, prefix: new HarmonyMethod(PrefixA()) { priority = 200 });
                modB.Patch(target, prefix: new HarmonyMethod(PrefixB()) { priority = 600 });

                result = bridge.TryRoute(target, new[] { Head() }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                TwoRulebookLog.Entries.Clear();
                Assert.Equal(7, TwoRulebookTargets.PriorityPair());

                Assert.Equal(new List<string> { "harmonyB", "harmonyA", "concordHead", "body" }, TwoRulebookLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(modA, OwnerA);
                TestUnpatch.Own(modB, OwnerB);
            }
        }

        [Fact]
        public void TwoHarmonyPrefixes_BeforeDependency_KeepRecordedOrderWhileConcordIsRouted()
        {
            MethodInfo target = Method(typeof(TwoRulebookTargets), nameof(TwoRulebookTargets.DependencyPair));
            HarmonyLib.Harmony modA = new HarmonyLib.Harmony(OwnerA);
            HarmonyLib.Harmony modB = new HarmonyLib.Harmony(OwnerB);
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult? result = null;
            try
            {
                modA.Patch(target, prefix: new HarmonyMethod(PrefixA()) { after = new[] { OwnerB } });
                modB.Patch(target, prefix: new HarmonyMethod(PrefixB()));

                result = bridge.TryRoute(target, new[] { Head() }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                TwoRulebookLog.Entries.Clear();
                Assert.Equal(7, TwoRulebookTargets.DependencyPair());

                Assert.Equal(new List<string> { "harmonyB", "harmonyA", "concordHead", "body" }, TwoRulebookLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(modA, OwnerA);
                TestUnpatch.Own(modB, OwnerB);
            }
        }

        [Fact]
        public void TwoConcordInjections_PriorityAndDependency_KeepRecordedOrder()
        {
            (long Seq, Injection Injection)[] byPriority =
            [
                (0, MakeInjection("early-low", 0)),
                (1, MakeInjection("late-low", 0)),
                (2, MakeInjection("high", 1))
            ];

            string[] priorityOrder = Enumerable
                .Reverse(InjectionOrderer.OrderForComposition(byPriority))
                .Select(injection => injection.Owner)
                .ToArray();

            Assert.Equal(new[] { "late-low", "early-low", "high" }, priorityOrder);

            (long Seq, Injection Injection)[] byDependency =
            [
                (0, MakeInjection("constrained", 10, before: new[] { "pinned" })),
                (1, MakeInjection("pinned", 0))
            ];

            string[] dependencyOrder = Enumerable
                .Reverse(InjectionOrderer.OrderForComposition(byDependency))
                .Select(injection => injection.Owner)
                .ToArray();

            Assert.Equal(new[] { "constrained", "pinned" }, dependencyOrder);

            (long Seq, Injection Injection)[] byAfter =
            [
                (0, MakeInjection("trailing", 0, after: new[] { "leading" })),
                (1, MakeInjection("leading", 10))
            ];

            string[] afterOrder = Enumerable
                .Reverse(InjectionOrderer.OrderForComposition(byAfter))
                .Select(injection => injection.Owner)
                .ToArray();

            Assert.Equal(new[] { "leading", "trailing" }, afterOrder);
        }

        [Fact]
        public void HarmonyPatchAgainstConcordInjection_HarmonyStaysOutside()
        {
            MethodInfo target = Method(typeof(TwoRulebookTargets), nameof(TwoRulebookTargets.Layering));
            HarmonyLib.Harmony modA = new HarmonyLib.Harmony(OwnerA);
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult? result = null;
            try
            {
                modA.Patch(
                    target,
                    prefix: new HarmonyMethod(PrefixA()),
                    postfix: new HarmonyMethod(PostfixA()));

                result = bridge.TryRoute(target, new[] { Head() }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                TwoRulebookLog.Entries.Clear();
                Assert.Equal(7, TwoRulebookTargets.Layering());

                Assert.Equal(
                    new List<string> { "harmonyA", "concordHead", "body", "harmonyPostfixA" },
                    TwoRulebookLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(modA, OwnerA);
            }
        }

        [Fact]
        public void HarmonyOrderingCycle_IsStillCulledTheSameWay()
        {
            MethodInfo target = Method(typeof(TwoRulebookTargets), nameof(TwoRulebookTargets.CycleShape));
            Patch first = new Patch(new HarmonyMethod(PrefixA()) { before = new[] { OwnerB } }, 0, OwnerA);
            Patch second = new Patch(new HarmonyMethod(PrefixB()) { before = new[] { OwnerA } }, 1, OwnerB);

            IReadOnlyList<Patch> sorted = HarmonyPatchSort.SortCategory(target, new[] { first, second });

            Assert.Equal(new[] { OwnerB, OwnerA }, sorted.Select(patch => patch.owner).ToArray());
            Assert.Equal(new[] { "PrefixB", "PrefixA" }, sorted.Select(patch => patch.GetMethod(target).Name).ToArray());
        }

        [Fact]
        public void HarmonyOrderingCycle_IsCulledTheSameWayWhileConcordIsRouted()
        {
            MethodInfo target = Method(typeof(TwoRulebookTargets), nameof(TwoRulebookTargets.CycleShape));
            HarmonyLib.Harmony modA = new HarmonyLib.Harmony(OwnerA);
            HarmonyLib.Harmony modB = new HarmonyLib.Harmony(OwnerB);
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult? result = null;
            try
            {
                modA.Patch(target, prefix: new HarmonyMethod(PrefixA()) { before = new[] { OwnerB } });
                modB.Patch(target, prefix: new HarmonyMethod(PrefixB()) { before = new[] { OwnerA } });

                result = bridge.TryRoute(target, new[] { Head() }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                TwoRulebookLog.Entries.Clear();
                Assert.Equal(7, TwoRulebookTargets.CycleShape());

                Assert.Equal(new List<string> { "harmonyB", "harmonyA", "concordHead", "body" }, TwoRulebookLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(modA, OwnerA);
                TestUnpatch.Own(modB, OwnerB);
            }
        }

        [Fact]
        public void MappedHarmonyPostfix_RunsOutsideTheConcordReturnInjection_AndSeesItsWrite()
        {
            MethodInfo target = Method(typeof(TwoRulebookTargets), nameof(TwoRulebookTargets.ReturnLayering));
            MethodInfo postfix = Method(typeof(TwoRulebookHarmonyMods), nameof(TwoRulebookHarmonyMods.PostfixResult));

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, new Injection(ReturnMethod(), new InjectAt.Return(0), ConcordOwner, 0))
            };

            foreach (Injection mapped in PostfixMapping.Map(target, postfix, ForeignOwner, 400))
            {
                live.Add((live.Count, mapped));
            }

            Func<int> wrapper = Weave<Func<int>>(target, live);

            TwoRulebookLog.Entries.Clear();
            int observed = wrapper();

            Assert.Contains("harmonyPostfix:100", TwoRulebookLog.Entries);
            Assert.Equal(101, observed);
        }

        [Fact]
        public void MappedHarmonyFinalizer_RunsOutsideTheConcordCatchInjection_AndSeesItsWrite()
        {
            MethodInfo target = Method(typeof(TwoRulebookTargets), nameof(TwoRulebookTargets.CatchLayering));
            MethodInfo finalizer = Method(typeof(TwoRulebookHarmonyMods), nameof(TwoRulebookHarmonyMods.FinalizerResult));

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, new Injection(CatchMethod(), new InjectAt.Catch(), ConcordOwner, 0))
            };

            foreach (Injection mapped in FinalizerParticipant.Map(target, finalizer, ForeignOwner, 400))
            {
                live.Add((live.Count, mapped));
            }

            Func<int, int> wrapper = Weave<Func<int, int>>(target, live);

            TwoRulebookLog.Entries.Clear();
            int observed = wrapper(-1);

            Assert.Contains("harmonyFinalizer:42", TwoRulebookLog.Entries);
            Assert.Equal(84, observed);
        }

        private static TDelegate Weave<TDelegate>(MethodInfo target, IReadOnlyList<(long Seq, Injection Injection)> live)
            where TDelegate : Delegate
        {
            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            return (TDelegate)composed.Wrapper.CreateDelegate(typeof(TDelegate));
        }

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        private static MethodInfo ReturnMethod()
        {
            return Method(typeof(TwoRulebookConcordMods), nameof(TwoRulebookConcordMods.Return));
        }

        private static MethodInfo CatchMethod()
        {
            return Method(typeof(TwoRulebookConcordMods), nameof(TwoRulebookConcordMods.Catch));
        }

        private static Injection MakeInjection(string owner, int priority, string[]? before = null, string[]? after = null)
        {
            return new Injection(HeadMethod(), new InjectAt.Head(), owner, priority)
            {
                BeforeOwners = before ?? new string[0],
                AfterOwners = after ?? new string[0]
            };
        }

        private static Injection Head()
        {
            return new Injection(HeadMethod(), new InjectAt.Head(), ConcordOwner, 0);
        }

        private static MethodInfo HeadMethod()
        {
            return Method(typeof(TwoRulebookConcordMods), nameof(TwoRulebookConcordMods.Head));
        }

        private static MethodInfo PrefixA()
        {
            return Method(typeof(TwoRulebookHarmonyMods), nameof(TwoRulebookHarmonyMods.PrefixA));
        }

        private static MethodInfo PrefixB()
        {
            return Method(typeof(TwoRulebookHarmonyMods), nameof(TwoRulebookHarmonyMods.PrefixB));
        }

        private static MethodInfo PostfixA()
        {
            return Method(typeof(TwoRulebookHarmonyMods), nameof(TwoRulebookHarmonyMods.PostfixA));
        }
    }
}
