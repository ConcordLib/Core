using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public static class Contested {
    public static int Alpha() {
        return 1;
    }

    public static int Beta() {
        return 2;
    }

    public static int Gamma() {
        return 3;
    }
}

[Collection("AdapterStatics")]
public sealed class ForeignPatcherPolicyTests : IDisposable {
    private static readonly MethodBase Alpha = typeof(Contested).GetMethod(nameof(Contested.Alpha))!;

    private static readonly MethodBase Beta = typeof(Contested).GetMethod(nameof(Contested.Beta))!;

    private static readonly MethodBase Gamma = typeof(Contested).GetMethod(nameof(Contested.Gamma))!;

    private readonly List<string> logged = [];

    public ForeignPatcherPolicyTests() {
        ForeignPatcherPolicy.ResetForTests();
        ForeignPatcherPolicy.Log = logged.Add;
    }

    public void Dispose() {
        ForeignPatcherPolicy.ResetForTests();
        SuppressedRebuilds.Forget(Alpha);
        SuppressedRebuilds.Forget(Beta);
        SuppressedRebuilds.Forget(Gamma);
    }

    [Fact]
    public void AllowsATargetNoForeignPatcherHolds() {
        ForeignPatcherPolicy.Owners = static _ => null;

        Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(Alpha).Handling);
        Assert.False(ForeignPatcherPolicy.IsDisowned(Alpha));
    }

    [Fact]
    public void DisownsATargetHarmonyHeldFirst() {
        ForeignPatcherPolicy.Owners = static _ => new[] { "com.example.other" };

        ForeignVerdict verdict = ForeignPatcherPolicy.Judge(Alpha);

        Assert.Equal(ForeignHandling.Disown, verdict.Handling);
        Assert.Contains(ForeignPatcherPolicy.ContestedMarker, verdict.Message, StringComparison.Ordinal);
        Assert.Contains("com.example.other", verdict.Message, StringComparison.Ordinal);
        Assert.Contains("Concord has not patched it", verdict.Message, StringComparison.Ordinal);
        Assert.True(ForeignPatcherPolicy.IsDisowned(Alpha));
    }

    [Fact]
    public void DisownsATargetHarmonyTookAfterConcordPublished() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;

        Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(Alpha).Handling);
        held[Alpha] = ["com.example.late"];
        ForeignVerdict verdict = ForeignPatcherPolicy.Judge(Alpha);

        Assert.Equal(ForeignHandling.Disown, verdict.Handling);
        Assert.Contains("has withdrawn that body", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BothOrdersEndAtTheSameHandling() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;

        held[Alpha] = ["com.example.first"];
        ForeignHandling harmonyFirst = ForeignPatcherPolicy.Judge(Alpha).Handling;

        ForeignPatcherPolicy.Judge(Beta);
        held[Beta] = ["com.example.first"];
        ForeignHandling concordFirst = ForeignPatcherPolicy.Judge(Beta).Handling;

        Assert.Equal(ForeignHandling.Disown, harmonyFirst);
        Assert.Equal(harmonyFirst, concordFirst);
    }

    [Fact]
    public void NamesTheUnpatchedCaseWhenHarmonyOwnsNothingButHasTouchedTheMethod() {
        ForeignPatcherPolicy.Owners = static _ => [];

        ForeignVerdict verdict = ForeignPatcherPolicy.Judge(Alpha);

        Assert.Equal(ForeignHandling.Disown, verdict.Handling);
        Assert.Contains("patched it and then unpatched it", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StaysDisownedAfterHarmonyStopsReportingTheTarget() {
        ForeignPatcherPolicy.Owners = static _ => new[] { "com.example.other" };
        ForeignPatcherPolicy.Judge(Alpha);

        ForeignPatcherPolicy.Owners = static _ => null;

        ForeignVerdict repeat = ForeignPatcherPolicy.Judge(Alpha);

        Assert.Equal(ForeignHandling.Disown, repeat.Handling);
        Assert.Contains("Concord has not patched it", repeat.Message, StringComparison.Ordinal);
        Assert.True(ForeignPatcherPolicy.IsDisowned(Alpha));
    }

    [Fact]
    public void ContentionsReportsAPublishedTargetHarmonyTookLater() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;

        ForeignPatcherPolicy.Judge(Alpha);
        ForeignPatcherPolicy.Judge(Beta);
        held[Beta] = ["com.example.late"];

        IReadOnlyList<string> first = ForeignPatcherPolicy.Contentions();

        Assert.Single(first);
        Assert.Contains("Contested.Beta", first[0], StringComparison.Ordinal);
        Assert.True(ForeignPatcherPolicy.IsDisowned(Beta));
        Assert.False(ForeignPatcherPolicy.IsDisowned(Alpha));
        Assert.Empty(ForeignPatcherPolicy.Contentions());
    }

    [Fact]
    public void ContentionsSaysNothingAboutATargetConcordAlreadyReverted() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;
        WriterRegistry registry = new WriterRegistry(static _ => { }, new ConcurrentDictionary<MethodBase, string>());

        Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(Alpha).Handling);
        registry.Claim(Alpha, [1], ["mod.a"]);
        Assert.True(registry.Release(Alpha));
        held[Alpha] = ["com.example.late"];

        Assert.Empty(ForeignPatcherPolicy.Contentions());
    }

    [Fact]
    public void ContentionsSaysTheBodyIsStillInstalledBecauseThePollWithdrawsNothing() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;
        ForeignPatcherPolicy.Judge(Alpha);
        held[Alpha] = ["com.example.late"];

        IReadOnlyList<string> found = ForeignPatcherPolicy.Contentions();

        Assert.Single(found);
        Assert.DoesNotContain("has withdrawn that body", found[0], StringComparison.Ordinal);
        Assert.Contains("still installed", found[0], StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsATargetWhoseForeignRebuildConcordSuppressed() {
        ForeignPatcherPolicy.Owners = static _ => new[] { "com.example.suppressed" };
        SuppressedRebuilds.Mark(Alpha);

        Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(Alpha).Handling);
        Assert.False(ForeignPatcherPolicy.IsDisowned(Alpha));
    }

    [Fact]
    public void TakesBackATargetWithdrawnInTheWindowBeforeTheSuppressionWasMarked() {
        ForeignPatcherPolicy.Owners = static _ => new[] { "com.example.suppressed" };
        Assert.Equal(ForeignHandling.Disown, ForeignPatcherPolicy.Judge(Alpha).Handling);

        SuppressedRebuilds.Mark(Alpha);

        Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(Alpha).Handling);
        Assert.False(ForeignPatcherPolicy.IsDisowned(Alpha));
    }

    [Fact]
    public void ContentionsSaysNothingAboutATargetConcordSuppressed() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;
        ForeignPatcherPolicy.Judge(Alpha);
        held[Alpha] = ["com.example.suppressed"];
        SuppressedRebuilds.Mark(Alpha);

        Assert.Empty(ForeignPatcherPolicy.Contentions());
        Assert.False(ForeignPatcherPolicy.IsDisowned(Alpha));
    }

    [Fact]
    public void DisownsATargetOnceTheSuppressionIsGivenBack() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;
        ForeignPatcherPolicy.Judge(Alpha);
        held[Alpha] = ["com.example.suppressed"];
        SuppressedRebuilds.Mark(Alpha);
        Assert.Empty(ForeignPatcherPolicy.Contentions());

        SuppressedRebuilds.Forget(Alpha);

        Assert.Single(ForeignPatcherPolicy.Contentions());
        Assert.True(ForeignPatcherPolicy.IsDisowned(Alpha));
    }

    [Fact]
    public void ContentionsDoesNotRepeatATargetJudgeAlreadyReported() {
        ForeignPatcherPolicy.Owners = static _ => new[] { "com.example.other" };

        ForeignPatcherPolicy.Judge(Alpha);

        Assert.Empty(ForeignPatcherPolicy.Contentions());
    }

    [Fact]
    public void ContentionsIsEmptyWhenNoForeignPatcherIsPresent() {
        ForeignPatcherPolicy.Owners = static _ => null;

        ForeignPatcherPolicy.Judge(Alpha);

        Assert.Empty(ForeignPatcherPolicy.Contentions());
    }

    [Fact]
    public void HarmonyOwnersSeparatesNeverTouchedFromTouchedAndUnpatched() {
        ForeignPatcherPolicy.Owners = target => target == Alpha ? [] : null;

        Assert.Null(ForeignPatcherPolicy.HarmonyOwners(Beta));
        Assert.NotNull(ForeignPatcherPolicy.HarmonyOwners(Alpha));
        Assert.Empty(ForeignPatcherPolicy.HarmonyOwners(Alpha)!);
    }

    [Fact]
    public void AWedgedProbeIsVisibleSoAWiredRegistryNeedNotCallTheTargetClean() {
        Assert.False(ForeignPatcherPolicy.ProbeFailed);

        ForeignPatcherPolicy.Owners = static _ => throw new InvalidOperationException("harmony is mid-rebuild");
        ForeignPatcherPolicy.Judge(Alpha);

        Assert.True(ForeignPatcherPolicy.ProbeFailed);
        Assert.Null(ForeignPatcherPolicy.HarmonyOwners(Beta));
    }

    [Fact]
    public void DisownedHandleReportsItIsNotApplied() {
        IDetourHandle handle = ForeignPatcherPolicy.Disowned(Gamma);

        Assert.Same(Gamma, handle.Original);
        Assert.False(handle.IsApplied);
        handle.Dispose();
        Assert.False(handle.IsApplied);
    }

    [Fact]
    public void ADisownedHandleNamesTheSharedBodyNotTheInstantiationItWasAskedAbout() {
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        MethodBase version = typeof(Held<Version>).GetMethod(nameof(Held<Version>.Ping))!;

        Assert.Same(
            ForeignPatcherPolicy.Disowned(text).Original,
            ForeignPatcherPolicy.Disowned(version).Original);
    }

    [Fact]
    public void DisowningOneInstantiationDisownsAValueInstantiationTheDetourKeyKeepsApart() {
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        MethodBase number = typeof(Held<int>).GetMethod(nameof(Held<int>.Ping))!;
        Assert.NotEqual(MethodIdentity.SharedBodyKey(text), MethodIdentity.SharedBodyKey(number));
        ForeignPatcherPolicy.Owners = target => target == text ? ["harmony.somemod"] : null;

        Assert.Equal(ForeignHandling.Disown, ForeignPatcherPolicy.Judge(text).Handling);

        Assert.True(ForeignPatcherPolicy.IsDisowned(number));
        Assert.Equal(ForeignHandling.Disown, ForeignPatcherPolicy.Judge(number).Handling);
    }

    [Fact]
    public void ADisownedHandleNamesOneBodyForEveryInstantiationIncludingValueOnes() {
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        MethodBase number = typeof(Held<int>).GetMethod(nameof(Held<int>.Ping))!;

        Assert.Equal(
            ForeignPatcherPolicy.Disowned(text).Original,
            ForeignPatcherPolicy.Disowned(number).Original);
    }

    [Fact]
    public void TheHarmonyProbeIsAlwaysHandedTheExactInstantiationItWasAskedAbout() {
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        MethodBase version = typeof(Held<Version>).GetMethod(nameof(Held<Version>.Ping))!;
        List<MethodBase> asked = [];
        ForeignPatcherPolicy.Owners = target => {
            asked.Add(target);
            return null;
        };

        ForeignPatcherPolicy.HarmonyOwners(text);
        ForeignPatcherPolicy.HarmonyOwners(version);
        ForeignPatcherPolicy.Judge(version);

        Assert.Equal([text, version, version], asked);
    }

    [Fact]
    public void EveryTargetJudgedCleanIsOneTheNextTickAsksAbout() {
        MethodBase[] published = Eight();
        foreach (MethodBase target in published) {
            Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(target).Handling);
        }

        ForeignPatcherPolicy.Owners = static _ => new[] { "com.example.late" };

        Assert.Equal(published.Length, ForeignPatcherPolicy.Contentions().Count);
    }

    [Fact]
    public void ATickLooksForHarmonyOnceNotOncePerPublishedTarget() {
        foreach (MethodBase target in Eight()) {
            Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(target).Handling);
        }

        int before = ForeignPatcherPolicy.Scans;

        Assert.Empty(ForeignPatcherPolicy.Contentions());

        Assert.Equal(1, ForeignPatcherPolicy.Scans - before);
    }

    [Fact]
    public void ATickWithNothingPublishedLooksForHarmonyNotAtAll() {
        int before = ForeignPatcherPolicy.Scans;

        Assert.Empty(ForeignPatcherPolicy.Contentions());

        Assert.Equal(0, ForeignPatcherPolicy.Scans - before);
    }

    [Fact]
    public void AnAbsentHarmonyIsLookedForAgainEveryTickBecauseItCanLoadLater() {
        ForeignPatcherPolicy.Judge(Alpha);
        ForeignPatcherPolicy.Judge(Beta);
        int before = ForeignPatcherPolicy.Scans;

        for (int i = 0; i < 3; i++) {
            Assert.Empty(ForeignPatcherPolicy.Contentions());
        }

        Assert.Equal(3, ForeignPatcherPolicy.Scans - before);
    }

    [Fact]
    public void JudgeAndHarmonyOwnersLookForHarmonyOncePerCallWhichIsAlreadyTheFloor() {
        int before = ForeignPatcherPolicy.Scans;

        ForeignPatcherPolicy.Judge(Alpha);
        ForeignPatcherPolicy.HarmonyOwners(Beta);

        Assert.Equal(2, ForeignPatcherPolicy.Scans - before);
    }

    [Fact]
    public void AProbeThatThrowsIsReportedOnceAndThenStopsBeingAsked() {
        int asked = 0;
        ForeignPatcherPolicy.Owners = _ => {
            asked++;
            throw new InvalidOperationException("harmony is mid-rebuild");
        };

        Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(Alpha).Handling);
        Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(Beta).Handling);

        Assert.Equal(1, asked);
        Assert.Single(logged);
        Assert.Contains(ForeignPatcherPolicy.ProbeFailedMarker, logged[0], StringComparison.Ordinal);
        Assert.Contains("harmony is mid-rebuild", logged[0], StringComparison.Ordinal);
    }

    private static MethodBase[] Eight() {
        return typeof(string)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsGenericMethod)
            .Take(8)
            .Cast<MethodBase>()
            .ToArray();
    }
}
