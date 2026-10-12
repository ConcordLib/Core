using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

[Collection("HarmonySerial")]
public sealed class SuppressedComposeTests
{
    private const string ModId = "test.suppressedcompose.mod";

    private const string OtherModId = "test.suppressedcompose.other";

    private const string ConcordOwner = "test.suppressedcompose.concord";

    [Fact]
    public void SuppressedTarget_KeepsTheModsHarmonyPrefix_AndMatchesLiveHarmony()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo live = Fresh(typeof(ParityLiveTarget), nameof(ParityLiveTarget.Compute));
        MethodInfo composed = Fresh(typeof(ParityComposedTarget), nameof(ParityComposedTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(composed);

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(composed, new[] { HeadOf(nameof(ComposeMods.SilentHead)) });
            Assert.True(backend.OwnsBody(composed));
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            harmony.Patch(live, prefix: MethodOf(nameof(ComposeMods.ParityPrefix)));
            ComposeLog.Entries.Clear();
            int liveResult = ParityLiveTarget.Compute();
            string[] liveEntries = ComposeLog.Entries.ToArray();

            harmony.Patch(composed, prefix: MethodOf(nameof(ComposeMods.ParityPrefix)));
            ComposeLog.Entries.Clear();
            int composedResult = ParityComposedTarget.Compute();
            string[] composedEntries = ComposeLog.Entries.ToArray();

            Assert.Equal(new[] { "prefix", "body" }, liveEntries);
            Assert.Equal(liveEntries, composedEntries);
            Assert.Equal(liveResult, composedResult);
            Assert.Equal(new[] { (MethodBase)composed }, decider.Asked);
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void SuppressedTarget_RunsTheHarmonyPrefixOutsideTheConcordHead()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(LayeredTarget), nameof(LayeredTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ComposeMods.LayeredHead)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.LayeredPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(20, LayeredTarget.Compute());
            Assert.Equal(new[] { "harmony", "concord", "body" }, ComposeLog.Entries);
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void RefusedPatch_LeavesTheRebuildToHarmony_AndTheModsPatchStillRuns()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(RefusedTarget), nameof(RefusedTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ComposeMods.RefusedHead)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            MethodInfo handedBack = harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.RefusedPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(99, RefusedTarget.Compute(1));
            Assert.Equal(new[] { "harmony" }, ComposeLog.Entries);
            Assert.NotSame(OriginalStubs.For(target), handedBack);

            string refused = Assert.Single(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.Contains("RefusedTarget.Compute", refused);
            Assert.Contains(ModId, refused);
            Assert.Contains(
                PrefixMapping.Validate(target, Method(typeof(ComposeMods), nameof(ComposeMods.RefusedPrefix)))
                    ?? throw new InvalidOperationException("RefusedPrefix must fail prefix validation"),
                refused);
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void RefusedPatch_ThenOwnedPatch_NeverAsksTheDeciderOnceHarmonyHasBuiltAWrapper()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(HarmonyBuiltTarget), nameof(HarmonyBuiltTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        AttemptCountingDecider decider = new AttemptCountingDecider();

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ComposeMods.SilentHead)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.FirstHarmonyBuiltPrefix)));
            Assert.Single(decider.Attempts);

