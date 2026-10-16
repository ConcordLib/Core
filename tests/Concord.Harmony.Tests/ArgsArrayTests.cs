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
    public sealed class ArgsArrayTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Measure(int amount, string label)
        {
            return amount + label.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Produce(int amount, out int doubled)
        {
            doubled = amount * 2;
            return amount;
        }
    }

    public static class ArgsArrayMods
    {
        public static List<string> Entries = new List<string>();

        public static void Reads(object[] __args)
        {
            Entries.Add("read:" + __args[0] + ":" + __args[1]);
        }

        public static void Writes(object[] __args)
        {
            __args[0] = 99;
        }

        public static void TakesItByRef(ref object[] __args)
        {
            GC.KeepAlive(__args);
        }

        public static void TakesItAsAList(List<object> __args)
        {
            GC.KeepAlive(__args);
        }

        public static void ReadsOnATargetWithAnOutArg(object[] __args)
        {
            Entries.Add("read:" + __args[0]);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class ArgsArrayTests
    {
        [Fact]
        public void PostfixReadingArgs_MatchesHarmony()
        {
            Assert.Equal(ViaHarmony(), ViaConcord());
        }

        [Fact]
        public void PostfixWritingIntoArgs_IsRefused()
        {
            Assert.Contains(
                "somewhere other than a plain element load",
                Plan(nameof(ArgsArrayMods.Writes), nameof(ArgsArrayTarget.Measure)));
        }

        [Fact]
        public void PostfixTakingArgsByRef_IsRefused()
        {
            Assert.Contains(
                "must be object[] passed by value",
                Plan(nameof(ArgsArrayMods.TakesItByRef), nameof(ArgsArrayTarget.Measure)));
        }

        [Fact]
        public void PostfixTakingArgsAsTheWrongType_IsRefused()
        {
            Assert.Contains(
                "must be object[] passed by value",
                Plan(nameof(ArgsArrayMods.TakesItAsAList), nameof(ArgsArrayTarget.Measure)));
        }

        [Fact]
        public void PostfixTakingArgsOnATargetWithAnOutArgument_IsRefused()
        {
            Assert.Contains(
                "declares the out parameter 'doubled'",
                Plan(nameof(ArgsArrayMods.ReadsOnATargetWithAnOutArg), nameof(ArgsArrayTarget.Produce)));
        }

        private static string? Plan(string modName, string targetName)
        {
            MethodBase target = Target(targetName);
            MethodInfo patch = typeof(ArgsArrayMods).GetMethod(modName)!;
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

        private static MethodBase Target(string name)
        {
            return typeof(ArgsArrayTarget).GetMethod(name)!;
        }

        private static string ViaHarmony()
        {
            MethodBase target = Target(nameof(ArgsArrayTarget.Measure));
            const string harmonyId = "test.argsarray";
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            try
            {
                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(ArgsArrayMods).GetMethod(nameof(ArgsArrayMods.Reads))!));
                return Record((holder, amount, label) => holder.Measure(amount, label));
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }
        }

        private static string ViaConcord()
        {
            MethodBase target = Target(nameof(ArgsArrayTarget.Measure));
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Injection injection in PostfixMapping.Map(target, typeof(ArgsArrayMods).GetMethod(nameof(ArgsArrayMods.Reads))!, "test.argsarray", 400))
            {
                live.Add((live.Count, injection));
            }

            ComposeResult composed = WrapperComposer.Compose(target, InjectionOrderer.OrderForComposition(live));
            Assert.Empty(composed.Rejected);
            Func<ArgsArrayTarget, int, string, int> wrapper =
                (Func<ArgsArrayTarget, int, string, int>)composed.Wrapper.CreateDelegate(typeof(Func<ArgsArrayTarget, int, string, int>));

            return Record((holder, amount, label) => wrapper(holder, amount, label));
        }

        private static string Record(Func<ArgsArrayTarget, int, string, int> call)
        {
            ArgsArrayMods.Entries.Clear();
            int produced = call(new ArgsArrayTarget(), 7, "node");
            return produced + "|" + string.Join(",", ArgsArrayMods.Entries);
        }
    }
}
