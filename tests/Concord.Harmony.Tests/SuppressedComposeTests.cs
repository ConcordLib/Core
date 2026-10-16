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
    public void PostfixTakingResultAndATargetArgument_IsComposed_NotRefused()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(ResultArgTarget), nameof(ResultArgTarget.Compute));
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

            MethodInfo handedBack = harmony.Patch(target, postfix: MethodOf(nameof(ComposeMods.ResultArgPostfix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(15, ResultArgTarget.Compute(5));
            Assert.Equal(new[] { "concord", "body", "harmony" }, ComposeLog.Entries);
            Assert.Same(OriginalStubs.For(target), handedBack);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PostfixWritingResultAfterAnEarlyReturn_KeepsTheValueItWroteBeforeReturning()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(EarlyReturnTarget), nameof(EarlyReturnTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ComposeMods.SilentHead)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            harmony.Patch(target, postfix: MethodOf(nameof(ComposeMods.EarlyReturnPostfix)));

            Assert.Equal(0, EarlyReturnTarget.Compute(0));
            Assert.Equal(-1, EarlyReturnTarget.Compute(3));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PostfixTakingPrivateFields_IsComposed_AndAByRefFieldWriteSticks()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(FieldArgTarget), nameof(FieldArgTarget.Compute));
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

            harmony.Patch(target, postfix: MethodOf(nameof(ComposeMods.FieldArgPostfix)));

            FieldArgTarget instance = new FieldArgTarget();
            ComposeLog.Entries.Clear();
            Assert.Equal(20, instance.Compute(5));
            Assert.Equal(new[] { "concord", "body", "harmony:2" }, ComposeLog.Entries);
            Assert.Equal(30, instance.Compute(5));
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PatchesTakingInstanceAndATargetArgument_AreComposed_NotRefused()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(InstanceArgTarget), nameof(InstanceArgTarget.Compute));
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

            harmony.Patch(
                target,
                prefix: MethodOf(nameof(ComposeMods.InstanceArgPrefix)),
                postfix: MethodOf(nameof(ComposeMods.InstanceArgPostfix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(24, new InstanceArgTarget().Compute(5));
            Assert.Equal(new[] { "pre:12", "concord", "body", "harmony" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PassThroughPostfixReturningTheTargetsType_IsComposed_NotRefused()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(PassThroughTarget), nameof(PassThroughTarget.Values));
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

            harmony.Patch(target, postfix: MethodOf(nameof(ComposeMods.PassThroughPostfix)));

            ComposeLog.Entries.Clear();
            List<string> seen = new List<string>(new PassThroughTarget().Values());
            Assert.Equal(new[] { "a", "b", "extra" }, seen);
            Assert.Equal(new[] { "concord", "body", "harmony" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PostfixWritingAByValueTargetArgument_IsComposed_AndTheWriteStaysInTheCopy()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(ScratchArgTarget), nameof(ScratchArgTarget.Compute));
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

            harmony.Patch(target, postfix: MethodOf(nameof(ComposeMods.ScratchArgPostfix)));
            harmony.Patch(target, postfix: MethodOf(nameof(ComposeMods.ScratchArgReader)));

            ComposeLog.Entries.Clear();
            Assert.Equal(55, new ScratchArgTarget().Compute(5));
            Assert.Equal(new[] { "concord", "body:5", "harmony:50", "reader:5" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PrefixTakingResultAlongsideATargetArgument_CancelsWithTheResultItWrote()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(PrefixResultTarget), nameof(PrefixResultTarget.Compute));
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

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.ReadResultPrefix)));
            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.CancelWithResultPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(105, new PrefixResultTarget().Compute(5));
            Assert.Equal(new[] { "readresult:5:0", "cancelresult:5", "concord" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PrefixWritingAByValueResult_WritesOnlyItsOwnCopy_AndIsNotRefused()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(ByValueResultTarget), nameof(ByValueResultTarget.Compute));
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

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.WritesByValueResultPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(0, new ByValueResultTarget().Compute(5));
            Assert.Equal(new[] { "byvalueresult:12", "concord" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void BoolPrefixOnTheCopyRoute_ReturningFalse_CancelsTheBody_AndSkipsOnlyTheAffectingShimPrefix()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(BoolPrefixTarget), nameof(BoolPrefixTarget.Compute));
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

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.CancelCopyPrefix)));
            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.AffectingShimPrefix)));
            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.PlainShimPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(0, new BoolPrefixTarget().Compute(5));
            Assert.Equal(new[] { "cancelcopy:5", "plainshim", "concord" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void ShimPrefixReturningFalse_SkipsTheLaterAffectingCopyRoutePrefix()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(CrossRouteTarget), nameof(CrossRouteTarget.Compute));
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

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.CancelShimPrefix)));
            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.AffectingCopyPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(0, new CrossRouteTarget().Compute("hi"));
            Assert.Equal(new[] { "cancelshim", "concord" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void AffectingCopyRoutePrefix_WithNothingCancelling_RunsAndLetsTheBodyRun()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(GuardedCopyTarget), nameof(GuardedCopyTarget.Compute));
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

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.AffectingCopyPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(2, new GuardedCopyTarget().Compute("hi"));
            Assert.Equal(new[] { "affectingcopy:hi", "concord", "body:hi" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void RefAdaptedPrefix_WritingAByValueTargetArgument_IsSeenByTheBody()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(RefAdaptedTarget), nameof(RefAdaptedTarget.Compute));
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

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.RefAdaptedPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(9, new RefAdaptedTarget().Compute("hi"));
            Assert.Equal(new[] { "refadapt:hi", "concord", "body:rewritten" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void RefAdaptedPrefix_WritingAByValueStructArgument_IsSeenByTheBody()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(RefAdaptedStructTarget), nameof(RefAdaptedStructTarget.Compute));
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

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.RefAdaptedStructPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(30, new RefAdaptedStructTarget().Compute(5));
            Assert.Equal(new[] { "refadaptstruct:5", "concord", "body:15" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void FinalizerTakingState_ReadsWhatThePrefixWrote()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(FinalizerStateReturnTarget), nameof(FinalizerStateReturnTarget.Compute));
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

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(FinalizerStateMods).GetMethod(nameof(FinalizerStateMods.Prefix))!),
                finalizer: new HarmonyMethod(typeof(FinalizerStateMods).GetMethod(nameof(FinalizerStateMods.Finalizer))!));

            ComposeLog.Entries.Clear();
            Assert.Equal(10, new FinalizerStateReturnTarget().Compute(5));
            Assert.Equal(new[] { "concord", "body:5", "fin:scoped:5:clean" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void FinalizerTakingState_ReadsItOnTheThrowPath()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(FinalizerStateThrowTarget), nameof(FinalizerStateThrowTarget.Compute));
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

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(FinalizerStateMods).GetMethod(nameof(FinalizerStateMods.Prefix))!),
                finalizer: new HarmonyMethod(typeof(FinalizerStateMods).GetMethod(nameof(FinalizerStateMods.Finalizer))!));

            ComposeLog.Entries.Clear();
            Assert.Throws<InvalidOperationException>(() => new FinalizerStateThrowTarget().Compute(-7));
            Assert.Equal(new[] { "concord", "body:-7", "fin:scoped:-7:InvalidOperationException" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void FinalizerTakingState_WithNoPrefixToWriteIt_ReadsTheDefault()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(FinalizerStateOnlyTarget), nameof(FinalizerStateOnlyTarget.Compute));
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

            harmony.Patch(
                target,
                finalizer: new HarmonyMethod(typeof(FinalizerStateOnlyMods).GetMethod(nameof(FinalizerStateOnlyMods.Finalizer))!));

            ComposeLog.Entries.Clear();
            Assert.Equal(12, new FinalizerStateOnlyTarget().Compute(6));
            Assert.Equal(new[] { "concord", "body:6", "fin:<null>" }, ComposeLog.Entries);
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PostfixWideningAnArgument_AndReadingAnOutArgument_Composes()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(WideningTarget), nameof(WideningTarget.Compute));
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

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(WideningMods).GetMethod(nameof(WideningMods.Postfix))!));

            ComposeLog.Entries.Clear();
            Assert.Equal(4, new WideningTarget().Compute("abcd", out int made));
            Assert.Equal(4, made);
            Assert.Equal(new[] { "concord", "body:abcd", "post:abcd:4" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PostfixTakingInstanceOnAStaticTarget_GetsNull()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(StaticInstanceShimTarget), nameof(StaticInstanceShimTarget.Compute));
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

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(StaticInstanceMods).GetMethod(nameof(StaticInstanceMods.ShimPostfix))!));

            ComposeLog.Entries.Clear();
            Assert.Equal(10, StaticInstanceShimTarget.Compute(5));
            Assert.Equal(new[] { "concord", "body:5", "post:<null>" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PostfixTakingInstanceAndAnArgumentOnAStaticTarget_GetsNull()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(StaticInstanceCopyTarget), nameof(StaticInstanceCopyTarget.Compute));
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

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(StaticInstanceMods).GetMethod(nameof(StaticInstanceMods.CopyPostfix))!));

            ComposeLog.Entries.Clear();
            Assert.Equal(10, StaticInstanceCopyTarget.Compute(5));
            Assert.Equal(new[] { "concord", "body:5", "post:<null>:5" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PrefixTakingAByRefTargetArgumentByName_BindsTheTargetArgument()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(ByRefNamedArgTarget), nameof(ByRefNamedArgTarget.Compute));
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

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(ByRefNamedArgMods).GetMethod(nameof(ByRefNamedArgMods.Prefix))!));

            ComposeLog.Entries.Clear();
            ByRefNamedArgTarget instance = new ByRefNamedArgTarget();
            bool alsoRemove = true;
            Assert.False(instance.Compute(out alsoRemove));
            Assert.False(alsoRemove);
            Assert.Equal(new[] { "pre", "concord" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PostfixTakingAPositionalArgument_GetsTheTargetArgumentAtThatIndex()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(PositionalTarget), nameof(PositionalTarget.Compute));
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

            harmony.Patch(target, postfix: MethodOf(nameof(ComposeMods.PositionalPostfix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(14, new PositionalTarget().Compute(7));
            Assert.Equal(new[] { "concord", "body:7", "positional:7" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void PostfixTakingTheOriginalMethod_GetsTheTargetItPatched()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(OriginalMethodTarget), nameof(OriginalMethodTarget.Compute));
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

            harmony.Patch(target, postfix: MethodOf(nameof(ComposeMods.OriginalMethodPostfix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(10, new OriginalMethodTarget().Compute(5));
            Assert.Equal(new[] { "concord", "body:5", "original:Compute:OriginalMethodTarget:5" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
        }
        finally
        {
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void StatePrefix_HandsItsStateToTheStatePostfix_ThroughOneSharedSlot()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(StateTarget), nameof(StateTarget.Compute));
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

            harmony.Patch(
                target,
                prefix: MethodOf(nameof(ComposeMods.StatePrefix)),
                postfix: MethodOf(nameof(ComposeMods.StatePostfix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(16, new StateTarget().Compute(5));
            Assert.Equal(new[] { "stateprefix:6", "concord", "body:5", "statepostfix:6" }, ComposeLog.Entries);
            Assert.True(backend.OwnsBody(target));
            Assert.DoesNotContain(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
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
    public void ReinstallingTheHook_LeavesASuppressedTargetWithConcord()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(ReseedTarget), nameof(ReseedTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ComposeMods.SilentHead)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.FirstHarmonyBuiltPrefix)));
            Assert.True(SuppressedRebuilds.Holds(target));

            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            MethodInfo handedBack = harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.SecondHarmonyBuiltPrefix)));

            Assert.Same(OriginalStubs.For(target), handedBack);
            Assert.True(SuppressedRebuilds.Holds(target));
            Assert.Equal(2, decider.Asked.Count);
            Assert.DoesNotContain(decider.Lines, line => line.IndexOf("Harmony already built a wrapper for", StringComparison.Ordinal) >= 0);
        }
        finally
        {
            SuppressedRebuilds.Forget(target);
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void TargetHarmonyDetouredBeforeTheHook_IsLeftToHarmony()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(PreHookTarget), nameof(PreHookTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        AttemptCountingDecider decider = new AttemptCountingDecider();
        decider.Own(target);

        try
        {
            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.FirstHarmonyBuiltPrefix)));

            WireGate();
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.SecondHarmonyBuiltPrefix)));

            Assert.Empty(decider.Attempts);
            Assert.False(SuppressedRebuilds.Holds(target));
            Assert.False(backend.OwnsBody(target));
            Assert.Contains(decider.Lines, line => line.IndexOf("Harmony already built a wrapper for", StringComparison.Ordinal) >= 0 &&
                line.IndexOf("PreHookTarget.Compute", StringComparison.Ordinal) >= 0);

            ComposeLog.Entries.Clear();
            Assert.Equal(60, PreHookTarget.Compute());
            Assert.Equal(new[] { "first", "second", "body" }, ComposeLog.Entries);
        }
        finally
        {
            Disarm(harmony, ModId, null);
        }
    }

    [Fact]
    public void NeverDetouredTarget_IsTakenOverByConcord_AndTheModsPatchStillRuns()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(UnownedTarget), nameof(UnownedTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        try
        {
            WireGate();
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));
            Assert.False(backend.OwnsBody(target));

            MethodInfo handedBack = harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.ReentryPrefix)));

            ComposeLog.Entries.Clear();
            Assert.Equal(2, UnownedTarget.Compute(1));
            Assert.Equal(new[] { "harmony", "body" }, ComposeLog.Entries);
            Assert.Same(OriginalStubs.For(target), handedBack);
            Assert.True(backend.OwnsBody(target));
            Assert.Equal(new[] { (MethodBase)target }, decider.Asked);
        }
        finally
        {
            Disarm(harmony, ModId, null);
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

    [Fact]
    public void SuppressedTarget_IsHeldAgainstTheForeignRebuild()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(MarkedTarget), nameof(MarkedTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ComposeMods.SilentHead)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));
            Assert.False(SuppressedRebuilds.Holds(target));

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.ReentryPrefix)));

            Assert.True(SuppressedRebuilds.Holds(target));
        }
        finally
        {
            SuppressedRebuilds.Forget(target);
            Disarm(harmony, ModId, concord);
        }
    }

    [Fact]
    public void RefusedPatch_LeavesTheTargetUnheld()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(UnmarkedTarget), nameof(UnmarkedTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        AttemptCountingDecider decider = new AttemptCountingDecider();

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ComposeMods.SilentHead)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            harmony.Patch(target, prefix: MethodOf(nameof(ComposeMods.ReentryPrefix)));

            Assert.Single(decider.Attempts);
            Assert.False(SuppressedRebuilds.Holds(target));
        }
        finally
        {
            SuppressedRebuilds.Forget(target);
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

    public static class ReseedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 70;
        }
    }

    public static class PreHookTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 60;
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

    public static class MarkedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 41;
        }
    }

    public static class UnmarkedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            ComposeLog.Entries.Add("body");
            return 42;
        }
    }

    public sealed class PassThroughTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IEnumerable<string> Values()
        {
            ComposeLog.Entries.Add("body");
            return new[] { "a", "b" };
        }
    }

    public sealed class FinalizerStateReturnTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public sealed class FinalizerStateThrowTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            if (seed < 0) {
                throw new InvalidOperationException("boom");
            }

            return seed;
        }
    }

    public sealed class FinalizerStateOnlyTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public static class FinalizerStateMods
    {
        public static void Prefix(int seed, ref string __state)
        {
            __state = "scoped:" + seed;
        }

        public static void Finalizer(string __state, Exception? __exception)
        {
            ComposeLog.Entries.Add("fin:" + (__state ?? "<null>") + ":" + (__exception == null ? "clean" : __exception.GetType().Name));
        }
    }

    public static class FinalizerStateOnlyMods
    {
        public static void Finalizer(string __state)
        {
            ComposeLog.Entries.Add("fin:" + (__state ?? "<null>"));
        }
    }

    public sealed class WideningTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(string label, out int made)
        {
            ComposeLog.Entries.Add("body:" + label);
            made = label.Length;
            return made;
        }
    }

    public static class WideningMods
    {
        public static void Postfix(object label, int made)
        {
            ComposeLog.Entries.Add("post:" + label + ":" + made);
        }
    }

    public sealed class ByRefNamedArgTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool Compute(out bool alsoRemove)
        {
            alsoRemove = true;
            ComposeLog.Entries.Add("body");
            return true;
        }
    }

    public static class ByRefNamedArgMods
    {
        public static bool Prefix(ByRefNamedArgTarget __instance, ref bool __result, ref bool alsoRemove)
        {
            __result = false;
            alsoRemove = false;
            ComposeLog.Entries.Add("pre");
            return false;
        }
    }

    public static class StaticInstanceShimTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public static class StaticInstanceCopyTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public static class StaticInstanceMods
    {
        public static void ShimPostfix(object __instance)
        {
            ComposeLog.Entries.Add("post:" + (__instance == null ? "<null>" : "<set>"));
        }

        public static void CopyPostfix(object __instance, int seed)
        {
            ComposeLog.Entries.Add("post:" + (__instance == null ? "<null>" : "<set>") + ":" + seed);
        }
    }

    public sealed class PositionalTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public sealed class OriginalMethodTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public sealed class ByValueResultTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public sealed class PrefixResultTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public sealed class BoolPrefixTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public sealed class CrossRouteTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(string text)
        {
            ComposeLog.Entries.Add("body:" + text);
            return text.Length;
        }
    }

    public sealed class GuardedCopyTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(string text)
        {
            ComposeLog.Entries.Add("body:" + text);
            return text.Length;
        }
    }

    public sealed class RefAdaptedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(string text)
        {
            ComposeLog.Entries.Add("body:" + text);
            return text.Length;
        }
    }

    public sealed class RefAdaptedStructTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public sealed class StateTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed * 2;
        }
    }

    public sealed class ScratchArgTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body:" + seed);
            return seed;
        }
    }

    public sealed class InstanceArgTarget
    {
        public int Bonus = 7;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body");
            return seed + Bonus;
        }
    }

    public sealed class FieldArgTarget
    {
        private static int offset = 2;

        private int count = 4;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int seed)
        {
            ComposeLog.Entries.Add("body");
            return seed + count + offset;
        }
    }

    public static class ResultArgTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            ComposeLog.Entries.Add("body");
            return seed * 2;
        }
    }

    public static class EarlyReturnTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            ComposeLog.Entries.Add("body");
            return seed * 2;
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

        public static bool RefusedPrefix(object[] __args, ref int __result)
        {
            GC.KeepAlive(__args);
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

        public static void ResultArgPostfix(int seed, ref int __result)
        {
            ComposeLog.Entries.Add("harmony");
            __result += seed;
        }

        public static void FieldArgPostfix(int seed, ref int ___count, int ___offset, ref int __result)
        {
            ComposeLog.Entries.Add("harmony:" + ___offset);
            ___count += seed;
            __result += ___count;
        }

        public static IEnumerable<string> PassThroughPostfix(IEnumerable<string> values)
        {
            ComposeLog.Entries.Add("harmony");
            foreach (string value in values)
            {
                yield return value;
            }

            yield return "extra";
        }

        public static void ScratchArgPostfix(int seed, ref int __result)
        {
            seed *= 10;
            ComposeLog.Entries.Add("harmony:" + seed);
            __result += seed;
        }

        public static void ScratchArgReader(int seed)
        {
            ComposeLog.Entries.Add("reader:" + seed);
        }

        public static void PositionalPostfix(int __0)
        {
            ComposeLog.Entries.Add("positional:" + __0);
        }

        public static void OriginalMethodPostfix(int seed, MethodBase __originalMethod)
        {
            ComposeLog.Entries.Add("original:" + __originalMethod.Name + ":" + __originalMethod.DeclaringType!.Name + ":" + seed);
        }

        public static bool WritesByValueResultPrefix(int seed, int __result)
        {
            __result = seed + 7;
            ComposeLog.Entries.Add("byvalueresult:" + __result);
            return false;
        }

        public static bool CancelWithResultPrefix(int seed, ref int __result)
        {
            ComposeLog.Entries.Add("cancelresult:" + seed);
            __result = seed + 100;
            return false;
        }

        public static bool ReadResultPrefix(int seed, int __result)
        {
            ComposeLog.Entries.Add("readresult:" + seed + ":" + __result);
            return true;
        }

        public static bool CancelCopyPrefix(int seed)
        {
            ComposeLog.Entries.Add("cancelcopy:" + seed);
            return false;
        }

        public static void AffectingShimPrefix(ref int __result)
        {
            ComposeLog.Entries.Add("affectingshim");
            __result = 99;
        }

        public static void PlainShimPrefix()
        {
            ComposeLog.Entries.Add("plainshim");
        }

        public static bool CancelShimPrefix()
        {
            ComposeLog.Entries.Add("cancelshim");
            return false;
        }

        public static void AffectingCopyPrefix(string text)
        {
            ComposeLog.Entries.Add("affectingcopy:" + text);
        }

        public static void StatePrefix(int seed, out int __state)
        {
            __state = seed + 1;
            ComposeLog.Entries.Add("stateprefix:" + __state);
        }

        public static void StatePostfix(ref int __result, int __state)
        {
            ComposeLog.Entries.Add("statepostfix:" + __state);
            __result += __state;
        }

        public static void RefAdaptedPrefix(ref string text)
        {
            ComposeLog.Entries.Add("refadapt:" + text);
            text = "rewritten";
        }

        public static void RefAdaptedStructPrefix(ref int seed)
        {
            ComposeLog.Entries.Add("refadaptstruct:" + seed);
            seed += 10;
        }

        public static void InstanceArgPrefix(int seed, InstanceArgTarget __instance)
        {
            ComposeLog.Entries.Add("pre:" + (__instance.Bonus + seed));
        }

        public static void InstanceArgPostfix(int seed, InstanceArgTarget __instance, ref int __result)
        {
            ComposeLog.Entries.Add("harmony");
            __result += __instance.Bonus + seed;
        }

        public static void EarlyReturnPostfix(int seed, ref int __result)
        {
            if (seed == 0)
            {
                return;
            }

            __result = -1;
        }
    }
}
