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
    public static class PrefixCancelTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Body(int x)
        {
            FinalizerParityLog.Entries.Add("body");
            if (x < 0)
            {
                throw new InvalidOperationException("boom");
            }

            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CancelFinalizerReads(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CancelFinalizerReplaces(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CancelManufactureThenSwallow(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CancelLoneManufacture(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CancelSwallowThenManufacture(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int NoCancelFinalizerRuns(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ThrowWithoutCancel(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CancelWithPostfix(int x)
        {
            return Body(x);
        }
    }

    public static class PrefixCancelHarmonyMethods
    {
        public static bool SkipWith(ref int __result)
        {
            __result = -100;
            FinalizerParityLog.Entries.Add("skip");
            return false;
        }

        public static bool RunOn(ref int __result)
        {
            FinalizerParityLog.Entries.Add("run");
            return true;
        }

        public static Exception? ReadResult(Exception? __exception, ref int __result)
        {
            FinalizerParityLog.Entries.Add("fin:" + __result + ":" + FinalizerParityLogic.Describe(__exception));
            return __exception;
        }

        public static Exception? ReplaceResult(Exception? __exception, ref int __result)
        {
            FinalizerParityLog.Entries.Add("fin:" + __result);
            __result = 77;
            return null;
        }

        public static Exception? Manufacture(Exception? __exception, ref int __result)
        {
            FinalizerParityLog.Entries.Add("finA:" + FinalizerParityLogic.Describe(__exception));
            return __exception ?? new ArgumentException("manufactured");
        }

        public static void Bump(ref int __result)
        {
            FinalizerParityLog.Entries.Add("postfix");
            __result += 1;
        }

        public static Exception? Swallow(Exception? __exception, ref int __result)
        {
            FinalizerParityLog.Entries.Add("finB:" + FinalizerParityLogic.Describe(__exception));
            __result = 5;
            return null;
        }
    }

    [Collection("HarmonySerial")]
    public sealed class PrefixCancelFinalizerParityTests
    {
        private const int High = 600;

        private const int Low = 200;

        private sealed class Patch(string owner, int priority, MethodInfo method)
        {
            public string Owner = owner;

            public int Priority = priority;

            public MethodInfo Method = method;
        }

        [Fact]
        public void PrefixCancels_FinalizerStillRunsAndReadsSkippedResult()
        {
            AssertParity(
                nameof(PrefixCancelTargets.CancelFinalizerReads),
                x => PrefixCancelTargets.CancelFinalizerReads(x),
                new[] { 4 },
                new[] { "ok:-100|skip,fin:-100:null" },
                Prefix("SkipWith", "test.prefix"),
                Fin("ReadResult", "test.fin", High));
        }

        [Fact]
        public void PrefixCancels_FinalizerReplacesTheResult()
        {
            AssertParity(
                nameof(PrefixCancelTargets.CancelFinalizerReplaces),
                x => PrefixCancelTargets.CancelFinalizerReplaces(x),
                new[] { 4 },
                new[] { "ok:77|skip,fin:-100" },
                Prefix("SkipWith", "test.prefix"),
                Fin("ReplaceResult", "test.fin", High));
        }

        [Fact]
        public void PrefixCancels_FirstFinalizerManufactures_SecondSwallows()
        {
            AssertParity(
                nameof(PrefixCancelTargets.CancelManufactureThenSwallow),
                x => PrefixCancelTargets.CancelManufactureThenSwallow(x),
                new[] { 4 },
                new[] { "ok:5|skip,finA:null,finB:ArgumentException:manufactured" },
                Prefix("SkipWith", "test.prefix"),
                Fin("Manufacture", "test.a", High),
                Fin("Swallow", "test.b", Low));
        }

        [Fact]
        public void PrefixCancels_LoneFinalizerManufactures_ExceptionReachesCaller()
        {
            AssertParity(
                nameof(PrefixCancelTargets.CancelLoneManufacture),
                x => PrefixCancelTargets.CancelLoneManufacture(x),
                new[] { 4 },
                new[] { "threw:ArgumentException:manufactured|skip,finA:null" },
                Prefix("SkipWith", "test.prefix"),
                Fin("Manufacture", "test.fin", High));
        }

        [Fact]
        public void PrefixCancels_LastFinalizerManufactures_ExceptionReachesCaller()
        {
            AssertParity(
                nameof(PrefixCancelTargets.CancelSwallowThenManufacture),
                x => PrefixCancelTargets.CancelSwallowThenManufacture(x),
                new[] { 4 },
                new[] { "threw:ArgumentException:manufactured|skip,finB:null,finA:null" },
                Prefix("SkipWith", "test.prefix"),
                Fin("Swallow", "test.a", High),
                Fin("Manufacture", "test.b", Low));
        }

        [Fact]
        public void PrefixRunsOriginal_FinalizerStillRunsOnNormalReturn()
        {
            AssertParity(
                nameof(PrefixCancelTargets.NoCancelFinalizerRuns),
                x => PrefixCancelTargets.NoCancelFinalizerRuns(x),
                new[] { 4 },
                new[] { "ok:8|run,body,fin:8:null" },
                Prefix("RunOn", "test.prefix"),
                Fin("ReadResult", "test.fin", High));
        }

        [Fact]
        public void OriginalThrowsWithoutCancel_FinalizerRuns()
        {
            AssertParity(
                nameof(PrefixCancelTargets.ThrowWithoutCancel),
                x => PrefixCancelTargets.ThrowWithoutCancel(x),
                new[] { -1 },
                new[] { "threw:InvalidOperationException:boom|run,body,fin:0:InvalidOperationException:boom" },
                Prefix("RunOn", "test.prefix"),
                Fin("ReadResult", "test.fin", High));
        }

        [Fact]
        public void PrefixCancels_MappedPostfixStillRunsBeforeMappedFinalizer()
        {
            AssertParity(
                nameof(PrefixCancelTargets.CancelWithPostfix),
                x => PrefixCancelTargets.CancelWithPostfix(x),
                new[] { 4 },
                new[] { "ok:-99|skip,postfix,fin:-99:null" },
                Prefix("SkipWith", "test.prefix"),
                Fin("Bump", "test.postfix", High),
                new[] { Fin("ReadResult", "test.fin", Low) });
        }

        private static Patch Prefix(string name, string owner)
        {
            return new Patch(owner, HarmonyLib.Priority.Normal, Method(typeof(PrefixCancelHarmonyMethods), name));
        }

        private static Patch Fin(string name, string owner, int priority)
        {
            return new Patch(owner, priority, Method(typeof(PrefixCancelHarmonyMethods), name));
        }

        private static void AssertParity(string targetName, Func<int, int> call, int[] inputs, string[] expected, Patch prefix, params Patch[] finalizers)
        {
            AssertParity(targetName, call, inputs, expected, prefix, null, finalizers);
        }

        private static void AssertParity(string targetName, Func<int, int> call, int[] inputs, string[] expected, Patch prefix, Patch? postfix, Patch[] finalizers)
        {
            MethodInfo target = Method(typeof(PrefixCancelTargets), targetName);
            string[] baseline = Record(call, inputs);

            string harmonyId = "test.prefixcancelfin." + targetName;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            string[] viaHarmony;
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix.Method) { priority = prefix.Priority });
                if (postfix != null)
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(postfix.Method) { priority = postfix.Priority });
                }

                foreach (Patch finalizer in finalizers)
                {
                    harmony.Patch(target, finalizer: new HarmonyMethod(finalizer.Method) { priority = finalizer.Priority });
                }

                viaHarmony = Record(call, inputs);
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }

            string[] afterReset = Record(call, inputs);
            AssertSame("reset vs baseline", baseline, afterReset);

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            live.Add((live.Count, PrefixMapping.Map(target, prefix.Method, prefix.Owner)));
            if (postfix != null)
            {
                foreach (Injection injection in PostfixMapping.Map(target, postfix.Method, postfix.Owner, postfix.Priority))
                {
                    live.Add((live.Count, injection));
                }
            }

            foreach (Patch finalizer in finalizers)
            {
                foreach (Injection injection in FinalizerParticipant.Map(target, finalizer.Method, finalizer.Owner, finalizer.Priority))
                {
                    live.Add((live.Count, injection));
                }
            }

            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            Func<int, int> wrapper = (Func<int, int>)composed.Wrapper.CreateDelegate(typeof(Func<int, int>));
            string[] viaConcord = Record(wrapper, inputs);

            AssertSame("harmony vs expected", expected, viaHarmony);
            AssertSame("concord vs harmony", viaHarmony, viaConcord);
        }

        private static void AssertSame(string label, string[] expected, string[] actual)
        {
            string expectedText = string.Join(" ;; ", expected);
            string actualText = string.Join(" ;; ", actual);
            Assert.True(expectedText == actualText, label + "\nexpected: " + expectedText + "\nactual:   " + actualText);
        }

        private static string[] Record(Func<int, int> call, int[] inputs)
        {
            string[] outcomes = new string[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                FinalizerParityLog.Entries.Clear();
                string outcome;
                try
                {
                    outcome = "ok:" + call(inputs[i]);
                }
                catch (Exception error)
                {
                    outcome = "threw:" + FinalizerParityLogic.Describe(error);
                }

                outcomes[i] = outcome + "|" + string.Join(",", FinalizerParityLog.Entries);
            }

            return outcomes;
        }

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }
    }
}
