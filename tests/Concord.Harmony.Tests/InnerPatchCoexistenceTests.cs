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
    public static class InnerPatchLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class InnerPatchHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Step(int x)
        {
            InnerPatchLog.Entries.Add("step:" + x);
            return x + 1;
        }
    }

    public static class InnerPatchTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InnerPatchHeadTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InnerPatchInvokeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InnerPatchMods
    {
        public static void HarmonyInnerPrefix()
        {
            InnerPatchLog.Entries.Add("harmony-inner");
        }

        public static void ConcordHead(ControlHandle ch)
        {
            InnerPatchLog.Entries.Add("concord-head");
        }

        public static void BeforeStep(ControlHandle ch)
        {
            InnerPatchLog.Entries.Add("concord-invoke");
        }

        public static void HeadThatCallsStep(ControlHandle ch)
        {
            InnerPatchLog.Entries.Add("concord-head-calls:" + InnerPatchHelper.Step(9));
        }
    }

    [Collection("HarmonySerial")]
    public sealed class InnerPatchCoexistenceTests
    {
        private static Patches FakeInnerPrefixes(MethodInfo innerMethod)
        {
            Patch patch = new Patch(
                typeof(InnerPatchMods).GetMethod(nameof(InnerPatchMods.HarmonyInnerPrefix)),
                0,
                "test.inner.owner",
                0,
                null,
                null,
                false);
            HarmonyVersions.Field(typeof(Patch), nameof(Patch.innerMethod)).SetValue(patch, new InnerMethod(innerMethod));
            return new Patches(null, null, null, null, new Patch[] { patch }, null);
        }

        [Fact]
        public void HarmonyAlone_InnerPrefix_ThrowsInHarmonyAndNeverLands()
        {
            if (HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodInfo target = HarmonyVersions.Method(typeof(InnerPatchTarget), nameof(InnerPatchTarget.Run));
            MethodInfo innerPrefix = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.HarmonyInnerPrefix));
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.inner.baseline");

            try
            {
                Exception failure = Record.Exception(() =>
                    new PatchProcessor(harmony, target).AddInnerPrefix(innerPrefix).Patch());

                Assert.IsType<NullReferenceException>(failure);
                Assert.Contains("Infix", failure.StackTrace);
                Assert.Null(PatchProcessor.GetPatchInfo(target));

                InnerPatchLog.Entries.Clear();
                Assert.Equal(5, InnerPatchTarget.Run());
                Assert.Equal(new List<string> { "body", "step:1", "step:2" }, InnerPatchLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.inner.baseline");
            }
        }

        [Fact]
        public void Validate_InnerPatch_HeadInjectionThatNeverCallsInner_Accepted()
        {
            MethodBase target = HarmonyVersions.Method(typeof(InnerPatchHeadTarget), nameof(InnerPatchHeadTarget.Run));
            MethodInfo head = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.ConcordHead));
            Injection injection = new Injection(head, new InjectAt.Head(), "test.concord", 0);

            string? reason = SupportMatrix.Validate(
                target,
                new[] { injection },
                FakeInnerPrefixes(HarmonyVersions.Method(typeof(InnerPatchHelper), nameof(InnerPatchHelper.Step))));

            Assert.Null(reason);
        }

        [Fact]
        public void Validate_InnerPatch_InvokeInjectionOnSameCallSite_Accepted()
        {
            MethodBase target = HarmonyVersions.Method(typeof(InnerPatchInvokeTarget), nameof(InnerPatchInvokeTarget.Run));
            MethodInfo beforeStep = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.BeforeStep));
            Injection injection = new Injection(
                beforeStep,
                new InjectAt.Invoke(typeof(InnerPatchHelper), nameof(InnerPatchHelper.Step), At.Head, 0),
                "test.concord",
                0);

            string? reason = SupportMatrix.Validate(
                target,
                new[] { injection },
                FakeInnerPrefixes(HarmonyVersions.Method(typeof(InnerPatchHelper), nameof(InnerPatchHelper.Step))));

            Assert.Null(reason);
        }

        [Fact]
        public void Validate_InnerPatch_InjectionBodyCallsInnerMethod_Rejected()
        {
            MethodBase target = HarmonyVersions.Method(typeof(InnerPatchHeadTarget), nameof(InnerPatchHeadTarget.Run));
            MethodInfo head = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.HeadThatCallsStep));
            Injection injection = new Injection(head, new InjectAt.Head(), "test.concord", 0);

            string? reason = SupportMatrix.Validate(
                target,
                new[] { injection },
                FakeInnerPrefixes(HarmonyVersions.Method(typeof(InnerPatchHelper), nameof(InnerPatchHelper.Step))));

            Assert.NotNull(reason);
            Assert.Contains("counts by position", reason);
        }

        [Fact]
        public void Validate_InnerPatch_TranspilerInjection_Rejected()
        {
            MethodBase target = HarmonyVersions.Method(typeof(InnerPatchHeadTarget), nameof(InnerPatchHeadTarget.Run));
            MethodInfo head = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.ConcordHead));
            Injection injection = new Injection(head, new InjectAt.Transpiler(false), "test.concord", 0);

            string? reason = SupportMatrix.Validate(
                target,
                new[] { injection },
                FakeInnerPrefixes(HarmonyVersions.Method(typeof(InnerPatchHelper), nameof(InnerPatchHelper.Step))));

            Assert.NotNull(reason);
            Assert.Contains("transpiler injection", reason);
        }

        [Fact]
        public void Validate_InnerPatchWithUnresolvedInnerMethod_Accepted()
        {
            MethodBase target = HarmonyVersions.Method(typeof(InnerPatchHeadTarget), nameof(InnerPatchHeadTarget.Run));
            MethodInfo head = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.HeadThatCallsStep));
            Injection injection = new Injection(head, new InjectAt.Head(), "test.concord", 0);

            Patch patch = new Patch(
                typeof(InnerPatchMods).GetMethod(nameof(InnerPatchMods.HarmonyInnerPrefix)),
                0,
                "test.inner.owner",
                0,
                null,
                null,
                false);
            Patches patchInfo = new Patches(null, null, null, null, new Patch[] { patch }, null);

            string? reason = SupportMatrix.Validate(target, new[] { injection }, patchInfo);

            Assert.Null(reason);
        }
    }

    public static class InfixHelperState
    {
        public static int Counter = 4;
    }

    public static class InfixOrderTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InfixInvokeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InfixHazardTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InfixFieldTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            int first = InfixHelperState.Counter;
            InnerPatchLog.Entries.Add("mid");
            int second = InfixHelperState.Counter;
            return first + second;
        }
    }

    public static class InfixMods
    {
        public static void InnerPrefix()
        {
            InnerPatchLog.Entries.Add("harmony-inner");
        }
    }

    internal static class HarmonyVersions
    {
        internal static readonly Assembly HarmonyAssembly = typeof(HarmonyLib.Harmony).Assembly;

        internal static bool InfixesAreReal => HarmonyAssembly.GetName().Version!.Major >= 3;

        internal static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        internal static FieldInfo Field(Type type, string name)
        {
            return type.GetField(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        internal static Type HarmonyType(string name)
        {
            return HarmonyAssembly.GetType(name) ?? throw new InvalidOperationException(name + " not found in " + HarmonyAssembly.GetName().Name);
        }

        internal static object InnerMethodSelector(MethodInfo inner, params int[] positions)
        {
            Type type = HarmonyType("HarmonyLib.InnerMethod");
            return Activator.CreateInstance(type, new object[] { inner, positions })!;
        }

        internal static object InnerTargetSelector(FieldInfo field, string kind, params int[] positions)
        {
            Type targetType = HarmonyType("HarmonyLib.InnerTarget");
            Type kindType = HarmonyType("HarmonyLib.InnerTargetKind");
            return Activator.CreateInstance(targetType, new object[] { field, Enum.Parse(kindType, kind), positions })!;
        }

        internal static HarmonyMethod Fix(MethodInfo fixMethod, object selector, string fieldName)
        {
            HarmonyMethod method = new HarmonyMethod(fixMethod);
            Field(typeof(HarmonyMethod), fieldName).SetValue(method, selector);
            return method;
        }

        internal static Patch InfixPatch(HarmonyMethod fix)
        {
            return (Patch)Activator.CreateInstance(typeof(Patch), new object[] { fix, 0, "test.inner.owner" })!;
        }

        internal static Patches WithInnerFinalizer(Patch patch)
        {
            ConstructorInfo seven = typeof(Patches).GetConstructor(new[]
            {
                typeof(Patch[]), typeof(Patch[]), typeof(Patch[]), typeof(Patch[]), typeof(Patch[]), typeof(Patch[]), typeof(Patch[]),
            }) ?? throw new InvalidOperationException("Patches constructor taking seven Patch[] parameters not found");
            return (Patches)seven.Invoke(new object?[] { null, null, null, null, null, null, new[] { patch } })!;
        }
    }

    [Collection("HarmonySerial")]
    public sealed class InfixV3CoexistenceTests
    {
        private static readonly MethodInfo Step = HarmonyVersions.Method(typeof(InnerPatchHelper), nameof(InnerPatchHelper.Step));

        private static readonly MethodInfo InnerPrefix = HarmonyVersions.Method(typeof(InfixMods), nameof(InfixMods.InnerPrefix));

        [Fact]
        public void HarmonyInfix_AndConcordHead_BothRunInObservedOrder()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodInfo target = HarmonyVersions.Method(typeof(InfixOrderTarget), nameof(InfixOrderTarget.Run));
            MethodInfo head = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.ConcordHead));
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.infix.order");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });
            ForeignRouteResult? result = null;

            try
            {
                new PatchProcessor(harmony, target)
                    .AddInnerPrefix(HarmonyVersions.Fix(InnerPrefix, HarmonyVersions.InnerMethodSelector(Step), "innerMethod"))
                    .Patch();

                InnerPatchLog.Entries.Clear();
                Assert.Equal(5, InfixOrderTarget.Run());
                Assert.Equal(
                    new List<string> { "body", "harmony-inner", "step:1", "harmony-inner", "step:2" },
                    InnerPatchLog.Entries);

                Injection injection = new Injection(head, new InjectAt.Head(), "test.concord", 0);
                Assert.Null(bridge.ValidateRoute(target, new[] { injection }));

                result = bridge.TryRoute(target, new[] { injection }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                InnerPatchLog.Entries.Clear();
                Assert.Equal(5, InfixOrderTarget.Run());
                Assert.Equal(
                    new List<string> { "concord-head", "body", "harmony-inner", "step:1", "harmony-inner", "step:2" },
                    InnerPatchLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmony, "test.infix.order");
            }
        }

        [Fact]
        public void HarmonyInfix_AndConcordInvokeOnTheSameCallSite_InfixStillLandsOnTheRightCall()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodInfo target = HarmonyVersions.Method(typeof(InfixInvokeTarget), nameof(InfixInvokeTarget.Run));
            MethodInfo beforeStep = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.BeforeStep));
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.infix.invoke");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });
            ForeignRouteResult? result = null;

            try
            {
                new PatchProcessor(harmony, target)
                    .AddInnerPrefix(HarmonyVersions.Fix(InnerPrefix, HarmonyVersions.InnerMethodSelector(Step, 1), "innerMethod"))
                    .Patch();

                Injection injection = new Injection(
                    beforeStep,
                    new InjectAt.Invoke(typeof(InnerPatchHelper), nameof(InnerPatchHelper.Step), At.Head, 0),
                    "test.concord",
                    0);

                Assert.Null(bridge.ValidateRoute(target, new[] { injection }));

                result = bridge.TryRoute(target, new[] { injection }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                InnerPatchLog.Entries.Clear();
                Assert.Equal(5, InfixInvokeTarget.Run());
                Assert.Equal(
                    new List<string> { "body", "concord-invoke", "harmony-inner", "step:1", "concord-invoke", "step:2" },
                    InnerPatchLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmony, "test.infix.invoke");
            }
        }

        [Fact]
        public void InjectionBodyCallsInner_GuardRejects_AndBypassingItLandsTheInfixOnTheWrongCall()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodInfo target = HarmonyVersions.Method(typeof(InfixHazardTarget), nameof(InfixHazardTarget.Run));
            MethodInfo head = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.HeadThatCallsStep));
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.infix.hazard");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });
            ForeignRouteResult? result = null;
            Injection injection = new Injection(head, new InjectAt.Head(), "test.concord", 0);

            try
            {
                result = bridge.TryRoute(target, new[] { injection }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                new PatchProcessor(harmony, target)
                    .AddInnerPrefix(HarmonyVersions.Fix(InnerPrefix, HarmonyVersions.InnerMethodSelector(Step, 1), "innerMethod"))
                    .Patch();

                InnerPatchLog.Entries.Clear();
                InfixHazardTarget.Run();

                int wrapped = InnerPatchLog.Entries.IndexOf("harmony-inner");
                Assert.True(wrapped >= 0);
                Assert.Equal("step:9", InnerPatchLog.Entries[wrapped + 1]);

                Assert.NotNull(bridge.ValidateRoute(target, new[] { injection }));
                Assert.Contains("counts by position", bridge.ValidateRoute(target, new[] { injection }));
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.infix.hazard");
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void InnerTargetFieldRead_IsRejected()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodBase target = HarmonyVersions.Method(typeof(InfixFieldTarget), nameof(InfixFieldTarget.Run));
            MethodInfo head = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.ConcordHead));
            FieldInfo counter = HarmonyVersions.Field(typeof(InfixHelperState), nameof(InfixHelperState.Counter));

            HarmonyMethod fix = HarmonyVersions.Fix(
                InnerPrefix,
                HarmonyVersions.InnerTargetSelector(counter, "FieldRead", 1),
                "innerTarget");
            Patches patchInfo = new Patches(null, null, null, null, new[] { HarmonyVersions.InfixPatch(fix) }, null);

            string? reason = SupportMatrix.Validate(
                target,
                new[] { new Injection(head, new InjectAt.Head(), "test.concord", 0) },
                patchInfo);

            Assert.NotNull(reason);
            Assert.Contains("non-method operation", reason);
        }

        [Fact]
        public void InnerFinalizer_IsSeenByTheGuard()
        {
            if (!HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodBase target = HarmonyVersions.Method(typeof(InfixOrderTarget), nameof(InfixOrderTarget.Run));
            MethodInfo head = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.HeadThatCallsStep));

            HarmonyMethod fix = HarmonyVersions.Fix(InnerPrefix, HarmonyVersions.InnerMethodSelector(Step), "innerMethod");
            Patches patchInfo = HarmonyVersions.WithInnerFinalizer(HarmonyVersions.InfixPatch(fix));

            Assert.True(SupportMatrix.HasInnerPatches(patchInfo));

            string? reason = SupportMatrix.Validate(
                target,
                new[] { new Injection(head, new InjectAt.Head(), "test.concord", 0) },
                patchInfo);

            Assert.NotNull(reason);
            Assert.Contains("counts by position", reason);
        }
    }
}
