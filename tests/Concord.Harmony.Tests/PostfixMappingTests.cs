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
    [Collection("HarmonySerial")]
    public sealed class PostfixMappingTests
    {
        private const int Priority = 400;

        [Fact]
        public void Postfix_ReadsResult_OnSingleReturnBody()
        {
            AssertIntParity(
                nameof(PostfixMappingTargets.Single),
                nameof(PostfixMappingHarmonyMethods.ReadResult),
                x => PostfixMappingTargets.Single(x),
                new[] { 4 },
                new[] { "ok:5|body,post:5" });
        }

        [Fact]
        public void Postfix_ReplacesResultByRef_AndCallerSeesIt()
        {
            AssertIntParity(
                nameof(PostfixMappingTargets.Replace),
                nameof(PostfixMappingHarmonyMethods.ReplaceResult),
                x => PostfixMappingTargets.Replace(x),
                new[] { 4 },
                new[] { "ok:10|body,post:5" });
        }

        [Fact]
        public void Postfix_OnThreeReturnBody_RunsOncePerCall()
        {
            AssertIntParity(
                nameof(PostfixMappingTargets.ThreeReturns),
                nameof(PostfixMappingHarmonyMethods.ReadResult),
                x => PostfixMappingTargets.ThreeReturns(x),
                new[] { -5, 0, 7 },
                new[]
                {
                    "ok:-1|body,post:-1",
                    "ok:0|body,post:0",
                    "ok:70|body,post:70",
                });
        }

        [Fact]
        public void Postfix_OnVoidTarget_Runs()
        {
            MethodInfo target = Method(typeof(PostfixMappingTargets), nameof(PostfixMappingTargets.VoidTarget));
            MethodInfo postfix = Method(typeof(PostfixMappingHarmonyMethods), nameof(PostfixMappingHarmonyMethods.NoResult));
            int[] inputs = { 7 };
            string[] expected = { "ok|body:7,post" };
            Action<int> direct = x => PostfixMappingTargets.VoidTarget(x);

            string[] baseline = Record(direct, inputs);
            string[] viaHarmony = UnderHarmony(target, postfix, () => Record(direct, inputs));
            AssertSame("reset vs baseline", baseline, Record(direct, inputs));

            Action<int> wrapper = (Action<int>)Compose(target, postfix, typeof(Action<int>));
            string[] viaConcord = Record(wrapper, inputs);

            AssertSame("harmony vs expected", expected, viaHarmony);
            AssertSame("concord vs harmony", viaHarmony, viaConcord);
        }

        [Fact]
        public void Postfix_AfterOriginalThrew_DoesNotRun()
        {
            AssertIntParity(
                nameof(PostfixMappingTargets.Throwing),
                nameof(PostfixMappingHarmonyMethods.ReadResult),
                x => PostfixMappingTargets.Throwing(x),
                new[] { 4, -1 },
                new[]
                {
                    "ok:5|body,post:5",
                    "threw:InvalidOperationException:boom|body",
                });
        }

        private static void AssertIntParity(string targetName, string postfixName, Func<int, int> direct, int[] inputs, string[] expected)
        {
            MethodInfo target = Method(typeof(PostfixMappingTargets), targetName);
            MethodInfo postfix = Method(typeof(PostfixMappingHarmonyMethods), postfixName);

            string[] baseline = Record(direct, inputs);
            string[] viaHarmony = UnderHarmony(target, postfix, () => Record(direct, inputs));
            AssertSame("reset vs baseline", baseline, Record(direct, inputs));

            Func<int, int> wrapper = (Func<int, int>)Compose(target, postfix, typeof(Func<int, int>));
            string[] viaConcord = Record(wrapper, inputs);

            AssertSame("harmony vs expected", expected, viaHarmony);
            AssertSame("concord vs harmony", viaHarmony, viaConcord);
        }

        private static string[] UnderHarmony(MethodInfo target, MethodInfo postfix, Func<string[]> record)
        {
            string harmonyId = "test.postfixmapping." + target.Name;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix) { priority = Priority });
                return record();
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }
        }

        private static Delegate Compose(MethodInfo target, MethodInfo postfix, Type delegateType)
        {
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Injection injection in PostfixMapping.Map(target, postfix, "test.postfix", Priority))
            {
                Assert.True(injection.ForeignOrigin, "mapped postfix injection must be foreign-origin");
                live.Add((live.Count, injection));
            }

            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            return composed.Wrapper.CreateDelegate(delegateType);
        }

        private static string[] Record(Func<int, int> call, int[] inputs)
        {
            string[] outcomes = new string[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                PostfixMappingLog.Entries.Clear();
                string outcome;
                try
                {
                    outcome = "ok:" + call(inputs[i]);
                }
                catch (Exception exception)
                {
                    outcome = "threw:" + exception.GetType().Name + ":" + exception.Message;
                }

                outcomes[i] = outcome + "|" + string.Join(",", PostfixMappingLog.Entries);
            }

            return outcomes;
        }

        private static string[] Record(Action<int> call, int[] inputs)
        {
            string[] outcomes = new string[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                PostfixMappingLog.Entries.Clear();
                string outcome;
                try
                {
                    call(inputs[i]);
                    outcome = "ok";
                }
                catch (Exception exception)
                {
                    outcome = "threw:" + exception.GetType().Name + ":" + exception.Message;
                }

                outcomes[i] = outcome + "|" + string.Join(",", PostfixMappingLog.Entries);
            }

            return outcomes;
        }

        private static void AssertSame(string label, string[] expected, string[] actual)
        {
            string expectedText = string.Join(" ;; ", expected);
            string actualText = string.Join(" ;; ", actual);
            Assert.True(expectedText == actualText, label + "\nexpected: " + expectedText + "\nactual:   " + actualText);
        }

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }
    }

    public static class PostfixMappingLog
    {
        public static readonly List<string> Entries = new List<string>();
    }

    public static class PostfixMappingTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Single(int x)
        {
            PostfixMappingLog.Entries.Add("body");
            return x + 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Replace(int x)
        {
            PostfixMappingLog.Entries.Add("body");
            return x + 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ThreeReturns(int x)
        {
            PostfixMappingLog.Entries.Add("body");
            if (x < 0)
            {
                return -1;
            }

            if (x == 0)
            {
                return 0;
            }

            return x * 10;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void VoidTarget(int x)
        {
            PostfixMappingLog.Entries.Add("body:" + x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Throwing(int x)
        {
            PostfixMappingLog.Entries.Add("body");
            if (x < 0)
            {
                throw new InvalidOperationException("boom");
            }

            return x + 1;
        }
    }

    public static class PostfixMappingHarmonyMethods
    {
        public static void ReadResult(int __result)
        {
            PostfixMappingLog.Entries.Add("post:" + __result);
        }

        public static void ReplaceResult(ref int __result)
        {
            PostfixMappingLog.Entries.Add("post:" + __result);
            __result *= 2;
        }

        public static void NoResult()
        {
            PostfixMappingLog.Entries.Add("post");
        }
    }
}
