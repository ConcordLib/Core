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
    public static class BoundFinalizerLog
    {
        public static List<string> Entries = new List<string>();
    }

    public sealed class BoundFinalizerTarget
    {
        public int Value;

        public BoundFinalizerTarget(int value)
        {
            Value = value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Body(int x)
        {
            BoundFinalizerLog.Entries.Add("body");
            if (x < 0)
            {
                throw new InvalidOperationException("boom");
            }

            return Value + x;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int SeesInstance(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int SwallowsOnInstance(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int NormalAndThrow(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int TakesArgument(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int MixedFinalizers(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int StaticBody(int x)
        {
            BoundFinalizerLog.Entries.Add("static-body");
            return x;
        }
    }

    public struct BoundFinalizerStruct
    {
        public int Value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read(int x)
        {
            return Value + x;
        }
    }

    public static class BoundFinalizerPatches
    {
        public static Exception? ReadsInstance(Exception? __exception, BoundFinalizerTarget __instance, ref int __result)
        {
            BoundFinalizerLog.Entries.Add("fin:" + __instance.Value + ":" + Describe(__exception) + ":result=" + __result);
            return __exception;
        }

        public static Exception? SwallowsUsingInstance(Exception? __exception, BoundFinalizerTarget __instance, ref int __result)
        {
            BoundFinalizerLog.Entries.Add("fin:" + __instance.Value + ":" + Describe(__exception));
            if (__exception == null)
            {
                return null;
            }

            __result = __instance.Value * 10;
            return null;
        }

        public static Exception? ReadsInstanceNoResult(Exception? __exception, BoundFinalizerTarget __instance)
        {
            BoundFinalizerLog.Entries.Add("fin:" + __instance.Value + ":" + Describe(__exception));
            return __exception;
        }

        public static Exception? WrapsUsingInstance(Exception? __exception, BoundFinalizerTarget __instance)
        {
            BoundFinalizerLog.Entries.Add("finA:" + __instance.Value + ":" + Describe(__exception));
            return __exception == null ? null : new ArgumentException("wrapped-" + __instance.Value);
        }

        public static Exception? ObservesWithoutInstance(Exception? __exception)
        {
            BoundFinalizerLog.Entries.Add("finB:" + Describe(__exception));
            return __exception;
        }

        public static Exception? TakesTargetArgument(Exception? __exception, int x)
        {
            BoundFinalizerLog.Entries.Add("fin:x=" + x);
            return __exception;
        }

        public static Exception? TakesTargetArgumentByRef(Exception? __exception, ref int x)
        {
            x = 0;
            return __exception;
        }

        public static Exception? TakesTargetArgumentAsLong(Exception? __exception, long x)
        {
            BoundFinalizerLog.Entries.Add("fin:x=" + x);
            return __exception;
        }

        public static Exception? TakesAnArgumentThatIsNotThere(Exception? __exception, int nope)
        {
            BoundFinalizerLog.Entries.Add("fin:nope=" + nope);
            return __exception;
        }

        public static Exception? ByRefException(ref Exception? __exception)
        {
            return __exception;
        }

        public static Exception? ByRefState(Exception? __exception, ref object? __state)
        {
            return __exception;
        }

        public static Exception? TakesState(Exception? __exception, object? __state)
        {
            return __exception;
        }

        public static Exception? TakesStaticInstance(Exception? __exception, BoundFinalizerTarget __instance)
        {
            return __exception;
        }

        public static Exception? TakesStructInstance(Exception? __exception, BoundFinalizerStruct __instance)
        {
            return __exception;
        }

        public static Exception? TakesWrongInstanceType(Exception? __exception, string __instance)
        {
            return __exception;
        }

        public static string Describe(Exception? error)
        {
            return error == null ? "null" : error.GetType().Name + ":" + error.Message;
        }
    }

    [Collection("HarmonySerial")]
    public sealed class BoundFinalizerTests
    {
        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        [Fact]
        public void Finalizer_ReadingInstance_MatchesHarmony()
        {
            AssertParity(
                nameof(BoundFinalizerTarget.SeesInstance),
                (instance, x) => instance.SeesInstance(x),
                nameof(BoundFinalizerPatches.ReadsInstance),
                new[] { 4, -1 });
        }

        [Fact]
        public void Finalizer_ReadingInstance_WithoutResult_MatchesHarmony()
        {
            AssertParity(
                nameof(BoundFinalizerTarget.NormalAndThrow),
                (instance, x) => instance.NormalAndThrow(x),
                nameof(BoundFinalizerPatches.ReadsInstanceNoResult),
                new[] { 4, -1 });
        }

        [Fact]
        public void Finalizer_ReturningNullOnInstancePath_SwallowsLikeHarmony()
        {
            AssertParity(
                nameof(BoundFinalizerTarget.SwallowsOnInstance),
                (instance, x) => instance.SwallowsOnInstance(x),
                nameof(BoundFinalizerPatches.SwallowsUsingInstance),
                new[] { 4, -1 });
        }

        [Fact]
        public void MixedFinalizers_InstanceBoundThenPlain_ChainLikeHarmony()
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), nameof(BoundFinalizerTarget.MixedFinalizers));
            MethodInfo wraps = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.WrapsUsingInstance));
            MethodInfo observes = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.ObservesWithoutInstance));
            int[] inputs = { 4, -1 };

            Func<BoundFinalizerTarget, int, int> direct = (instance, x) => instance.MixedFinalizers(x);
            string[] baseline = Record(direct, inputs);

            string harmonyId = "test.boundfin.mixed";
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            string[] viaHarmony;
            try
            {
                harmony.Patch(target, finalizer: new HarmonyMethod(wraps) { priority = 600 });
                harmony.Patch(target, finalizer: new HarmonyMethod(observes) { priority = 200 });
                viaHarmony = Record(direct, inputs);
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }

            AssertSame("reset vs baseline", baseline, Record(direct, inputs));

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Injection injection in FinalizerParticipant.Map(target, wraps, "test.a", 600))
            {
                live.Add((live.Count, injection));
            }

            foreach (Injection injection in FinalizerParticipant.Map(target, observes, "test.b", 200))
            {
                live.Add((live.Count, injection));
            }

            ComposeResult composed = WrapperComposer.Compose(target, InjectionOrderer.OrderForComposition(live));
            Assert.Empty(composed.Rejected);
            Func<BoundFinalizerTarget, int, int> wrapper =
                (Func<BoundFinalizerTarget, int, int>)composed.Wrapper.CreateDelegate(typeof(Func<BoundFinalizerTarget, int, int>));

            AssertSame("concord vs harmony", viaHarmony, Record(wrapper, inputs));
            Assert.Contains("finB:ArgumentException:wrapped-5", string.Join(" ;; ", viaHarmony));
        }

        [Fact]
        public void Finalizer_TakingTargetArgument_MatchesHarmony()
        {
            AssertParity(
                nameof(BoundFinalizerTarget.TakesArgument),
                (instance, x) => instance.TakesArgument(x),
                nameof(BoundFinalizerPatches.TakesTargetArgument),
                new[] { 4, -1 });
        }

        [Fact]
        public void Finalizer_TakingTargetArgumentByRef_IsRefused()
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), nameof(BoundFinalizerTarget.TakesArgument));
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.TakesTargetArgumentByRef));

            Assert.Contains("by reference", FinalizerParticipant.Validate(target, finalizer));
        }

        [Fact]
        public void Finalizer_TakingTargetArgumentAtTheWrongType_IsRefused()
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), nameof(BoundFinalizerTarget.TakesArgument));
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.TakesTargetArgumentAsLong));

            Assert.Contains("only an exact type match is bound", FinalizerParticipant.Validate(target, finalizer));
        }

        [Fact]
        public void Finalizer_NamingAnArgumentTheTargetDoesNotHave_IsRefused()
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), nameof(BoundFinalizerTarget.TakesArgument));
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.TakesAnArgumentThatIsNotThere));

            Assert.Contains("declares no parameter of that name", FinalizerParticipant.Validate(target, finalizer));
        }

        [Fact]
        public void Finalizer_TakingByRefException_IsStillRefused()
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), nameof(BoundFinalizerTarget.SeesInstance));
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.ByRefException));

            string? reason = FinalizerParticipant.Validate(target, finalizer);

            Assert.NotNull(reason);
            Assert.Contains("__exception by reference", reason);
        }

        [Fact]
        public void Finalizer_TakingState_IsAccepted()
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), nameof(BoundFinalizerTarget.SeesInstance));
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.TakesState));

            Assert.Null(FinalizerParticipant.Validate(target, finalizer));
        }

        [Fact]
        public void Finalizer_TakingStateByReference_IsRefused()
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), nameof(BoundFinalizerTarget.SeesInstance));
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.ByRefState));

            string? reason = FinalizerParticipant.Validate(target, finalizer);

            Assert.NotNull(reason);
            Assert.Contains("__state by reference", reason);
        }

        [Fact]
        public void Finalizer_TakingInstanceOnStaticTarget_IsRefused()
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), nameof(BoundFinalizerTarget.StaticBody));
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.TakesStaticInstance));

            string? reason = FinalizerParticipant.Validate(target, finalizer);

            Assert.NotNull(reason);
            Assert.Contains("is static, so there is no receiver", reason);
        }

        [Fact]
        public void Finalizer_TakingInstanceOnStructTarget_IsRefused()
        {
            MethodInfo target = Method(typeof(BoundFinalizerStruct), nameof(BoundFinalizerStruct.Read));
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.TakesStructInstance));

            string? reason = FinalizerParticipant.Validate(target, finalizer);

            Assert.NotNull(reason);
            Assert.Contains("value-type receiver", reason);
        }

        [Fact]
        public void Finalizer_DeclaringInstanceAsUnrelatedType_IsRefused()
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), nameof(BoundFinalizerTarget.SeesInstance));
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), nameof(BoundFinalizerPatches.TakesWrongInstanceType));

            string? reason = FinalizerParticipant.Validate(target, finalizer);

            Assert.NotNull(reason);
            Assert.Contains("cannot receive target instance type", reason);
        }

        private static void AssertParity(string targetName, Func<BoundFinalizerTarget, int, int> direct, string finalizerName, int[] inputs)
        {
            MethodInfo target = Method(typeof(BoundFinalizerTarget), targetName);
            MethodInfo finalizer = Method(typeof(BoundFinalizerPatches), finalizerName);
            Assert.Null(FinalizerParticipant.Validate(target, finalizer));

            string[] baseline = Record(direct, inputs);

            string harmonyId = "test.boundfin." + targetName;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            string[] viaHarmony;
            try
            {
                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
                viaHarmony = Record(direct, inputs);
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }

            string[] afterReset = Record(direct, inputs);
            AssertSame("reset vs baseline", baseline, afterReset);

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Injection injection in FinalizerParticipant.Map(target, finalizer, "test.boundfin", 0))
            {
                live.Add((live.Count, injection));
            }

            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            Func<BoundFinalizerTarget, int, int> wrapper =
                (Func<BoundFinalizerTarget, int, int>)composed.Wrapper.CreateDelegate(typeof(Func<BoundFinalizerTarget, int, int>));

            string[] viaConcord = Record(wrapper, inputs);
            AssertSame("concord vs harmony", viaHarmony, viaConcord);
            Assert.NotEqual(string.Join(" ;; ", baseline), string.Join(" ;; ", viaHarmony));
        }

        private static string[] Record(Func<BoundFinalizerTarget, int, int> call, int[] inputs)
        {
            string[] outcomes = new string[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                BoundFinalizerLog.Entries.Clear();
                string outcome;
                try
                {
                    outcome = "ok:" + call(new BoundFinalizerTarget(5), inputs[i]);
                }
                catch (TargetInvocationException wrapped)
                {
                    outcome = "threw:" + BoundFinalizerPatches.Describe(wrapped.InnerException);
                }
                catch (Exception error)
                {
                    outcome = "threw:" + BoundFinalizerPatches.Describe(error);
                }

                outcomes[i] = outcome + "|" + string.Join(",", BoundFinalizerLog.Entries);
            }

            return outcomes;
        }

        private static void AssertSame(string label, string[] expected, string[] actual)
        {
            string expectedText = string.Join(" ;; ", expected);
            string actualText = string.Join(" ;; ", actual);
            Assert.True(expectedText == actualText, label + "\nexpected: " + expectedText + "\nactual:   " + actualText);
        }
    }
}
