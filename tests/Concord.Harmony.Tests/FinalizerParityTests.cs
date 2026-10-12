using System;
using System.Collections.Generic;
using System.Reflection;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    [Collection("HarmonySerial")]
    public sealed class FinalizerParityTests
    {
        private const int High = 600;
        private const int Low = 200;

        private sealed class Finalizer
        {
            public string Owner;
            public int Priority;
            public MethodInfo Harmony;

            public Finalizer(string owner, int priority, MethodInfo harmony)
            {
                Owner = owner;
                Priority = priority;
                Harmony = harmony;
            }
        }

        [Fact]
        public void Finalizer_ReadsExceptionAndLetsItPropagate()
        {
            AssertParity(
                nameof(FinalizerParityTargets.Propagate),
                x => FinalizerParityTargets.Propagate(x),
                new[] { -1 },
                new[] { "threw:InvalidOperationException:boom|body,fin:InvalidOperationException:boom" },
                Fin("Propagate", "test.fin", High));
        }

        [Fact]
        public void Finalizer_SwallowsByReturningNull()
        {
            AssertParity(
                nameof(FinalizerParityTargets.Swallow),
                x => FinalizerParityTargets.Swallow(x),
                new[] { -1 },
                new[] { "ok:0|body,fin:InvalidOperationException:boom" },
                Fin("Swallow", "test.fin", High));
        }

        [Fact]
        public void Finalizer_ReplacesResultAfterException()
        {
            AssertParity(
                nameof(FinalizerParityTargets.Replace),
                x => FinalizerParityTargets.Replace(x),
                new[] { -1 },
                new[] { "ok:99|body,fin:InvalidOperationException:boom" },
                Fin("Replace", "test.fin", High));
        }

        [Fact]
        public void Finalizer_OnNormalReturn_SeesNullException()
        {
            AssertParity(
                nameof(FinalizerParityTargets.Normal),
                x => FinalizerParityTargets.Normal(x),
                new[] { 4 },
                new[] { "ok:8|body,fin:null" },
                Fin("Propagate", "test.fin", High));
        }

        [Fact]
        public void TwoFinalizers_DifferentOwners_RunInPriorityOrder()
        {
            AssertParity(
                nameof(FinalizerParityTargets.TwoOwners),
                x => FinalizerParityTargets.TwoOwners(x),
                new[] { -1 },
                new[] { "threw:InvalidOperationException:boom|body,finA:InvalidOperationException:boom,finB:InvalidOperationException:boom" },
                Fin("OwnerA", "test.a", High),
                Fin("OwnerB", "test.b", Low));
        }

        [Fact]
        public void Finalizer_PlusPostfix_PostfixRunsFirstAndOnlyOnNormalReturn()
        {
            AssertParity(
                nameof(FinalizerParityTargets.WithPostfix),
                x => FinalizerParityTargets.WithPostfix(x),
                new[] { 4, -1 },
                new[]
                {
                    "ok:9|body,postfix,fin:null",
                    "threw:InvalidOperationException:boom|body,fin:InvalidOperationException:boom",
                },
                new[] { Fin("Propagate", "test.fin", Low) },
                postfix: true);
        }

        [Fact]
        public void Finalizer_WritesResultWithoutSwallowing_NextFinalizerSeesTheWriteAndExceptionStillEscapes()
        {
            AssertParity(
                nameof(FinalizerParityTargets.ResultWithoutSwallow),
                x => FinalizerParityTargets.ResultWithoutSwallow(x),
                new[] { -1 },
                new[] { "threw:InvalidOperationException:boom|body,finA:InvalidOperationException:boom,finB:result=42" },
                Fin("WriteResultKeepThrow", "test.a", High),
                Fin("ReadResultKeepThrow", "test.b", Low));
        }

        [Fact]
        public void TwoFinalizers_FirstReplacesException_SecondSeesTheReplacement()
        {
            AssertParity(
                nameof(FinalizerParityTargets.Chain),
                x => FinalizerParityTargets.Chain(x),
                new[] { -1 },
                new[] { "threw:ArgumentException:wrapped|body,finA:InvalidOperationException:boom,finB:ArgumentException:wrapped" },
                Fin("ChainWrap", "test.a", High),
                Fin("ChainObserve", "test.b", Low));
        }

        [Fact]
        public void Finalizer_ManufacturesExceptionOnNormalReturn_RunsOnceNotTwice()
        {
            AssertParity(
                nameof(FinalizerParityTargets.ManufactureOnNormalReturn),
                x => FinalizerParityTargets.ManufactureOnNormalReturn(x),
                new[] { 4, -1 },
                new[]
                {
                    "threw:ArgumentException:manufactured|body,fin:null",
                    "threw:InvalidOperationException:boom|body,fin:InvalidOperationException:boom",
                },
                Fin("ManufactureOnNormalReturn", "test.fin", High));
        }

        [Fact]
        public void TwoFinalizers_FirstManufacturesOnNormalReturn_NeitherRunsASecondTime()
        {
            AssertParity(
                nameof(FinalizerParityTargets.TwoManufactureOnNormalReturn),
                x => FinalizerParityTargets.TwoManufactureOnNormalReturn(x),
                new[] { 4 },
                new[] { "threw:ArgumentException:manufactured|body,fin:null,finB:ArgumentException:manufactured" },
                Fin("ManufactureOnNormalReturn", "test.a", High),
                Fin("ObserveAfterManufacture", "test.b", Low));
        }

        [Fact]
        public void ThrowingFinalizer_SwallowedOnThrowPath_EscapesOnReturnPath()
        {
            AssertParity(
                nameof(FinalizerParityTargets.ThrowingFinalizer),
                x => FinalizerParityTargets.ThrowingFinalizer(x),
                new[] { 4, -1 },
                new[]
                {
                    "threw:NotSupportedException:fin-boom|body,fin:null,fin:NotSupportedException:fin-boom",
                    "threw:InvalidOperationException:boom|body,fin:InvalidOperationException:boom",
                },
                Fin("Boom", "test.fin", High));
        }

        [Fact]
        public void ByRefReturningTarget_IsRejectedByValidate_WithOrWithoutResult()
        {
            MethodInfo target = Method(typeof(FinalizerParityTargets), nameof(FinalizerParityTargets.ByRefReturn));
            MethodInfo withResult = Method(typeof(FinalizerParityHarmonyMethods), nameof(FinalizerParityHarmonyMethods.Propagate));
            MethodInfo withoutResult = Method(typeof(FinalizerParityHarmonyMethods), nameof(FinalizerParityHarmonyMethods.NoResult));

            Assert.Contains("by-ref", FinalizerParticipant.Validate(target, withResult));
            Assert.Contains("by-ref", FinalizerParticipant.Validate(target, withoutResult));
        }

        private static Finalizer Fin(string name, string owner, int priority)
        {
            return new Finalizer(owner, priority, Method(typeof(FinalizerParityHarmonyMethods), name));
        }

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        private static void AssertParity(string targetName, Func<int, int> call, int[] inputs, string[] expected, params Finalizer[] finalizers)
        {
            AssertParity(targetName, call, inputs, expected, finalizers, postfix: false);
        }

        private static void AssertParity(string targetName, Func<int, int> call, int[] inputs, string[] expected, Finalizer[] finalizers, bool postfix)
        {
            MethodInfo target = Method(typeof(FinalizerParityTargets), targetName);
            string[] baseline = Record(call, inputs);

            string harmonyId = "test.finparity." + targetName;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            string[] viaHarmony;
            try
            {
                foreach (Finalizer finalizer in finalizers)
                {
                    harmony.Patch(target, finalizer: new HarmonyMethod(finalizer.Harmony) { priority = finalizer.Priority });
                }

                if (postfix)
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(Method(typeof(FinalizerParityHarmonyMethods), nameof(FinalizerParityHarmonyMethods.Postfix))) { priority = High });
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
            if (postfix)
            {
                MethodInfo harmonyPostfix = Method(typeof(FinalizerParityHarmonyMethods), nameof(FinalizerParityHarmonyMethods.Postfix));
                foreach (Injection injection in PostfixMapping.Map(target, harmonyPostfix, "test.postfix", High))
                {
                    live.Add((live.Count, injection));
                }
            }

            foreach (Finalizer finalizer in finalizers)
            {
                foreach (Injection injection in FinalizerParticipant.Map(target, finalizer.Harmony, finalizer.Owner, finalizer.Priority))
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
    }
}
