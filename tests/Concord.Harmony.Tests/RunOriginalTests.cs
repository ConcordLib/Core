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
    public sealed class RunOriginalTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Double(int amount)
        {
            RunOriginalMods.Entries.Add("body");
            return amount * 2;
        }
    }

    public static class RunOriginalMods
    {
        public static List<string> Entries = new List<string>();

        public static bool Blocks = false;

        public static bool Gate()
        {
            RunOriginalMods.Entries.Add("pre");
            return !Blocks;
        }

        public static void Reads(bool __runOriginal)
        {
            RunOriginalMods.Entries.Add("post:" + __runOriginal);
        }

        public static void TakesItByRef(ref bool __runOriginal)
        {
            __runOriginal = false;
        }

        public static void TakesItAsInt(int __runOriginal)
        {
            GC.KeepAlive(__runOriginal);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class RunOriginalTests
    {
        [Fact]
        public void PostfixReadingRunOriginal_MatchesHarmony()
        {
            Assert.Equal(ViaHarmony(false), ViaConcord(false));
        }

        [Fact]
        public void PostfixReadingRunOriginalAfterACancellingPrefix_MatchesHarmony()
        {
            Assert.Equal(ViaHarmony(true), ViaConcord(true));
        }

        [Fact]
        public void PostfixTakingRunOriginalByRef_IsRefused()
        {
            Assert.Contains(
                "takes __runOriginal by reference",
                Plan(nameof(RunOriginalMods.TakesItByRef)));
        }

        [Fact]
        public void PostfixTakingRunOriginalAsTheWrongType_IsRefused()
        {
            Assert.Contains("it must be bool", Plan(nameof(RunOriginalMods.TakesItAsInt)));
        }

        private static string? Plan(string modName)
        {
            MethodBase target = Target();
            MethodInfo patch = typeof(RunOriginalMods).GetMethod(modName)!;
            return BoundParameters.PlanCopy(
                "Postfix",
                modName,
                target,
                patch,
                typeof(int),
                HarmonyPatchRole.Postfix,
                true,
                out CopyBinding _);
        }

        private static MethodBase Target()
        {
            return typeof(RunOriginalTarget).GetMethod(nameof(RunOriginalTarget.Double))!;
        }

        private static string ViaHarmony(bool blocks)
        {
            MethodBase target = Target();
            string harmonyId = "test.runoriginal." + blocks;
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(RunOriginalMods).GetMethod(nameof(RunOriginalMods.Gate))!),
                    postfix: new HarmonyMethod(typeof(RunOriginalMods).GetMethod(nameof(RunOriginalMods.Reads))!));
                return Record(blocks, (holder, amount) => holder.Double(amount));
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }
        }

        private static string ViaConcord(bool blocks)
        {
            MethodBase target = Target();
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, PrefixMapping.Map(target, typeof(RunOriginalMods).GetMethod(nameof(RunOriginalMods.Gate))!, "test.runoriginal")),
            };

            foreach (Injection injection in PostfixMapping.Map(target, typeof(RunOriginalMods).GetMethod(nameof(RunOriginalMods.Reads))!, "test.runoriginal", 400))
            {
                live.Add((live.Count, injection));
            }

            ComposeResult composed = WrapperComposer.Compose(target, InjectionOrderer.OrderForComposition(live));
            Assert.Empty(composed.Rejected);
            Func<RunOriginalTarget, int, int> wrapper =
                (Func<RunOriginalTarget, int, int>)composed.Wrapper.CreateDelegate(typeof(Func<RunOriginalTarget, int, int>));

            return Record(blocks, (holder, amount) => wrapper(holder, amount));
        }

        private static string Record(bool blocks, Func<RunOriginalTarget, int, int> call)
        {
            RunOriginalMods.Entries.Clear();
            RunOriginalMods.Blocks = blocks;
            try
            {
                int produced = call(new RunOriginalTarget(), 21);
                return produced + "|" + string.Join(",", RunOriginalMods.Entries);
            }
            finally
            {
                RunOriginalMods.Blocks = false;
            }
        }
    }
}
