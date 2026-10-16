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
    public static class InfixMapHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Add(int a, int b)
        {
            InnerPatchLog.Entries.Add("add:" + a + "," + b);
            return a + b;
        }
    }

    public sealed class InfixMapBox
    {
        public int Scale;

        public InfixMapBox(int scale)
        {
            Scale = scale;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Times(int value)
        {
            InnerPatchLog.Entries.Add("times:" + value);
            return value * Scale;
        }
    }

    public static class InfixMapTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            return InfixMapHelper.Add(1, 2) + 10;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Scaled(InfixMapBox box)
        {
            return box.Times(3) + 1;
        }

        public static IEnumerable<int> Gen(int target)
        {
            yield return InfixMapHelper.Add(target, 1);
        }
    }

    public static class InfixMapMods
    {
        public static bool SkipAdd(ref int __result, int a, int b)
        {
            InnerPatchLog.Entries.Add("skip:" + a + "," + b);
            __result = 100;
            return false;
        }

        public static void BumpA(ref int a)
        {
            a += 5;
        }

        public static bool ReadInstance(InfixMapBox __instance, ref int __result)
        {
            __result = __instance.Scale + 1000;
            return false;
        }

        public static int DoubleResult(int __result)
        {
            return __result * 2;
        }
    }

    [Collection("HarmonySerial")]
    public sealed class InfixMappingTests
    {
        private static readonly MethodInfo Add = HarmonyVersions.Method(typeof(InfixMapHelper), nameof(InfixMapHelper.Add));

        private static readonly MethodInfo Times = HarmonyVersions.Method(typeof(InfixMapBox), nameof(InfixMapBox.Times));

        [Fact]
        public void InnerPrefix_ReturningFalseWithResult_SkipsTheCall()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            Func<int> run = Wrap<Func<int>>(nameof(InfixMapTarget.Run), Add, nameof(InfixMapMods.SkipAdd), true);

            InnerPatchLog.Entries.Clear();
            Assert.Equal(110, run());
            Assert.Equal(new List<string> { "skip:1,2" }, InnerPatchLog.Entries);
        }

        [Fact]
        public void InnerPrefix_WritingAByRefArgument_ChangesWhatTheCallReceives()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            Func<int> run = Wrap<Func<int>>(nameof(InfixMapTarget.Run), Add, nameof(InfixMapMods.BumpA), true);

            InnerPatchLog.Entries.Clear();
            Assert.Equal(18, run());
            Assert.Equal(new List<string> { "add:6,2" }, InnerPatchLog.Entries);
        }

        [Fact]
        public void InnerPrefix_ReadingTheCallSiteReceiver_BindsInstance()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            Func<InfixMapBox, int> run =
                Wrap<Func<InfixMapBox, int>>(nameof(InfixMapTarget.Scaled), Times, nameof(InfixMapMods.ReadInstance), true);

            InnerPatchLog.Entries.Clear();
            Assert.Equal(1008, run(new InfixMapBox(7)));
            Assert.Empty(InnerPatchLog.Entries);
        }

        [Fact]
        public void InnerPostfix_ReturningAValue_ReplacesTheCallResult()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            Func<int> run = Wrap<Func<int>>(nameof(InfixMapTarget.Run), Add, nameof(InfixMapMods.DoubleResult), false);

            InnerPatchLog.Entries.Clear();
            Assert.Equal(16, run());
            Assert.Equal(new List<string> { "add:1,2" }, InnerPatchLog.Entries);
        }

        [Fact]
        public void OuterBinding_IsRefusedWithAReasonNamingTheParameter()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodInfo target = HarmonyVersions.Method(typeof(InfixMapTarget), nameof(InfixMapTarget.Run));
            MethodInfo patchMethod = typeof(InfixMapOuterMods).GetMethod(nameof(InfixMapOuterMods.ReadsOuter))!;
            Patch patch = HarmonyVersions.InfixPatch(
                HarmonyVersions.Fix(patchMethod, HarmonyVersions.InnerMethodSelector(Add), "innerMethod"));

            string? reason = InfixMapping.Validate(target, patch);

            Assert.NotNull(reason);
            Assert.Contains("outer", reason);
        }

        [Fact]
        public void InnerPostfix_ReadingACapturedOuterVariable_BindsTheHoistedField()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            IEnumerator<int> plain = InfixMapTarget.Gen(5).GetEnumerator();
            Assert.True(plain.MoveNext());
            Assert.Equal(6, plain.Current);

            IEnumerator<int> machine = InfixMapTarget.Gen(5).GetEnumerator();
            MethodInfo moveNext = machine.GetType().GetMethod(
                "MoveNext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
            MethodInfo patchMethod = typeof(InfixMapOuterMods).GetMethod(nameof(InfixMapOuterMods.AddCaptured))!;
            Patch patch = HarmonyVersions.InfixPatch(
                HarmonyVersions.Fix(patchMethod, HarmonyVersions.InnerMethodSelector(Add), "innerMethod"));

            Assert.Null(InfixMapping.Validate(moveNext, patch));

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Injection injection in InfixMapping.Map(moveNext, patch, false))
            {
                live.Add((live.Count, injection));
            }

            ComposeResult composed = WrapperComposer.Compose(moveNext, InjectionOrderer.OrderForComposition(live));
            Assert.Empty(composed.Rejected);

            InnerPatchLog.Entries.Clear();
            Assert.Equal(true, composed.Wrapper.Invoke(null, new object[] { machine }));
            Assert.Equal(11, machine.Current);
            Assert.Equal(new List<string> { "add:5,1" }, InnerPatchLog.Entries);
        }

        private static TDelegate Wrap<TDelegate>(string targetName, MethodInfo inner, string patchName, bool isPrefix)
            where TDelegate : Delegate
        {
            MethodInfo target = HarmonyVersions.Method(typeof(InfixMapTarget), targetName);
            MethodInfo patchMethod = HarmonyVersions.Method(typeof(InfixMapMods), patchName);
            Patch patch = HarmonyVersions.InfixPatch(
                HarmonyVersions.Fix(patchMethod, HarmonyVersions.InnerMethodSelector(inner), "innerMethod"));

            Assert.Null(InfixMapping.Validate(target, patch));

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Injection injection in InfixMapping.Map(target, patch, isPrefix))
            {
                live.Add((live.Count, injection));
            }

            Assert.NotEmpty(live);

            ComposeResult composed = WrapperComposer.Compose(target, InjectionOrderer.OrderForComposition(live));
            Assert.Empty(composed.Rejected);
            return (TDelegate)composed.Wrapper.CreateDelegate(typeof(TDelegate));
        }
    }

    public static class InfixMapOuterMods
    {
        public static void ReadsOuter([HarmonyOuter] int a)
        {
            _ = a;
        }

        public static int AddCaptured(
            int __result,
            [HarmonyOuter] [HarmonyArgument("target", ArgumentMode.Captured)] int target)
        {
            return __result + target;
        }
    }
}
