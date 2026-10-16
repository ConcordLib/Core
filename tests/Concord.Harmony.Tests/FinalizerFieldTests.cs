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
    public sealed class FinalizerFieldHolder
    {
        private bool contentsKnown;

        private int tally = 0;

        public bool ContentsKnown
        {
            get { return contentsKnown; }
            set { contentsKnown = value; }
        }

        public int Tally
        {
            get { return tally; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Open(int amount)
        {
            if (amount < 0)
            {
                throw new InvalidOperationException("boom");
            }

            return amount * 2;
        }
    }

    public static class FinalizerFieldMods
    {
        public static List<string> Entries = new List<string>();

        public static void ReadsField(Exception __exception, bool ___contentsKnown)
        {
            Entries.Add("read:" + ___contentsKnown + ":" + (__exception == null ? "null" : __exception.Message));
        }

        public static void WritesField(Exception __exception, ref int ___tally)
        {
            ___tally = ___tally + 1;
            Entries.Add("write:" + ___tally);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class FinalizerFieldTests
    {
        [Fact]
        public void FinalizerReadingAPrivateInstanceField_MatchesHarmony()
        {
            AssertParity(nameof(FinalizerFieldMods.ReadsField), new[] { 4, -1 });
        }

        [Fact]
        public void FinalizerWritingAPrivateInstanceFieldByRef_MatchesHarmony()
        {
            AssertParity(nameof(FinalizerFieldMods.WritesField), new[] { 4, -1 });
        }

        [Fact]
        public void FinalizerNamingAFieldTheTargetDoesNotHave_IsRefused()
        {
            MethodInfo target = typeof(FinalizerFieldHolder).GetMethod(nameof(FinalizerFieldHolder.Open))!;
            MethodInfo finalizer = typeof(MissingFieldMod).GetMethod(nameof(MissingFieldMod.Finalizer))!;

            Assert.Contains("has no field named", HarmonyFinalizerShape.Validate(target, finalizer));
        }

        private static void AssertParity(string modName, int[] inputs)
        {
            MethodInfo target = typeof(FinalizerFieldHolder).GetMethod(nameof(FinalizerFieldHolder.Open))!;
            MethodInfo finalizer = typeof(FinalizerFieldMods).GetMethod(modName)!;

            string harmonyId = "test.finfield." + modName;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            string[] viaHarmony;
            try
            {
                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
                viaHarmony = Record(inputs, (holder, amount) => holder.Open(amount));
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Injection injection in FinalizerParticipant.Map(target, finalizer, "test.finfield", 400))
            {
                live.Add((live.Count, injection));
            }

            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);
            Func<FinalizerFieldHolder, int, int> wrapper =
                (Func<FinalizerFieldHolder, int, int>)composed.Wrapper.CreateDelegate(typeof(Func<FinalizerFieldHolder, int, int>));

            string[] viaConcord = Record(inputs, (holder, amount) => wrapper(holder, amount));

            Assert.Equal(string.Join(" ;; ", viaHarmony), string.Join(" ;; ", viaConcord));
        }

        private static string[] Record(int[] inputs, Func<FinalizerFieldHolder, int, int> call)
        {
            string[] outcomes = new string[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                FinalizerFieldMods.Entries.Clear();
                FinalizerFieldHolder holder = new FinalizerFieldHolder { ContentsKnown = true };
                string outcome;
                try
                {
                    outcome = "ok:" + call(holder, inputs[i]);
                }
                catch (Exception error)
                {
                    outcome = "threw:" + error.GetType().Name + ":" + error.Message;
                }

                outcomes[i] = outcome + "|" + string.Join(",", FinalizerFieldMods.Entries) + "|tally:" + holder.Tally;
            }

            return outcomes;
        }
    }

    public static class MissingFieldMod
    {
        public static void Finalizer(Exception __exception, int ___nope)
        {
            GC.KeepAlive(___nope);
            GC.KeepAlive(__exception);
        }
    }
}
