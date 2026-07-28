using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    using CodeInstruction = HarmonyLib.CodeInstruction;

    public static class DiscordSequenceLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class DiscordSequenceTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Foo()
        {
            DiscordSequenceLog.Entries.Add("body");
            return 7;
        }
    }

    public static class DiscordSequenceMods
    {
        public static void PostfixA()
        {
            DiscordSequenceLog.Entries.Add("postfixA");
        }

        public static void PostfixB()
        {
            DiscordSequenceLog.Entries.Add("postfixB");
        }

        public static void ConcordHead(ControlHandle ch)
        {
            DiscordSequenceLog.Entries.Add("head");
        }
    }

    public static class UncontestedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 3;
        }
    }

    public static class UncontestedMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
        }
    }

    public static class ForceRouteTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 5;
        }
    }

    public static class ForceRouteMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            ForceRouteTarget.Entries.Add("head");
        }
    }

    public static class ForeignOrderLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class ForeignOrderTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int BeforeCase()
        {
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int AfterCase()
        {
            ForeignOrderLog.Entries.Add("body");
            return 7;
        }
    }

    public static class ForeignOrderInjectionMethods
    {
        public static void ObserveReturn(ControlHandle<int> ch)
        {
            ForeignOrderLog.Entries.Add("return:" + ch.ReturnValue);
            ch.ReturnValue += 1;
        }

        public static void Head(ControlHandle ch)
        {
            ForeignOrderLog.Entries.Add("head");
        }
    }

    public static class ForeignOrderTranspilers
    {
        public static IEnumerable<CodeInstruction> SwapSevenTo42(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4_7 || (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int value && value == 7))
                {
                    CodeInstruction replaced = new CodeInstruction(OpCodes.Ldc_I4, 42);
                    replaced.labels.AddRange(instruction.labels);
                    replaced.blocks.AddRange(instruction.blocks);
                    yield return replaced;
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }

    public static class PrefixSkipLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class PrefixSkipTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Skippy()
        {
            PrefixSkipLog.Entries.Add("body");
            return 7;
        }
    }

    public static class PrefixSkipMods
    {
        public static bool SkipPrefix(ref int __result)
        {
            PrefixSkipLog.Entries.Add("prefix");
            __result = -100;
            return false;
        }

        public static void ConcordHead(ControlHandle ch)
        {
            PrefixSkipLog.Entries.Add("head");
        }
    }

    public static class AroundGapLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class AroundGapTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Triple(int x)
        {
            AroundGapLog.Entries.Add("body");
            return x * 3;
        }
    }

    public static class AroundGapMods
    {
        public static int WrapTriple(int x, Operation<int, int> original)
        {
            AroundGapLog.Entries.Add("pre");
            int inner = original.Invoke(x);
            AroundGapLog.Entries.Add("post");
            return inner + 1;
        }

        public static void ForeignPostfix()
        {
            AroundGapLog.Entries.Add("foreignPostfix");
        }
    }

    public static class InvokeGapHelper
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Step()
        {
            Entries.Add("step");
        }
    }

    public static class InvokeGapTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run()
        {
            InvokeGapHelper.Entries.Add("before");
            InvokeGapHelper.Step();
            InvokeGapHelper.Entries.Add("after");
        }
    }

    public static class InvokeGapMods
    {
        public static void BeforeStep(ControlHandle ch)
        {
            InvokeGapHelper.Entries.Add("injected");
        }
    }

    public static class ConstantGapTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int AddBonus(int x)
        {
            return x + 100;
        }
    }

    public static class ConstantGapMods
    {
        public static int ReplaceBonus(int original)
        {
            return 200;
        }

        public static void ForeignPostfix()
        {
            ConstantGapTarget.Entries.Add("foreignPostfix");
        }
    }

    public static class SelfOwnedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 6;
        }
    }

    public static class SelfOwnedMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
        }
    }

    public static class ForeignOwnersTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 8;
        }
    }

    public static class ForeignOwnersMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
        }

        public static void ForeignPostfix()
        {
        }
    }

    public sealed class InstanceGapTarget
    {
        public int Seed;

        public InstanceGapTarget(int seed)
        {
            Seed = seed;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int AddOne()
        {
            return Seed + 1;
        }
    }

    public static class InstanceGapMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            InstanceGapLog.Entries.Add("head");
        }
    }

    public static class InstanceGapLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class DisposalGranularityTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 9;
        }
    }

    public static class DisposalGranularityMods
    {
        public static void First(ControlHandle ch)
        {
            DisposalGranularityTarget.Entries.Add("first");
        }

        public static void Second(ControlHandle ch)
        {
            DisposalGranularityTarget.Entries.Add("second");
        }

        public static void ForeignPostfix()
        {
            DisposalGranularityTarget.Entries.Add("foreignPostfix");
        }
    }

    public static class ApplyToRoutedTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 4;
        }
    }

    public static class ApplyToRoutedMods
    {
        public static void First(ControlHandle ch)
        {
            ApplyToRoutedTarget.Entries.Add("first");
        }

        public static void Second(ControlHandle ch)
        {
            ApplyToRoutedTarget.Entries.Add("second");
        }
    }

    public abstract class AbstractRollbackTarget
    {
        public abstract int DoIt();
    }

    public static class AbstractRollbackMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
        }
    }

    public static class RebuildFailureRecoveredTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 1;
        }
    }

    public static class RebuildFailureRecoveredMods
    {
        public static void First(ControlHandle ch)
        {
            RebuildFailureRecoveredTarget.Entries.Add("first");
        }

        public static void Second(ControlHandle ch)
        {
            RebuildFailureRecoveredTarget.Entries.Add("second");
        }
    }

    public static class RebuildFailureLostTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 1;
        }
    }

    public static class RebuildFailureLostMods
    {
        public static void First(ControlHandle ch)
        {
        }

        public static void Second(ControlHandle ch)
        {
        }
    }

    public static class DisposalFailureTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 1;
        }
    }

    public static class DisposalFailureMods
    {
        public static void First(ControlHandle ch)
        {
            DisposalFailureTarget.Entries.Add("first");
        }
    }

    public static class ComposeFailureLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class ComposeFailureHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Step()
        {
            ComposeFailureLog.Entries.Add("step");
        }
    }

    public static class ComposeFailureTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(int x)
        {
            ComposeFailureHelper.Step();
            ComposeFailureLog.Entries.Add("body");
            return x * 3;
        }
    }

    public static class ComposeFailureMods
    {
        public static int WrapRun(int x, Operation<int, int> original)
        {
            ComposeFailureLog.Entries.Add("pre");
            int inner = original.Invoke(x);
            ComposeFailureLog.Entries.Add("post");
            return inner;
        }

        public static void BeforeStep(ControlHandle ch)
        {
            ComposeFailureLog.Entries.Add("injected");
        }

        public static void ForeignPostfix()
        {
            ComposeFailureLog.Entries.Add("foreignPostfix");
        }
    }

    [Collection("HarmonySerial")]
    public sealed class CoexistenceTests
    {
        private static Injection MakeHeadInjection(MethodInfo method, string owner)
        {
            return new Injection(method, new InjectAt.Head(), owner, 0);
        }

        [Fact]
        public void DiscordSequence_ConcordSurvivesForeignPatchAndUnpatchCycles()
        {
            MethodInfo target = typeof(DiscordSequenceTarget).GetMethod(nameof(DiscordSequenceTarget.Foo));
            MethodInfo headMethod = typeof(DiscordSequenceMods).GetMethod(nameof(DiscordSequenceMods.ConcordHead));
            MethodInfo postfixA = typeof(DiscordSequenceMods).GetMethod(nameof(DiscordSequenceMods.PostfixA));
            MethodInfo postfixB = typeof(DiscordSequenceMods).GetMethod(nameof(DiscordSequenceMods.PostfixB));

            HarmonyLib.Harmony harmonyA = new HarmonyLib.Harmony("test.modA");
            HarmonyLib.Harmony harmonyB = new HarmonyLib.Harmony("test.modB");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                harmonyA.Patch(target, postfix: new HarmonyMethod(postfixA));

                DiscordSequenceLog.Entries.Clear();
                Assert.Equal(7, DiscordSequenceTarget.Foo());
                Assert.Equal(new List<string> { "body", "postfixA" }, DiscordSequenceLog.Entries);

                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.concord") }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                DiscordSequenceLog.Entries.Clear();
                Assert.Equal(7, DiscordSequenceTarget.Foo());
                Assert.Equal(new List<string> { "head", "body", "postfixA" }, DiscordSequenceLog.Entries);

                harmonyB.Patch(target, postfix: new HarmonyMethod(postfixB));

                DiscordSequenceLog.Entries.Clear();
                Assert.Equal(7, DiscordSequenceTarget.Foo());
                Assert.Equal(new List<string> { "head", "body", "postfixA", "postfixB" }, DiscordSequenceLog.Entries);

                harmonyA.UnpatchAll("test.modA");

                DiscordSequenceLog.Entries.Clear();
                Assert.Equal(7, DiscordSequenceTarget.Foo());
                Assert.Equal(new List<string> { "head", "body", "postfixB" }, DiscordSequenceLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                harmonyA.UnpatchAll("test.modA");
                harmonyB.UnpatchAll("test.modB");
            }
        }

        [Fact]
        public void TryRoute_NoForeignPatches_NotContested_NoConcordTranspilerInstalled()
        {
            MethodInfo target = typeof(UncontestedTarget).GetMethod(nameof(UncontestedTarget.Bare));
            MethodInfo headMethod = typeof(UncontestedMods).GetMethod(nameof(UncontestedMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            try
            {
                ForeignRouteResult result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.concord") }, false);

                Assert.Equal(ForeignRouteKind.NotContested, result.Kind);
                Assert.Null(PatchProcessor.GetPatchInfo(target));
            }
            finally
            {
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void TryRoute_ForceRoute_NoForeignPatches_Routes_HeadFires()
        {
            MethodInfo target = typeof(ForceRouteTarget).GetMethod(nameof(ForceRouteTarget.Bare));
            MethodInfo headMethod = typeof(ForceRouteMods).GetMethod(nameof(ForceRouteMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.concord") }, true);

                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                ForceRouteTarget.Entries.Clear();
                Assert.Equal(5, ForceRouteTarget.Bare());
                Assert.Equal(new List<string> { "head" }, ForceRouteTarget.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void ForeignTranspilerBeforeConcord_CompositionSeesRewrittenStream()
        {
            MethodInfo target = typeof(ForeignOrderTargets).GetMethod(nameof(ForeignOrderTargets.BeforeCase));
            MethodInfo foreignTranspiler = typeof(ForeignOrderTranspilers).GetMethod(nameof(ForeignOrderTranspilers.SwapSevenTo42));
            MethodInfo returnMethod = typeof(ForeignOrderInjectionMethods).GetMethod(nameof(ForeignOrderInjectionMethods.ObserveReturn));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.foreign.before");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                harmonyForeign.Patch(target, transpiler: new HarmonyMethod(foreignTranspiler) { priority = Priority.High });

                Injection returnInjection = new Injection(returnMethod, new InjectAt.Return(), "test.concord.before", 0);
                result = bridge.TryRoute(target, new[] { returnInjection }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                ForeignOrderLog.Entries.Clear();
                int value = ForeignOrderTargets.BeforeCase();

                Assert.Equal(43, value);
                Assert.Equal(new List<string> { "return:42" }, ForeignOrderLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                harmonyForeign.UnpatchAll("test.foreign.before");
            }
        }

        [Fact]
        public void ForeignTranspilerAfterConcord_BenignRewriteOverComposedStreamStaysCorrect()
        {
            MethodInfo target = typeof(ForeignOrderTargets).GetMethod(nameof(ForeignOrderTargets.AfterCase));
            MethodInfo foreignTranspiler = typeof(ForeignOrderTranspilers).GetMethod(nameof(ForeignOrderTranspilers.SwapSevenTo42));
            MethodInfo headMethod = typeof(ForeignOrderInjectionMethods).GetMethod(nameof(ForeignOrderInjectionMethods.Head));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.foreign.after");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.concord.after") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                harmonyForeign.Patch(target, transpiler: new HarmonyMethod(foreignTranspiler) { after = new[] { "concord.bridge" } });

                ForeignOrderLog.Entries.Clear();
                int value = ForeignOrderTargets.AfterCase();

                Assert.Equal(42, value);
                Assert.Equal(new List<string> { "head", "body" }, ForeignOrderLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                harmonyForeign.UnpatchAll("test.foreign.after");
            }
        }

        [Fact]
        public void ForeignPrefixReturningFalse_SkipsBodyAndConcordInjections()
        {
            MethodInfo target = typeof(PrefixSkipTarget).GetMethod(nameof(PrefixSkipTarget.Skippy));
            MethodInfo prefix = typeof(PrefixSkipMods).GetMethod(nameof(PrefixSkipMods.SkipPrefix));
            MethodInfo headMethod = typeof(PrefixSkipMods).GetMethod(nameof(PrefixSkipMods.ConcordHead));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.prefix.foreign");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.prefix.concord") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                PrefixSkipLog.Entries.Clear();
                Assert.Equal(7, PrefixSkipTarget.Skippy());
                Assert.Equal(new List<string> { "head", "body" }, PrefixSkipLog.Entries);

                harmonyForeign.Patch(target, prefix: new HarmonyMethod(prefix));

                PrefixSkipLog.Entries.Clear();
                Assert.Equal(-100, PrefixSkipTarget.Skippy());
                Assert.Equal(new List<string> { "prefix" }, PrefixSkipLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                harmonyForeign.UnpatchAll("test.prefix.foreign");
            }
        }

        [Fact]
        public void WholeMethodAround_PreAndPostFireAndForeignPostfixStillRunsOncePerOuterInvocation()
        {
            MethodInfo target = typeof(AroundGapTarget).GetMethod(nameof(AroundGapTarget.Triple));
            MethodInfo wrapMethod = typeof(AroundGapMods).GetMethod(nameof(AroundGapMods.WrapTriple));
            MethodInfo foreignPostfix = typeof(AroundGapMods).GetMethod(nameof(AroundGapMods.ForeignPostfix));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.around.foreign");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                harmonyForeign.Patch(target, postfix: new HarmonyMethod(foreignPostfix));

                Injection around = new Injection(wrapMethod, new InjectAt.Around(), "test.concord.around", 0);
                result = bridge.TryRoute(target, new[] { around }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                AroundGapLog.Entries.Clear();
                int value = AroundGapTarget.Triple(5);

                Assert.Equal(16, value);
                Assert.Equal(new List<string> { "pre", "body", "post", "foreignPostfix" }, AroundGapLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                harmonyForeign.UnpatchAll("test.around.foreign");
            }
        }

        [Fact]
        public void InvokeCallSiteInjection_FiresUnderHarmonyRouting()
        {
            MethodInfo target = typeof(InvokeGapTarget).GetMethod(nameof(InvokeGapTarget.Run));
            MethodInfo stepMethod = typeof(InvokeGapHelper).GetMethod(nameof(InvokeGapHelper.Step));
            MethodInfo beforeStep = typeof(InvokeGapMods).GetMethod(nameof(InvokeGapMods.BeforeStep));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                Injection invoke = new Injection(
                    beforeStep,
                    new InjectAt.Invoke(typeof(InvokeGapHelper), nameof(InvokeGapHelper.Step), At.Head, 0),
                    "test.concord.invoke",
                    0);
                result = bridge.TryRoute(target, new[] { invoke }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                InvokeGapHelper.Entries.Clear();
                InvokeGapTarget.Run();

                Assert.Equal(new List<string> { "before", "injected", "step", "after" }, InvokeGapHelper.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void ConstantInjection_ReplacesInlinedInt32Constant_UnderGenuinelyContestedTarget()
        {
            MethodInfo target = typeof(ConstantGapTarget).GetMethod(nameof(ConstantGapTarget.AddBonus));
            MethodInfo replaceBonus = typeof(ConstantGapMods).GetMethod(nameof(ConstantGapMods.ReplaceBonus));
            MethodInfo foreignPostfix = typeof(ConstantGapMods).GetMethod(nameof(ConstantGapMods.ForeignPostfix));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.constant.foreign");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                harmonyForeign.Patch(target, postfix: new HarmonyMethod(foreignPostfix));
                Assert.True(HasForeignPatch(target));

                Injection constant = new Injection(replaceBonus, new InjectAt.Constant(100, 0), "test.concord.constant", 0);
                result = bridge.TryRoute(target, new[] { constant }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                ConstantGapTarget.Entries.Clear();
                int value = (int)target.Invoke(null, new object[] { 5 });

                Assert.Equal(205, value);
                Assert.Equal(new List<string> { "foreignPostfix" }, ConstantGapTarget.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                harmonyForeign.UnpatchAll("test.constant.foreign");
            }
        }

        [Fact]
        public void TryRoute_TargetPatchedOnlyByConcord_IsTreatedAsUncontested()
        {
            MethodInfo target = typeof(SelfOwnedTarget).GetMethod(nameof(SelfOwnedTarget.Bare));
            MethodInfo headMethod = typeof(SelfOwnedMods).GetMethod(nameof(SelfOwnedMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult firstResult = null;
            try
            {
                firstResult = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.selfowned") }, true);
                Assert.Equal(ForeignRouteKind.Routed, firstResult.Kind);

                ForeignRouteResult secondResult = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.selfowned.second") }, false);

                Assert.Equal(ForeignRouteKind.NotContested, secondResult.Kind);
            }
            finally
            {
                firstResult?.Handle?.Dispose();
            }
        }

        [Fact]
        public void ForeignOwners_ReturnsForeignOwnerId_ExcludesConcordsOwnOwner()
        {
            MethodInfo target = typeof(ForeignOwnersTarget).GetMethod(nameof(ForeignOwnersTarget.Bare));
            MethodInfo headMethod = typeof(ForeignOwnersMods).GetMethod(nameof(ForeignOwnersMods.ConcordHead));
            MethodInfo foreignPostfix = typeof(ForeignOwnersMods).GetMethod(nameof(ForeignOwnersMods.ForeignPostfix));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.foreignowners.foreign");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                harmonyForeign.Patch(target, postfix: new HarmonyMethod(foreignPostfix));

                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.foreignowners.concord") }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                IReadOnlyList<string> owners = bridge.ForeignOwners(target);

                Assert.Contains("test.foreignowners.foreign", owners);
                Assert.DoesNotContain("concord.bridge", owners);
            }
            finally
            {
                result?.Handle?.Dispose();
                harmonyForeign.UnpatchAll("test.foreignowners.foreign");
            }
        }

        private static bool HasForeignPatch(MethodBase target)
        {
            Patches patchInfo = PatchProcessor.GetPatchInfo(target);
            if (patchInfo == null)
            {
                return false;
            }

            foreach (Patch patch in patchInfo.Postfixes)
            {
                if (patch.PatchMethod != TranspilerParticipant.TranspileMethod)
                {
                    return true;
                }
            }

            return false;
        }

        [Fact]
        public void InstanceMethodTarget_ThisFlowsThroughArgumentSlotZero()
        {
            MethodInfo target = typeof(InstanceGapTarget).GetMethod(nameof(InstanceGapTarget.AddOne));
            MethodInfo headMethod = typeof(InstanceGapMods).GetMethod(nameof(InstanceGapMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.concord.instance") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                InstanceGapLog.Entries.Clear();
                InstanceGapTarget instance = new InstanceGapTarget(41);
                int value = instance.AddOne();

                Assert.Equal(42, value);
                Assert.Equal(new List<string> { "head" }, InstanceGapLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void DisposalGranularity_DisposeFirstThenLast_ForeignPostfixIntactThroughout()
        {
            MethodInfo target = typeof(DisposalGranularityTarget).GetMethod(nameof(DisposalGranularityTarget.Bare));
            MethodInfo first = typeof(DisposalGranularityMods).GetMethod(nameof(DisposalGranularityMods.First));
            MethodInfo second = typeof(DisposalGranularityMods).GetMethod(nameof(DisposalGranularityMods.Second));
            MethodInfo foreignPostfix = typeof(DisposalGranularityMods).GetMethod(nameof(DisposalGranularityMods.ForeignPostfix));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.disposal.foreign");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                harmonyForeign.Patch(target, postfix: new HarmonyMethod(foreignPostfix));

                Injection firstInjection = MakeHeadInjection(first, "test.disposal.first");
                Injection secondInjection = MakeHeadInjection(second, "test.disposal.second");
                result = bridge.TryRoute(target, new[] { firstInjection }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                IDetourHandle secondHandle = bridge.ApplyToRouted(target, new[] { secondInjection });

                DisposalGranularityTarget.Entries.Clear();
                DisposalGranularityTarget.Bare();
                Assert.Contains("first", DisposalGranularityTarget.Entries);
                Assert.Contains("second", DisposalGranularityTarget.Entries);
                Assert.Contains("foreignPostfix", DisposalGranularityTarget.Entries);

                result.Handle.Dispose();

                DisposalGranularityTarget.Entries.Clear();
                DisposalGranularityTarget.Bare();
                Assert.DoesNotContain("first", DisposalGranularityTarget.Entries);
                Assert.Contains("second", DisposalGranularityTarget.Entries);
                Assert.Contains("foreignPostfix", DisposalGranularityTarget.Entries);
                Assert.NotNull(PatchProcessor.GetPatchInfo(target));

                secondHandle.Dispose();

                Patches patchInfoAfter = PatchProcessor.GetPatchInfo(target);
                Assert.True(patchInfoAfter == null || !ContainsConcordTranspiler(patchInfoAfter));

                DisposalGranularityTarget.Entries.Clear();
                DisposalGranularityTarget.Bare();
                Assert.DoesNotContain("first", DisposalGranularityTarget.Entries);
                Assert.DoesNotContain("second", DisposalGranularityTarget.Entries);
                Assert.Contains("foreignPostfix", DisposalGranularityTarget.Entries);
            }
            finally
            {
                harmonyForeign.UnpatchAll("test.disposal.foreign");
            }
        }

        [Fact]
        public void ApplyToRouted_SecondInjectionLandsAfterTryRoute_BothFireInOrdererOrder()
        {
            MethodInfo target = typeof(ApplyToRoutedTarget).GetMethod(nameof(ApplyToRoutedTarget.Bare));
            MethodInfo first = typeof(ApplyToRoutedMods).GetMethod(nameof(ApplyToRoutedMods.First));
            MethodInfo second = typeof(ApplyToRoutedMods).GetMethod(nameof(ApplyToRoutedMods.Second));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            IDetourHandle secondHandle = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(first, "test.apply.first") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                secondHandle = bridge.ApplyToRouted(target, new[] { MakeHeadInjection(second, "test.apply.second") });

                ApplyToRoutedTarget.Entries.Clear();
                ApplyToRoutedTarget.Bare();

                Assert.Equal(new List<string> { "second", "first" }, ApplyToRoutedTarget.Entries);
            }
            finally
            {
                secondHandle?.Dispose();
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void TryRoute_AbstractTarget_InstallFailure_PropagatesAndCompensationRemovesRegistryEntry()
        {
            MethodInfo target = typeof(AbstractRollbackTarget).GetMethod(nameof(AbstractRollbackTarget.DoIt));
            MethodInfo headMethod = typeof(AbstractRollbackMods).GetMethod(nameof(AbstractRollbackMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            Assert.ThrowsAny<Exception>(() => bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.abstract") }, true));

            Assert.False(TranspilerParticipant.Registry.HasInjections(MethodIdentity.Normalize(target)));
        }

        [Fact]
        public void ApplyToRouted_RebuildFailsOnFirstInstallCallOnly_Recovered_FirstInjectionSurvivesAndComposesAlone()
        {
            MethodInfo target = typeof(RebuildFailureRecoveredTarget).GetMethod(nameof(RebuildFailureRecoveredTarget.Bare));
            MethodInfo first = typeof(RebuildFailureRecoveredMods).GetMethod(nameof(RebuildFailureRecoveredMods.First));
            MethodInfo second = typeof(RebuildFailureRecoveredMods).GetMethod(nameof(RebuildFailureRecoveredMods.Second));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(first, "test.recovered.first") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                Action<MethodBase> realInstall = bridge.InstallParticipant;
                int callCount = 0;
                bridge.InstallParticipant = t =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        throw new InvalidOperationException("simulated install failure");
                    }

                    realInstall(t);
                };

                Assert.ThrowsAny<Exception>(() => bridge.ApplyToRouted(target, new[] { MakeHeadInjection(second, "test.recovered.second") }));

                MethodBase normalized = MethodIdentity.Normalize(target);
                Assert.Single(TranspilerParticipant.Registry.OrderedSnapshot(normalized));

                RebuildFailureRecoveredTarget.Entries.Clear();
                RebuildFailureRecoveredTarget.Bare();
                Assert.Equal(new List<string> { "first" }, RebuildFailureRecoveredTarget.Entries);

                Patches patchInfo = PatchProcessor.GetPatchInfo(target);
                Assert.NotNull(patchInfo);
                int concordTranspilerCount = 0;
                foreach (Patch patch in patchInfo.Transpilers)
                {
                    if (patch.PatchMethod == TranspilerParticipant.TranspileMethod)
                    {
                        concordTranspilerCount++;
                    }
                }

                Assert.Equal(1, concordTranspilerCount);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void ApplyToRouted_RebuildAlwaysFails_ParticipantLost_RegistryClearedAndLogged()
        {
            MethodInfo target = typeof(RebuildFailureLostTarget).GetMethod(nameof(RebuildFailureLostTarget.Bare));
            MethodInfo first = typeof(RebuildFailureLostMods).GetMethod(nameof(RebuildFailureLostMods.First));
            MethodInfo second = typeof(RebuildFailureLostMods).GetMethod(nameof(RebuildFailureLostMods.Second));
            List<string> logs = new List<string>();
            HarmonyBridge bridge = new HarmonyBridge(logs.Add);

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(first, "test.lost.first") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                bridge.InstallParticipant = _ => throw new InvalidOperationException("always fails");

                Assert.ThrowsAny<Exception>(() => bridge.ApplyToRouted(target, new[] { MakeHeadInjection(second, "test.lost.second") }));

                MethodBase normalized = MethodIdentity.Normalize(target);
                Assert.Empty(TranspilerParticipant.Registry.OrderedSnapshot(normalized));

                bool foundParticipantLostLog = false;
                foreach (string line in logs)
                {
                    if (line.Contains("participant lost"))
                    {
                        foundParticipantLostLog = true;
                        break;
                    }
                }

                Assert.True(foundParticipantLostLog);
            }
            finally
            {
                result = null;
            }
        }

        [Fact]
        public void Dispose_RemoveFailsOnFirstCallOnly_Recovered_HandleStillAppliedAndInjectionStillFiresOnce()
        {
            MethodInfo target = typeof(DisposalFailureTarget).GetMethod(nameof(DisposalFailureTarget.Bare));
            MethodInfo first = typeof(DisposalFailureMods).GetMethod(nameof(DisposalFailureMods.First));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = bridge.TryRoute(target, new[] { MakeHeadInjection(first, "test.disposefail.first") }, true);
            Assert.Equal(ForeignRouteKind.Routed, result.Kind);

            Action<MethodBase> realRemove = bridge.RemoveParticipant;
            int callCount = 0;
            bridge.RemoveParticipant = t =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new InvalidOperationException("simulated remove failure");
                }

                realRemove(t);
            };

            Assert.ThrowsAny<Exception>(() => result.Handle.Dispose());

            MethodBase normalized = MethodIdentity.Normalize(target);
            Assert.Single(TranspilerParticipant.Registry.OrderedSnapshot(normalized));
            Assert.True(result.Handle.IsApplied);

            Patches patchInfo = PatchProcessor.GetPatchInfo(target);
            Assert.NotNull(patchInfo);
            int concordTranspilerCount = 0;
            foreach (Patch patch in patchInfo.Transpilers)
            {
                if (patch.PatchMethod == TranspilerParticipant.TranspileMethod)
                {
                    concordTranspilerCount++;
                }
            }

            Assert.Equal(1, concordTranspilerCount);

            DisposalFailureTarget.Entries.Clear();
            DisposalFailureTarget.Bare();
            Assert.Equal(new List<string> { "first" }, DisposalFailureTarget.Entries);

            bridge.RemoveParticipant = realRemove;
            result.Handle.Dispose();

            Patches patchInfoAfter = PatchProcessor.GetPatchInfo(target);
            Assert.True(patchInfoAfter == null || !ContainsConcordTranspiler(patchInfoAfter));
        }

        private static bool ContainsConcordTranspiler(Patches patchInfo)
        {
            foreach (Patch patch in patchInfo.Transpilers)
            {
                if (patch.PatchMethod == TranspilerParticipant.TranspileMethod)
                {
                    return true;
                }
            }

            return false;
        }

        [Fact]
        public void TryRoute_PassesSupportMatrixButFailsComposition_RejectsWithCleanCompensation()
        {
            MethodInfo target = typeof(ComposeFailureTarget).GetMethod(nameof(ComposeFailureTarget.Run));
            MethodInfo wrapMethod = typeof(ComposeFailureMods).GetMethod(nameof(ComposeFailureMods.WrapRun));
            MethodInfo beforeStep = typeof(ComposeFailureMods).GetMethod(nameof(ComposeFailureMods.BeforeStep));
            MethodInfo stepMethod = typeof(ComposeFailureHelper).GetMethod(nameof(ComposeFailureHelper.Step));
            MethodInfo foreignPostfix = typeof(ComposeFailureMods).GetMethod(nameof(ComposeFailureMods.ForeignPostfix));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.composefailure.foreign");
            List<string> logs = new List<string>();
            HarmonyBridge bridge = new HarmonyBridge(logs.Add);

            try
            {
                harmonyForeign.Patch(target, postfix: new HarmonyMethod(foreignPostfix));

                Injection around = new Injection(wrapMethod, new InjectAt.Around(), "test.composefailure.around", 0);
                Injection invoke = new Injection(
                    beforeStep,
                    new InjectAt.Invoke(typeof(ComposeFailureHelper), nameof(ComposeFailureHelper.Step), At.Head, 0),
                    "test.composefailure.invoke",
                    0);

                string preValidateReason = SupportMatrix.Validate(MethodIdentity.Normalize(target), new Injection[] { around, invoke }, PatchProcessor.GetPatchInfo(target));
                Assert.Null(preValidateReason);

                ForeignRouteResult result = bridge.TryRoute(target, new[] { around, invoke }, forceRoute: false);

                Assert.Equal(ForeignRouteKind.Rejected, result.Kind);

                ComposeFailureLog.Entries.Clear();
                int value = ComposeFailureTarget.Run(5);

                Assert.Equal(15, value);
                Assert.Equal(new List<string> { "step", "body", "foreignPostfix" }, ComposeFailureLog.Entries);

                MethodBase normalized = MethodIdentity.Normalize(target);
                Assert.False(TranspilerParticipant.Registry.HasInjections(normalized));

                Patches patchInfoAfter = PatchProcessor.GetPatchInfo(target);
                Assert.NotNull(patchInfoAfter);
                Assert.False(ContainsConcordTranspiler(patchInfoAfter));
                Assert.Single(patchInfoAfter.Postfixes);
                Assert.Equal(foreignPostfix, patchInfoAfter.Postfixes[0].PatchMethod);

                bool foundStreamRejectedLog = false;
                foreach (string line in logs)
                {
                    if (line.Contains(CoexistenceLogMarkers.StreamRejected))
                    {
                        foundStreamRejectedLog = true;
                        break;
                    }
                }

                Assert.True(foundStreamRejectedLog);
            }
            finally
            {
                TranspilerParticipant.Registry.Clear(MethodIdentity.Normalize(target));
                TranspilerParticipant.LastStreamFailure = null;
                TranspilerParticipant.Log = null;
                harmonyForeign.UnpatchAll("test.composefailure.foreign");
            }
        }
    }
}
