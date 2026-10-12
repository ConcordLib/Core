using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

[Collection("HarmonySerial")]
public sealed class SuppressionTests
{
    private const string ModId = "test.suppression.mod";

    private const string OtherModId = "test.suppression.other";

    [Fact]
    public void Patch_OnASuppressedTarget_LeavesTheMethodRunningConcordsBody()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(DirectTarget), nameof(DirectTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(SuppressionMods.SilentHead)) });
            Arm(decider, backend);
            SuppressionLog.Entries.Clear();

            harmony.Patch(target, prefix: MethodOf(nameof(SuppressionMods.DirectPrefix)));

            Assert.Equal(new[] { (MethodBase)target }, decider.Asked);
            Assert.Equal(99, DirectTarget.Compute());
            Assert.Equal(new[] { "direct" }, SuppressionLog.Entries);
            Assert.Single(PatchProcessor.GetPatchInfo(target).Prefixes);
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void SuppressedRebuild_HandsHarmonyTheStubMintedForThatOriginal()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(StubHandbackTarget), nameof(StubHandbackTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(SuppressionMods.SilentHead)) });
            Arm(decider, backend);
            MethodInfo minted = OriginalStubs.For(target);

            MethodInfo handedBack = harmony.Patch(target, prefix: MethodOf(nameof(SuppressionMods.StubHandbackPrefix)));

            Assert.Same(minted, handedBack);
            Assert.Same(minted, OriginalStubs.For(target));
            Assert.Same(handedBack.DeclaringType, handedBack.ReflectedType);
            Assert.Equal(99, StubHandbackTarget.Compute());
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void ClassProcessorAndPatchAll_OnSuppressedTargets_ReachTheSameSuppression()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo classProcessorTarget = Fresh(typeof(ClassProcessorTarget), nameof(ClassProcessorTarget.Compute));
        MethodInfo patchAllTarget = Fresh(typeof(PatchAllTarget), nameof(PatchAllTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(classProcessorTarget);
        decider.Own(patchAllTarget);

        IDetourHandle? classProcessorConcord = null;
        IDetourHandle? patchAllConcord = null;
        try
        {
            classProcessorConcord = backend.ApplyComposed(classProcessorTarget, new[] { HeadOf(nameof(SuppressionMods.SilentHead)) });
            patchAllConcord = backend.ApplyComposed(patchAllTarget, new[] { HeadOf(nameof(SuppressionMods.SilentHead)) });
            Arm(decider, backend);
            SuppressionLog.Entries.Clear();

            List<MethodInfo> replacements = harmony.CreateClassProcessor(typeof(ClassProcessorPatch)).Patch();
            Assert.Same(OriginalStubs.For(classProcessorTarget), Assert.Single(replacements));

            harmony.PatchAll(typeof(SuppressionTests).Assembly);

            Assert.Contains((MethodBase)patchAllTarget, decider.Asked);
            Assert.Equal(99, PatchAllTarget.Compute());
            Assert.Equal(99, ClassProcessorTarget.Compute());
            Assert.Equal(new[] { "patch-all", "class-processor" }, SuppressionLog.Entries);
            Assert.Single(PatchProcessor.GetPatchInfo(patchAllTarget).Prefixes);
        }
        finally
        {
            Disarm(harmony, ModId, null);
            classProcessorConcord?.Dispose();
            patchAllConcord?.Dispose();
        }
    }

    [Fact]
    public void Unpatch_AfterConcordStepsAside_RebuildsWithOnlyTheRemainingPatch()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(UnpatchTarget), nameof(UnpatchTarget.Compute));
        HarmonyLib.Harmony first = new HarmonyLib.Harmony(ModId);
        HarmonyLib.Harmony second = new HarmonyLib.Harmony(OtherModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(SuppressionMods.SilentHead)) });
            Arm(decider, backend);
            SuppressionLog.Entries.Clear();

            first.Patch(target, prefix: MethodOf(nameof(SuppressionMods.UnpatchPrefix)));
            second.Patch(target, postfix: MethodOf(nameof(SuppressionMods.UnpatchPostfix)));

            Assert.Equal(102, UnpatchTarget.Compute());
            Assert.Equal(new[] { "unpatch-prefix", "unpatch-postfix" }, SuppressionLog.Entries);

            decider.Release(target);
            first.Unpatch(target, HarmonyPatchType.Prefix, ModId);

            SuppressionLog.Entries.Clear();
            Assert.Equal(102, UnpatchTarget.Compute());
            Assert.Equal(new[] { "unpatch-postfix" }, SuppressionLog.Entries);
            Assert.Empty(PatchProcessor.GetPatchInfo(target).Prefixes);
            Assert.Equal(OtherModId, Assert.Single(PatchProcessor.GetPatchInfo(target).Postfixes).owner);
        }
        finally
        {
            UpdateWrapperHook.DetachObserver();
            SuppressionGate.ClearProbes();
            concord?.Dispose();
            TestUnpatch.Own(first, ModId);
            TestUnpatch.Own(second, OtherModId);
        }
    }

    private static bool SuppressionIsSafeHere()
    {
        return HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()) == null;
    }

    private static MethodInfo Fresh(Type owner, string name)
    {
        MethodInfo target = owner.GetMethod(name) ?? throw new InvalidOperationException(owner.Name + "." + name + " not found");
        Assert.Null(PatchProcessor.GetPatchInfo(target));
        return target;
    }

    private static void Arm(Decider decider, MonoModDetourBackend backend)
    {
        SuppressionGate.WireProfiler(() => false);
        SuppressionGate.WireCapability(() => null);
        Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));
    }

    private static void Disarm(HarmonyLib.Harmony harmony, string id, IDetourHandle? concord)
    {
        UpdateWrapperHook.DetachObserver();
        SuppressionGate.ClearProbes();
        concord?.Dispose();
        TestUnpatch.Own(harmony, id);
    }

    private static HarmonyMethod MethodOf(string name)
    {
        return new HarmonyMethod(typeof(SuppressionMods).GetMethod(name));
    }

    private static Injection HeadOf(string name)
    {
        MethodInfo method = typeof(SuppressionMods).GetMethod(name) ?? throw new InvalidOperationException(nameof(SuppressionMods) + "." + name + " not found");
        return new Injection(method, new InjectAt.Head(), "test.suppression.concord", 0);
    }

    private sealed class Decider : IForeignPatchObserver, IForeignPatchDecider
    {
        private readonly HashSet<MethodBase> owned = new HashSet<MethodBase>();

        public List<MethodBase> Asked { get; } = new List<MethodBase>();

        public List<string> Lines { get; } = new List<string>();

        public void Own(MethodBase target)
        {
            owned.Add(target);
        }

        public void Release(MethodBase target)
        {
            owned.Remove(target);
        }

        public void Record(string line)
        {
            Lines.Add(line);
        }

        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
        }

        public bool TrySuppressForeignRebuild(MethodBase target, object hostPatchState)
        {
            Asked.Add(target);
            return owned.Contains(target);
        }
    }

    public static class SuppressionLog
    {
        public static List<string> Entries { get; } = new List<string>();
    }

    public static class DirectTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 41;
        }
    }

    public static class StubHandbackTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 42;
        }
    }

    public static class PatchAllTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 43;
        }
    }

    public static class ClassProcessorTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 45;
        }
    }

    public static class UnpatchTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 44;
        }
    }

    [HarmonyPatch(typeof(PatchAllTarget), nameof(PatchAllTarget.Compute))]
    public static class PatchAllPatch
    {
        public static bool Prefix(ref int __result)
        {
            SuppressionLog.Entries.Add("patch-all");
            __result = 99;
            return false;
        }
    }

    [HarmonyPatch(typeof(ClassProcessorTarget), nameof(ClassProcessorTarget.Compute))]
    public static class ClassProcessorPatch
    {
        public static bool Prefix(ref int __result)
        {
            SuppressionLog.Entries.Add("class-processor");
            __result = 99;
            return false;
        }
    }

    public static class SuppressionMods
    {
        public static void SilentHead(ControlHandle<int> ch)
        {
        }

        public static bool DirectPrefix(ref int __result)
        {
            SuppressionLog.Entries.Add("direct");
            __result = 99;
            return false;
        }

        public static bool StubHandbackPrefix(ref int __result)
        {
            SuppressionLog.Entries.Add("stub-handback");
            __result = 99;
            return false;
        }

        public static void UnpatchPrefix()
        {
            SuppressionLog.Entries.Add("unpatch-prefix");
        }

        public static void UnpatchPostfix(ref int __result)
        {
            SuppressionLog.Entries.Add("unpatch-postfix");
            __result = 102;
        }
    }
}