            decider.Own(target);
            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.SecondHarmonyBuiltPrefix)));

            Assert.Single(decider.Attempts);
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void UnownedBody_LeavesTheRebuildToHarmony_AndTheModsPatchStillRuns()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(UnownedTarget), nameof(UnownedTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        UnownedBodyBackend backend = new UnownedBodyBackend();
        Decider decider = new Decider();
        decider.Own(target);

        try
        {
            WireGate();
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            MethodInfo handedBack = harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.UnownedPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(77, UnownedTarget.Compute(1));
            Assert.Contains("harmony", ComposeLog.Entries);
            Assert.NotSame(OriginalStubs.For(target), handedBack);

            string unowned = Assert.Single(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeUnownedMarker, StringComparison.Ordinal));
            Assert.Contains("UnownedTarget.Compute", unowned);
        }
        finally
        {
            UpdateWrapperHook.DetachObserver();
            SuppressionGate.ClearProbes();
            TestUnpatch.Own(harmony, ModId);
        }
    }

    [Fact]
    public void UnpatchOnASuppressedTarget_Recomposes_AndTheRemainingPatchStillRuns()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(RecomposeTarget), nameof(RecomposeTarget.Compute));
        HarmonyLib.Harmony first = new HarmonyLib.Harmony(ModId);
        HarmonyLib.Harmony second = new HarmonyLib.Harmony(OtherModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ComposeMods.SilentHead)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            first.Patch(target, prefix: MethodOf(nameof(ComposeMods.RecomposePrefix)));
            second.Patch(target, postfix: MethodOf(nameof(ComposeMods.RecomposePostfix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(55, RecomposeTarget.Compute());
            Assert.Equal(new[] { "a", "body", "b" }, ComposeLog.Entries);

            first.Unpatch(target, HarmonyPatchType.Prefix, ModId);

            ComposeLog.Entries.Clear();
            Assert.Equal(55, RecomposeTarget.Compute());
            Assert.Equal(new[] { "body", "b" }, ComposeLog.Entries);
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

    [Fact]
    public void ComposingASuppressedTarget_RunsInsideTheSuppressionScope_AndAsksTheDeciderOnce()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(ReentryTarget), nameof(ReentryTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        DepthProbeBackend backend = new DepthProbeBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ComposeMods.SilentHead)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));
            backend.DepthsSeen.Clear();

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.ReentryPrefix)));

            Assert.NotEmpty(backend.DepthsSeen);
            Assert.DoesNotContain(0, backend.DepthsSeen);
            Assert.Equal(0, DepthProbeBackend.CurrentDepth());
            Assert.Equal(new[] { (MethodBase)target }, decider.Asked);

            ComposeLog.Entries.Clear();
            Assert.Equal(40, ReentryTarget.Compute());
            Assert.Equal(new[] { "harmony", "body" }, ComposeLog.Entries);
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    private static bool SuppressionIsSafeHere()
    {
        return HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()) == null;
    }

    private static MethodInfo Fresh(Type owner, string name)
    {
        MethodInfo target = Method(owner, name);
        Assert.Null(PatchProcessor.GetPatchInfo(target));
        return target;
    }

    private static void WireGate()
    {
        SuppressionGate.WireProfiler(() => false);
        SuppressionGate.WireCapability(() => null);
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
        return new HarmonyMethod(Method(typeof(ComposeMods), name));
    }

    private static Injection HeadOf(string name)
    {
        return new Injection(Method(typeof(ComposeMods), name), new InjectAt.Head(), ConcordOwner, 0);
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private sealed class AttemptCountingDecider : IForeignPatchObserver, IForeignPatchDecider
    {
        private readonly HashSet<MethodBase> owned = new HashSet<MethodBase>();

        public List<MethodBase> Attempts { get; } = new List<MethodBase>();

        public List<string> Lines { get; } = new List<string>();

        public void Own(MethodBase target)
        {
            owned.Add(target);
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
            Attempts.Add(target);
            return owned.Contains(target);
        }
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

        public void Record(string line)
        {
            Lines.Add(line);
        }

        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
        }

        public bool TrySuppressForeignRebuild(MethodBase target, object hostPatchState)
        {
            if (!owned.Contains(target))
            {
                return false;
            }

            Asked.Add(target);
            return true;
        }
    }

    private sealed class DepthProbeBackend : IDetourBackend, IOwnsBodyQuery
    {
        private static readonly FieldInfo DepthField =
            typeof(UpdateWrapperHook).GetField("suppressDepth", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("UpdateWrapperHook.suppressDepth not found");

        private readonly MonoModDetourBackend inner = new MonoModDetourBackend();

        public List<int> DepthsSeen { get; } = new List<int>();

        public static int CurrentDepth()
        {
            Assert.NotNull(DepthField);
            return (int)(DepthField.GetValue(null) ?? throw new InvalidOperationException("UpdateWrapperHook.suppressDepth read as null"));
        }

        public bool OwnsBody(MethodBase target)
        {
            return inner.OwnsBody(target);
        }

        public IDetourHandle Apply(MethodBase original, MethodInfo replacement)
        {
            return inner.Apply(original, replacement);
        }

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added)
        {
            DepthsSeen.Add(CurrentDepth());
            return inner.ApplyComposed(target, added);
        }
    }

    private sealed class UnownedBodyBackend : IDetourBackend, IOwnsBodyQuery
    {
        public bool OwnsBody(MethodBase target)
        {
            return false;
        }

        public IDetourHandle Apply(MethodBase original, MethodInfo replacement)
        {
            throw new NotSupportedException();
        }

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added)
        {
            throw new NotSupportedException();
        }
    }

    public static class ComposeLog
    {
        public static List<string> Entries { get; } = new List<string>();
    }

    public static class HarmonyBuiltTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 50;
        }
    }

    public static class ParityLiveTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 10;
        }
    }

    public static class ParityComposedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 10;
        }
    }

    public static class LayeredTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 20;
        }
    }

    public static class RefusedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            ComposeLog.Entries.Add("body");
            return seed + 1;
        }
    }

    public static class RecomposeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 30;
        }
    }

    public static class UnownedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            ComposeLog.Entries.Add("body");
            return seed + 1;
        }
    }

    public static class ReentryTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 40;
        }
    }

    public static class ComposeMods
    {
        public static void SilentHead(ControlHandle<int> ch)
        {
        }

        public static void FirstHarmonyBuiltPrefix()
        {
            ComposeLog.Entries.Add("first");
        }

        public static void SecondHarmonyBuiltPrefix()
        {
            ComposeLog.Entries.Add("second");
        }

        public static void ParityPrefix()
        {
            ComposeLog.Entries.Add("prefix");
        }

        public static void LayeredHead(ControlHandle<int> ch)
        {
            ComposeLog.Entries.Add("concord");
        }

        public static void LayeredPrefix()
        {
            ComposeLog.Entries.Add("harmony");
        }

        public static void RefusedHead(ControlHandle<int> ch)
        {
            ComposeLog.Entries.Add("concord");
        }

        public static bool RefusedPrefix(int seed, ref int __result)
        {
            ComposeLog.Entries.Add("harmony");
            __result = 99;
            return false;
        }

        public static void RecomposePrefix()
        {
            ComposeLog.Entries.Add("a");
        }

        public static void RecomposePostfix(ref int __result)
        {
            ComposeLog.Entries.Add("b");
            __result = 55;
        }

        public static void ReentryPrefix()
        {
            ComposeLog.Entries.Add("harmony");
        }

        public static bool UnownedPrefix(int seed, ref int __result)
        {
            ComposeLog.Entries.Add("harmony");
            __result = 77;
            return false;
        }
    }
}
