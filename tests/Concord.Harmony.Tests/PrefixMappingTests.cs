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
    public static class PrefixMappingLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class PrefixMappingTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int OneSkipInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void OneSkipVoid(int x)
        {
            PrefixMappingLog.Entries.Add("body");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TwoBoolInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TwoBoolVoid(int x)
        {
            PrefixMappingLog.Entries.Add("body");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TwoVoidInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TwoVoidVoid(int x)
        {
            PrefixMappingLog.Entries.Add("body");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int NoResultInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void NoResultVoid(int x)
        {
            PrefixMappingLog.Entries.Add("body");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int VoidPrefixInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void VoidPrefixVoid(int x)
        {
            PrefixMappingLog.Entries.Add("body");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ConcordHeadInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TwoAffectingVoidInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TwoNonAffectingArgInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ConcordSkipHeadInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ConcordSkipFirstInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CarveOutInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ControlInt(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }
    }

    public sealed class CancelGuardTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Method(int x)
        {
            PrefixMappingLog.Entries.Add("body");
            return x * 2;
        }
    }

    public static class CancelGuardPatches
    {
        public static bool CancelsOnInstance(CancelGuardTarget __instance, ref int __result)
        {
            PrefixMappingLog.Entries.Add("cancelInst");
            __result = -1;
            return false;
        }

        public static void AffectingNoInstance(ref int __result)
        {
            PrefixMappingLog.Entries.Add("affecting:" + __result);
        }

        public static void ReadsInstanceOnly(CancelGuardTarget __instance)
        {
            PrefixMappingLog.Entries.Add("readsInstance:" + (__instance != null));
        }
    }

    public static class PrefixMappingHarmonyMethods
    {
        public static bool SkipWithResult(ref int __result)
        {
            PrefixMappingLog.Entries.Add("skip");
            __result = -100;
            return false;
        }

        public static bool Skip()
        {
            PrefixMappingLog.Entries.Add("skip");
            return false;
        }

        public static bool SecondBool()
        {
            PrefixMappingLog.Entries.Add("second");
            return true;
        }

        public static void SecondVoid()
        {
            PrefixMappingLog.Entries.Add("second");
        }

        public static void SecondReadsArg(int x)
        {
            PrefixMappingLog.Entries.Add("second:" + x);
        }

        public static void Observe()
        {
            PrefixMappingLog.Entries.Add("observe");
        }

        public static void ObserveResult(ref int __result)
        {
            PrefixMappingLog.Entries.Add("observe");
        }

        public static void Postfix(ref int __result)
        {
            PrefixMappingLog.Entries.Add("postfix:" + __result);
            __result += 1;
        }

        public static void PostfixVoid()
        {
            PrefixMappingLog.Entries.Add("postfix");
        }
    }

    public static class PrefixMappingConcordMethods
    {
        public static void Postfix(ControlHandle<int> control)
        {
            PrefixMappingLog.Entries.Add("postfix:" + control.ReturnValue);
            control.ReturnValue += 1;
        }

        public static void PostfixVoid(ControlHandle control)
        {
            PrefixMappingLog.Entries.Add("postfix");
        }

        public static void Head(ControlHandle control)
        {
            PrefixMappingLog.Entries.Add("head");
        }

        public static void CancelWithoutReturn(ControlHandle<int> control)
        {
            PrefixMappingLog.Entries.Add("cancel");
            control.Cancel();
        }

        public static void CancelSkipping(ControlHandle<int> control)
        {
            PrefixMappingLog.Entries.Add("concordA");
            control.ReturnValue = 5;
            control.Cancel(true);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class PrefixMappingTests
    {
        private const int High = 600;
        private const int Low = 200;

        [Fact]
        public void OnePrefixReturnsFalse_SkipsOriginal_PostfixStillRuns_Int()
        {
            AssertIntParity(
                nameof(PrefixMappingTargets.OneSkipInt),
                x => PrefixMappingTargets.OneSkipInt(x),
                "ok:-99|skip,postfix:-100",
                Pre(nameof(PrefixMappingHarmonyMethods.SkipWithResult), "test.a", High));
        }

        [Fact]
        public void OnePrefixReturnsFalse_SkipsOriginal_PostfixStillRuns_Void()
        {
            AssertVoidParity(
                nameof(PrefixMappingTargets.OneSkipVoid),
                x => PrefixMappingTargets.OneSkipVoid(x),
                "ok|skip,postfix",
                Pre(nameof(PrefixMappingHarmonyMethods.Skip), "test.a", High));
        }

        [Fact]
        public void TwoPrefixes_FirstReturnsFalse_BoolSecondIsSkipped_Int()
        {
            AssertIntParity(
                nameof(PrefixMappingTargets.TwoBoolInt),
                x => PrefixMappingTargets.TwoBoolInt(x),
                "ok:-99|skip,postfix:-100",
                Pre(nameof(PrefixMappingHarmonyMethods.SkipWithResult), "test.a", High),
                Pre(nameof(PrefixMappingHarmonyMethods.SecondBool), "test.b", Low));
        }

        [Fact]
        public void TwoPrefixes_FirstReturnsFalse_BoolSecondIsSkipped_Void()
        {
            AssertVoidParity(
                nameof(PrefixMappingTargets.TwoBoolVoid),
                x => PrefixMappingTargets.TwoBoolVoid(x),
                "ok|skip,postfix",
                Pre(nameof(PrefixMappingHarmonyMethods.Skip), "test.a", High),
                Pre(nameof(PrefixMappingHarmonyMethods.SecondBool), "test.b", Low));
        }

        [Fact]
        public void TwoPrefixes_FirstReturnsFalse_VoidSecondStillRuns_Int()
        {
            AssertIntParity(
                nameof(PrefixMappingTargets.TwoVoidInt),
                x => PrefixMappingTargets.TwoVoidInt(x),
                "ok:-99|skip,second,postfix:-100",
                Pre(nameof(PrefixMappingHarmonyMethods.SkipWithResult), "test.a", High),
                Pre(nameof(PrefixMappingHarmonyMethods.SecondVoid), "test.b", Low));
        }

        [Fact]
        public void TwoPrefixes_FirstReturnsFalse_VoidSecondStillRuns_Void()
        {
            AssertVoidParity(
                nameof(PrefixMappingTargets.TwoVoidVoid),
                x => PrefixMappingTargets.TwoVoidVoid(x),
                "ok|skip,second,postfix",
                Pre(nameof(PrefixMappingHarmonyMethods.Skip), "test.a", High),
                Pre(nameof(PrefixMappingHarmonyMethods.SecondVoid), "test.b", Low));
        }

        [Fact]
        public void TwoPrefixes_FirstReturnsFalse_AffectingVoidSecondIsSkipped_Int()
        {
            AssertIntParity(
                nameof(PrefixMappingTargets.TwoAffectingVoidInt),
                x => PrefixMappingTargets.TwoAffectingVoidInt(x),
                "ok:-99|skip,postfix:-100",
                Pre(nameof(PrefixMappingHarmonyMethods.SkipWithResult), "test.a", High),
                Pre(nameof(PrefixMappingHarmonyMethods.ObserveResult), "test.b", Low));
        }

        [Fact]
        public void TwoPrefixes_FirstReturnsFalse_NonAffectingArgSecondStillRuns_Int()
        {
            AssertIntParity(
                nameof(PrefixMappingTargets.TwoNonAffectingArgInt),
                x => PrefixMappingTargets.TwoNonAffectingArgInt(x),
                "ok:-99|skip,second:4,postfix:-100",
                Pre(nameof(PrefixMappingHarmonyMethods.SkipWithResult), "test.a", High),
                Pre(nameof(PrefixMappingHarmonyMethods.SecondReadsArg), "test.b", Low));
        }

        [Fact]
        public void ForeignPrefixReturnsFalse_ConcordHeadCancellingWithSkipNeverRuns()
        {
            MethodInfo target = Find(typeof(PrefixMappingTargets), nameof(PrefixMappingTargets.ConcordSkipHeadInt));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, PrefixMapping.Map(target, Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.SkipWithResult)), "test.a")),
                (1, new Injection(Find(typeof(PrefixMappingConcordMethods), nameof(PrefixMappingConcordMethods.CancelSkipping)), new InjectAt.Head(), "test.concord", 0)),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            string[] outcome = Record(x => "ok:" + wrapper(x));

            Assert.Equal("ok:-100|skip", outcome[0]);
        }

        [Fact]
        public void ForeignPrefixReturnsFalse_StillRunsFirstWhenTheConcordHeadRegisteredEarlier()
        {
            MethodInfo target = Find(typeof(PrefixMappingTargets), nameof(PrefixMappingTargets.ConcordSkipFirstInt));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, new Injection(Find(typeof(PrefixMappingConcordMethods), nameof(PrefixMappingConcordMethods.CancelSkipping)), new InjectAt.Head(), "test.concord", 0)),
                (1, PrefixMapping.Map(target, Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.SkipWithResult)), "test.a")),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            string[] outcome = Record(x => "ok:" + wrapper(x));

            Assert.Equal("ok:-100|skip", outcome[0]);
        }

        [Fact]
        public void InstanceBoundCancel_SkipsSiblingPrefixWithoutInstance_MatchesHarmony()
        {
            MethodInfo target = Find(typeof(CancelGuardTarget), nameof(CancelGuardTarget.Method));
            MethodInfo cancels = Find(typeof(CancelGuardPatches), nameof(CancelGuardPatches.CancelsOnInstance));
            MethodInfo affecting = Find(typeof(CancelGuardPatches), nameof(CancelGuardPatches.AffectingNoInstance));

            string harmonyId = "test.prefixmap.cancelguard";
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            string viaHarmony;
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(cancels) { priority = High });
                harmony.Patch(target, prefix: new HarmonyMethod(affecting) { priority = Low });
                CancelGuardTarget instance = new CancelGuardTarget();
                viaHarmony = Record(_ => "ok:" + instance.Method(4))[0];
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, PrefixMapping.Map(target, cancels, "test.a")),
                (1, PrefixMapping.Map(target, affecting, "test.b")),
            };
            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            Func<CancelGuardTarget, int, int> wrapper =
                (Func<CancelGuardTarget, int, int>)composed.Wrapper.CreateDelegate(typeof(Func<CancelGuardTarget, int, int>));
            CancelGuardTarget concordInstance = new CancelGuardTarget();
            string viaConcord = Record(_ => "ok:" + wrapper(concordInstance, 4))[0];

            Assert.Equal("ok:-1|cancelInst", viaHarmony);
            Assert.Equal(viaHarmony, viaConcord);
        }

        [Fact]
        public void InstanceBoundCancel_SiblingInstanceOnlyPrefixStillRuns_MatchesHarmony()
        {
            MethodInfo target = Find(typeof(CancelGuardTarget), nameof(CancelGuardTarget.Method));
            MethodInfo cancels = Find(typeof(CancelGuardPatches), nameof(CancelGuardPatches.CancelsOnInstance));
            MethodInfo readsInstance = Find(typeof(CancelGuardPatches), nameof(CancelGuardPatches.ReadsInstanceOnly));

            string harmonyId = "test.prefixmap.cancelguardinstance";
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            string viaHarmony;
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(cancels) { priority = High });
                harmony.Patch(target, prefix: new HarmonyMethod(readsInstance) { priority = Low });
                CancelGuardTarget instance = new CancelGuardTarget();
                viaHarmony = Record(_ => "ok:" + instance.Method(4))[0];
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, PrefixMapping.Map(target, cancels, "test.a")),
                (1, PrefixMapping.Map(target, readsInstance, "test.b")),
            };
            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            Func<CancelGuardTarget, int, int> wrapper =
                (Func<CancelGuardTarget, int, int>)composed.Wrapper.CreateDelegate(typeof(Func<CancelGuardTarget, int, int>));
            CancelGuardTarget concordInstance = new CancelGuardTarget();
            string viaConcord = Record(_ => "ok:" + wrapper(concordInstance, 4))[0];

            Assert.Equal("ok:-1|cancelInst,readsInstance:True", viaHarmony);
            Assert.Equal(viaHarmony, viaConcord);
        }

        [Fact]
        public void PrefixReturnsFalseWithoutResult_ComposesAndReturnsDefault_Int()
        {
            AssertIntParity(
                nameof(PrefixMappingTargets.NoResultInt),
                x => PrefixMappingTargets.NoResultInt(x),
                "ok:1|skip,postfix:0",
                Pre(nameof(PrefixMappingHarmonyMethods.Skip), "test.a", High));
        }

        [Fact]
        public void PrefixReturnsFalseWithoutResult_Composes_Void()
        {
            AssertVoidParity(
                nameof(PrefixMappingTargets.NoResultVoid),
                x => PrefixMappingTargets.NoResultVoid(x),
                "ok|skip,postfix",
                Pre(nameof(PrefixMappingHarmonyMethods.Skip), "test.a", High));
        }

        [Fact]
        public void VoidPrefix_MapsToHead_AndOriginalRuns_Int()
        {
            MethodInfo target = Find(typeof(PrefixMappingTargets), nameof(PrefixMappingTargets.VoidPrefixInt));
            Injection mapped = PrefixMapping.Map(target, Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.ObserveResult)), "test.a");

            Assert.IsType<InjectAt.Head>(mapped.At);
            Assert.True(mapped.ForeignOrigin);

            AssertIntParity(
                nameof(PrefixMappingTargets.VoidPrefixInt),
                x => PrefixMappingTargets.VoidPrefixInt(x),
                "ok:9|observe,body,postfix:8",
                Pre(nameof(PrefixMappingHarmonyMethods.ObserveResult), "test.a", High));
        }

        [Fact]
        public void VoidPrefix_MapsToHead_AndOriginalRuns_Void()
        {
            AssertVoidParity(
                nameof(PrefixMappingTargets.VoidPrefixVoid),
                x => PrefixMappingTargets.VoidPrefixVoid(x),
                "ok|observe,body,postfix",
                Pre(nameof(PrefixMappingHarmonyMethods.Observe), "test.a", High));
        }

        [Fact]
        public void ForeignPrefixReturnsFalse_NonAffectingConcordHeadStillRuns()
        {
            MethodInfo target = Find(typeof(PrefixMappingTargets), nameof(PrefixMappingTargets.ConcordHeadInt));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, PrefixMapping.Map(target, Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.SkipWithResult)), "test.a")),
                (1, new Injection(Find(typeof(PrefixMappingConcordMethods), nameof(PrefixMappingConcordMethods.Head)), new InjectAt.Head(), "test.concord", 0)),
            };

            Func<int, int> wrapper = ComposeInt(target, live);
            string[] outcome = Record(x => "ok:" + wrapper(x));

            Assert.Equal("ok:-100|skip,head", outcome[0]);
        }

        [Fact]
        public void ForeignHeadCancelsWithoutReturn_ComposesWithoutCONC012()
        {
            MethodInfo target = Find(typeof(PrefixMappingTargets), nameof(PrefixMappingTargets.CarveOutInt));
            Injection head = new Injection(Find(typeof(PrefixMappingConcordMethods), nameof(PrefixMappingConcordMethods.CancelWithoutReturn)), new InjectAt.Head(), "test.foreign", 0)
            {
                ForeignOrigin = true,
            };

            Func<int, int> wrapper = ComposeInt(target, new List<(long Seq, Injection Injection)> { (0, head) });
            string[] outcome = Record(x => "ok:" + wrapper(x));

            Assert.Equal("ok:0|cancel", outcome[0]);
        }

        [Fact]
        public void ConcordHeadCancelsWithoutReturn_StillThrowsCONC012()
        {
            MethodInfo target = Find(typeof(PrefixMappingTargets), nameof(PrefixMappingTargets.ControlInt));
            Injection head = new Injection(Find(typeof(PrefixMappingConcordMethods), nameof(PrefixMappingConcordMethods.CancelWithoutReturn)), new InjectAt.Head(), "test.concord", 0);

            ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, new[] { head }));

            Assert.Equal("CONC012", error.Code);
        }

        [Fact]
        public void AffectsOriginal_MatchesHarmonyRule()
        {
            Assert.True(PrefixMapping.AffectsOriginal(Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.SecondBool))));
            Assert.True(PrefixMapping.AffectsOriginal(Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.ObserveResult))));
            Assert.False(PrefixMapping.AffectsOriginal(Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.SecondVoid))));
        }

        [Fact]
        public void Validate_RejectsWhatTheMappingDoesNotCover()
        {
            MethodInfo intTarget = Find(typeof(PrefixMappingTargets), nameof(PrefixMappingTargets.ControlInt));
            MethodInfo voidTarget = Find(typeof(PrefixMappingTargets), nameof(PrefixMappingTargets.OneSkipVoid));
            MethodInfo withResult = Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.SkipWithResult));
            MethodInfo postfix = Find(typeof(PrefixMappingConcordMethods), nameof(PrefixMappingConcordMethods.Postfix));

            Assert.Contains("returns void", PrefixMapping.Validate(voidTarget, withResult));
            string? refusal = PrefixMapping.Validate(intTarget, postfix);
            Assert.NotNull(refusal);
            Assert.Contains("takes 'ControlHandle`1 control'", refusal);
            Assert.Contains("declares no parameter of that name and type", refusal);
            Assert.DoesNotContain("argument binder", refusal);
            Assert.Null(PrefixMapping.Validate(intTarget, withResult));
        }

        private sealed class Prefix
        {
            public Prefix(string owner, int priority, MethodInfo method)
            {
                Owner = owner;
                Priority = priority;
                Method = method;
            }

            public string Owner;
            public int Priority;
            public MethodInfo Method;
        }

        private static MethodInfo Find(Type owner, string name)
        {
            MethodInfo? found = owner.GetMethod(name);
            Assert.NotNull(found);
            return found;
        }

        private static Prefix Pre(string name, string owner, int priority)
        {
            return new Prefix(owner, priority, Find(typeof(PrefixMappingHarmonyMethods), name));
        }

        private static void AssertIntParity(string targetName, Func<int, int> call, string expected, params Prefix[] prefixes)
        {
            MethodInfo target = Find(typeof(PrefixMappingTargets), targetName);
            MethodInfo postfix = Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.Postfix));
            string[] baseline = Record(x => "ok:" + call(x));

            string[] viaHarmony = RunHarmony(target, prefixes, postfix, () => Record(x => "ok:" + call(x)));

            AssertSame("reset vs baseline", baseline, Record(x => "ok:" + call(x)));

            List<(long Seq, Injection Injection)> live = Mapped(target, prefixes);
            live.Add((live.Count, new Injection(Find(typeof(PrefixMappingConcordMethods), nameof(PrefixMappingConcordMethods.Postfix)), new InjectAt.Return(), "test.postfix", 0) { ForeignOrigin = true }));
            Func<int, int> wrapper = ComposeInt(target, live);
            string[] viaConcord = Record(x => "ok:" + wrapper(x));

            AssertSame("harmony vs expected", new[] { expected }, viaHarmony);
            AssertSame("concord vs harmony", viaHarmony, viaConcord);
        }

        private static void AssertVoidParity(string targetName, Action<int> call, string expected, params Prefix[] prefixes)
        {
            MethodInfo target = Find(typeof(PrefixMappingTargets), targetName);
            MethodInfo postfix = Find(typeof(PrefixMappingHarmonyMethods), nameof(PrefixMappingHarmonyMethods.PostfixVoid));
            Func<int, string> voidCall = x =>
            {
                call(x);
                return "ok";
            };
            string[] baseline = Record(voidCall);

            string[] viaHarmony = RunHarmony(target, prefixes, postfix, () => Record(voidCall));

            AssertSame("reset vs baseline", baseline, Record(voidCall));

            List<(long Seq, Injection Injection)> live = Mapped(target, prefixes);
            live.Add((live.Count, new Injection(Find(typeof(PrefixMappingConcordMethods), nameof(PrefixMappingConcordMethods.PostfixVoid)), new InjectAt.Return(), "test.postfix", 0) { ForeignOrigin = true }));
            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            Action<int> wrapper = (Action<int>)composed.Wrapper.CreateDelegate(typeof(Action<int>));
            string[] viaConcord = Record(x =>
            {
                wrapper(x);
                return "ok";
            });

            AssertSame("harmony vs expected", new[] { expected }, viaHarmony);
            AssertSame("concord vs harmony", viaHarmony, viaConcord);
        }

        private static string[] RunHarmony(MethodInfo target, Prefix[] prefixes, MethodInfo postfix, Func<string[]> record)
        {
            string harmonyId = "test.prefixmap." + target.Name;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            try
            {
                foreach (Prefix prefix in prefixes)
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(prefix.Method) { priority = prefix.Priority });
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix) { priority = High });
                return record();
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }
        }

        private static List<(long Seq, Injection Injection)> Mapped(MethodInfo target, Prefix[] prefixes)
        {
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Prefix prefix in prefixes)
            {
                live.Add((live.Count, PrefixMapping.Map(target, prefix.Method, prefix.Owner)));
            }

            return live;
        }

        private static Func<int, int> ComposeInt(MethodInfo target, List<(long Seq, Injection Injection)> live)
        {
            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            return (Func<int, int>)composed.Wrapper.CreateDelegate(typeof(Func<int, int>));
        }

        private static void AssertSame(string label, string[] expected, string[] actual)
        {
            string expectedText = string.Join(" ;; ", expected);
            string actualText = string.Join(" ;; ", actual);
            Assert.True(expectedText == actualText, label + "\nexpected: " + expectedText + "\nactual:   " + actualText);
        }

        private static string[] Record(Func<int, string> call)
        {
            PrefixMappingLog.Entries.Clear();
            string outcome;
            try
            {
                outcome = call(4);
            }
            catch (Exception error)
            {
                outcome = "threw:" + error.GetType().Name + ":" + error.Message;
            }

            return new[] { outcome + "|" + string.Join(",", PrefixMappingLog.Entries) };
        }
    }
}
