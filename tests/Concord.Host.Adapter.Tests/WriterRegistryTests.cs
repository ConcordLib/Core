using System.Collections.Concurrent;
using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public static class Written {
    public static int First(int left) {
        return left + 1;
    }

    public static int Second(int left) {
        return left + 2;
    }

    public static int Third(int left) {
        return left + 3;
    }
}

public sealed class Held<T> {
    public int Ping() {
        return 1;
    }
}

[Collection("AdapterStatics")]
public class WriterRegistryTests : IDisposable {
    private static readonly MethodBase One = typeof(Written).GetMethod(nameof(Written.First))!;

    private static readonly MethodBase Two = typeof(Written).GetMethod(nameof(Written.Second))!;

    private static readonly MethodBase Three = typeof(Written).GetMethod(nameof(Written.Third))!;

    private readonly List<string> policyLog = [];

    public WriterRegistryTests() {
        ForeignPatcherPolicy.ResetForTests();
        ForeignPatcherPolicy.Log = policyLog.Add;
    }

    public void Dispose() {
        ForeignPatcherPolicy.ResetForTests();
    }

    [Fact]
    public void AClaimRecordsWhatWasWrittenOnWhoseBehalfAndWhen() {
        List<string> logged = [];
        WriterRegistry registry = Fresh(logged);
        DateTime before = DateTime.UtcNow;

        registry.Claim(One, [1, 2, 3], ["mod.a", "mod.b"]);

        WriterRecord record = registry.RecordFor(One)!;
        Assert.Equal(["mod.a", "mod.b"], record.Owners);
        Assert.Equal(3, record.BodyLength);
        Assert.Equal(WriterRegistry.Fingerprint([1, 2, 3]), record.Fingerprint);
        Assert.Equal(1, record.PublishCount);
        Assert.InRange(record.PublishedUtc, before, DateTime.UtcNow);
        Assert.Equal(registry.WriterId, record.WriterId);
        Assert.Empty(logged);
    }

    [Fact]
    public void ClaimingFromTheComposedInjectionsTakesTheOwnersOffThem() {
        WriterRegistry registry = Fresh([]);
        MethodBase injector = typeof(Written).GetMethod(nameof(Written.Third))!;

        registry.Claim(
            One,
            [7],
            [
                new Injection(injector, new InjectAt.Head(), "mod.a", 0),
                new Injection(injector, new InjectAt.Tail(), "mod.b", 0),
            ]);

        Assert.Equal(["mod.a", "mod.b"], registry.RecordFor(One)!.Owners);
    }

    [Fact]
    public void RepublishingOneTargetUpdatesTheRecordAndCountsBothWrites() {
        WriterRegistry registry = Fresh([]);

        registry.Claim(One, [1], ["mod.a"]);
        registry.Published(One, 1);
        registry.Claim(One, [9, 9], ["mod.a", "mod.b"]);
        registry.Published(One, 2);

        WriterRecord record = registry.RecordFor(One)!;
        Assert.Equal(2, record.PublishCount);
        Assert.Equal(["mod.a", "mod.b"], record.Owners);
        Assert.Equal(WriterRegistry.Fingerprint([9, 9]), record.Fingerprint);
        Assert.Equal(1, registry.ClaimCount);
        Assert.Equal(2, registry.PublishedBodies);
        Assert.Equal(3, registry.PublishedBytes);
    }

    [Fact]
    public void AClaimOnItsOwnIsNotAPublishUntilTheWriterConfirmsOne() {
        WriterRegistry registry = Fresh([]);

        registry.Claim(One, [1, 2], ["mod.a"]);

        Assert.True(registry.HoldsClaim(One));
        Assert.Equal(0, registry.PublishedBodies);
        Assert.Equal(0, registry.PublishedBytes);

        registry.Published(One, 2);

        Assert.Equal(1, registry.PublishedBodies);
        Assert.Equal(2, registry.PublishedBytes);
    }

    [Fact]
    public void ReleasingDropsTheClaimAndKeepsTheLeakLedger() {
        WriterRegistry registry = Fresh([]);
        registry.Claim(One, [1, 2], ["mod.a"]);
        registry.Published(One, 2);

        Assert.True(registry.Release(One));
        Assert.False(registry.Release(One));
        Assert.False(registry.HoldsClaim(One));
        Assert.Equal(0, registry.ClaimCount);
        Assert.Equal(1, registry.PublishedBodies);
        Assert.Equal(2, registry.PublishedBytes);
    }

    [Fact]
    public void ASecondConcordWriterOnOneTargetIsDetectedExactly() {
        List<string> logged = [];
        ConcurrentDictionary<MethodBase, string> shared = new ConcurrentDictionary<MethodBase, string>();
        WriterRegistry first = new WriterRegistry(logged.Add, shared);
        WriterRegistry second = new WriterRegistry(logged.Add, shared);

        first.Claim(One, [1], ["mod.a"]);
        ContentionReport report = second.Claim(One, [2], ["mod.b"]);

        Assert.Equal(ContentionFinding.ConcordCollision, report.Finding);
        Assert.Contains(first.WriterId, report.Explanation);
        Assert.Contains(second.WriterId, report.Explanation);
        Assert.Single(logged);
        Assert.StartsWith(WriterRegistry.CollisionMarker, logged[0]);
    }

    [Fact]
    public void ACollisionRecordsNoPublishSoTheLeakLedgerStaysTrueWhenTheWriteIsRefused() {
        ConcurrentDictionary<MethodBase, string> shared = new ConcurrentDictionary<MethodBase, string>();
        WriterRegistry first = new WriterRegistry(static _ => { }, shared);
        WriterRegistry second = new WriterRegistry(static _ => { }, shared);

        first.Claim(One, [1, 2, 3], ["mod.a"]);
        ContentionReport refused = second.Claim(One, [4, 5, 6, 7], ["mod.b"]);

        Assert.Equal(ContentionFinding.ConcordCollision, refused.Finding);
        Assert.Null(refused.Concord);
        Assert.Equal(0, second.PublishedBodies);
        Assert.Equal(0, second.PublishedBytes);
        Assert.Equal(0, second.ClaimCount);
        Assert.False(second.HoldsClaim(One));
        Assert.Equal(1, first.RecordFor(One)!.PublishCount);
        Assert.Equal(WriterRegistry.Fingerprint([1, 2, 3]), first.RecordFor(One)!.Fingerprint);
    }

    [Fact]
    public void OneWriterRepublishingIsNeverACollision() {
        ConcurrentDictionary<MethodBase, string> shared = new ConcurrentDictionary<MethodBase, string>();
        WriterRegistry registry = new WriterRegistry(static _ => { }, shared);

        registry.Claim(One, [1], ["mod.a"]);
        ContentionReport report = registry.Claim(One, [2], ["mod.a"]);

        Assert.Equal(ContentionFinding.Unknown, report.Finding);
    }

    [Fact]
    public void ReleasingHandsTheTargetBackSoTheNextWriterIsNotACollision() {
        ConcurrentDictionary<MethodBase, string> shared = new ConcurrentDictionary<MethodBase, string>();
        WriterRegistry first = new WriterRegistry(static _ => { }, shared);
        WriterRegistry second = new WriterRegistry(static _ => { }, shared);

        first.Claim(One, [1], ["mod.a"]);
        first.Release(One);

        Assert.Equal(ContentionFinding.Unknown, second.Claim(One, [2], ["mod.b"]).Finding);
    }

    [Fact]
    public void AForeignOwnerIsReportedBeforeTheBodyIsWritten() {
        List<string> logged = [];
        WriterRegistry registry = Fresh(logged);
        ForeignPatcherPolicy.Owners = static _ => ["harmony.somemod"];

        ContentionReport report = registry.Claim(One, [1], ["mod.a"]);

        Assert.Equal(ContentionFinding.ForeignOwner, report.Finding);
        Assert.Equal(["harmony.somemod"], ForeignPatcherPolicy.HarmonyOwners(One));
        Assert.Contains("harmony.somemod", report.Explanation);
        Assert.True(report.Poisoned);
        Assert.Single(logged);
        Assert.StartsWith(WriterRegistry.ContestedMarker, logged[0]);
    }

    [Fact]
    public void AClaimHandedAProbedOwnerListJudgesFromItAndDoesNotProbeAgain() {
        List<string> logged = [];
        WriterRegistry registry = Fresh(logged);
        int probes = 0;
        ForeignPatcherPolicy.Owners = _ => {
            probes++;
            return null;
        };

        ContentionReport report = registry.Claim(One, [1], ["mod.a"], ["harmony.somemod"]);

        Assert.Equal(0, probes);
        Assert.Equal(ContentionFinding.ForeignOwner, report.Finding);
        Assert.True(report.Poisoned);
        Assert.Contains("harmony.somemod", report.Explanation);
        Assert.Single(logged);
    }

    [Fact]
    public void HarmonyPatchingThenUnpatchingStillReadsAsPoisonedNotClean() {
        WriterRegistry registry = Fresh([]);
        ForeignPatcherPolicy.Owners = static _ => [];

        ContentionReport report = registry.Claim(One, [1], ["mod.a"]);

        Assert.Empty(ForeignPatcherPolicy.HarmonyOwners(One)!);
        Assert.True(report.Poisoned);
        Assert.Equal(ContentionFinding.Unknown, report.Finding);
        Assert.Contains("poisons a target permanently", report.Explanation);
        Assert.Equal(WriterRegistry.ContestedMarker, report.Marker);
    }

    [Fact]
    public void ATargetThePolicyDisownedStaysPoisonedAfterHarmonyForgetsIt() {
        WriterRegistry registry = Fresh([]);
        ForeignPatcherPolicy.Owners = static _ => ["harmony.somemod"];
        Assert.Equal(ForeignHandling.Disown, ForeignPatcherPolicy.Judge(One).Handling);

        ForeignPatcherPolicy.Owners = static _ => null;
        ContentionReport report = registry.Claim(One, [1], ["mod.a"]);

        Assert.Null(ForeignPatcherPolicy.HarmonyOwners(One));
        Assert.True(report.Poisoned);
        Assert.Equal(ContentionFinding.Unknown, report.Finding);
        Assert.Contains("owned this target at some point", report.Explanation);
    }

    [Fact]
    public void AnUnobservedTargetSaysSoRatherThanClaimingToBeClean() {
        WriterRegistry registry = Fresh([]);
        ForeignPatcherPolicy.Owners = static _ => null;

        ContentionReport report = registry.Claim(One, [1], ["mod.a"]);

        Assert.Equal(ContentionFinding.Unknown, report.Finding);
        Assert.False(report.Poisoned);
        Assert.Equal(WriterRegistry.UnobservedMarker, report.Marker);
        Assert.Contains("a patcher Concord cannot question reads the same way", report.Explanation);
        Assert.Contains(WriterRegistry.BlindSpot, report.Explanation);
        Assert.Contains("not that Concord is the sole writer", WriterRegistry.BlindSpot);
    }

    [Fact]
    public void AProbeThatThrowsLeavesTheTargetUnobservedRatherThanClean() {
        List<string> logged = [];
        WriterRegistry registry = Fresh(logged);
        ForeignPatcherPolicy.Owners = static _ => throw new InvalidOperationException("the bridge is gone");

        ContentionReport report = registry.Claim(One, [1], ["mod.a"]);

        Assert.Equal(ContentionFinding.Unknown, report.Finding);
        Assert.False(report.Poisoned);
        Assert.Contains(WriterRegistry.BlindSpot, report.Explanation);
        Assert.Empty(logged);
        Assert.Contains(policyLog, line => line.Contains(ForeignPatcherPolicy.ProbeFailedMarker));
    }

    [Fact]
    public void ACheckpointReportsOnlyContestedTargetsAndLogsEachOnce() {
        List<string> logged = [];
        WriterRegistry registry = Fresh(logged);
        registry.Claim(One, [1], ["mod.a"]);
        registry.Claim(Two, [2], ["mod.b"]);
        ForeignPatcherPolicy.Owners = static method => method.Name == nameof(Written.Second) ? ["harmony.somemod"] : null;

        IReadOnlyList<ContentionReport> first = registry.Checkpoint();
        IReadOnlyList<ContentionReport> second = registry.Checkpoint();

        Assert.Equal([nameof(Written.Second)], first.Select(report => report.Target.Name));
        Assert.Equal([nameof(Written.Second)], second.Select(report => report.Target.Name));
        Assert.Single(logged);
    }

    [Fact]
    public void TheSummaryCountsBodiesBytesAndPoisonedTargets() {
        WriterRegistry registry = Fresh([]);
        ForeignPatcherPolicy.Owners = static method => method.Name == nameof(Written.Third) ? ["harmony.somemod"] : null;

        registry.Claim(One, [1, 2], ["mod.a"]);
        registry.Published(One, 2);
        registry.Claim(Two, [3], ["mod.b"]);
        registry.Published(Two, 1);
        registry.Claim(Three, [4], ["mod.c"]);
        registry.Published(Three, 1);

        string summary = registry.Summary();
        Assert.Contains("3 target(s) claimed", summary);
        Assert.Contains("3 body/bodies published", summary);
        Assert.Contains("4 byte(s) of replaced IL", summary);
        Assert.Contains("1 target(s) permanently poisoned", summary);
        Assert.Contains(WriterRegistry.BlindSpot, summary);
    }

    [Fact]
    public void NoPublicBooleanEverAnswersAQuestionAboutAnotherWriter() {
        List<string> booleans = [];
        foreach (MethodInfo member in typeof(WriterRegistry).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
            if (member.ReturnType == typeof(bool)) {
                booleans.Add(member.Name);
            }
        }

        booleans.Sort();
        Assert.Equal(["HoldsClaim", "Release"], booleans);
    }

    [Fact]
    public void TheRegistryKeepsNoForeignOwnerProbeOfItsOwn() {
        List<string> probes = [];
        foreach (PropertyInfo member in typeof(WriterRegistry).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
            if (typeof(Delegate).IsAssignableFrom(member.PropertyType)) {
                probes.Add(member.Name);
            }
        }

        Assert.Empty(probes);
    }

    [Fact]
    public void EveryInstantiationOfASharedGenericLandsOnOneClaim() {
        WriterRegistry registry = Fresh([]);
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        MethodBase version = typeof(Held<Version>).GetMethod(nameof(Held<Version>.Ping))!;

        registry.Claim(text, [1], ["mod.a"]);

        Assert.True(registry.HoldsClaim(version));
        Assert.Equal(1, registry.ClaimCount);
    }

    [Fact]
    public void AValueInstantiationTheDetourKeyKeepsApartLandsOnTheSameClaim() {
        WriterRegistry registry = Fresh([]);
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        MethodBase number = typeof(Held<int>).GetMethod(nameof(Held<int>.Ping))!;
        Assert.NotEqual(MethodIdentity.SharedBodyKey(text), MethodIdentity.SharedBodyKey(number));

        registry.Claim(text, [1], ["mod.a"]);

        Assert.True(registry.HoldsClaim(number));
        Assert.Equal(1, registry.ClaimCount);
        Assert.Equal(["mod.a"], registry.RecordFor(number)!.Owners);
    }

    [Fact]
    public void ReleasingOneInstantiationReleasesEveryInstantiationOfTheSameDefinition() {
        WriterRegistry registry = Fresh([]);
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        MethodBase number = typeof(Held<int>).GetMethod(nameof(Held<int>.Ping))!;
        registry.Claim(text, [1], ["mod.a"]);

        Assert.True(registry.Release(number));

        Assert.False(registry.HoldsClaim(text));
        Assert.Equal(0, registry.ClaimCount);
    }

    [Fact]
    public void OneInstantiationDisownedPoisonsEveryInstantiationThatSharesTheBody() {
        WriterRegistry registry = Fresh([]);
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        MethodBase version = typeof(Held<Version>).GetMethod(nameof(Held<Version>.Ping))!;
        ForeignPatcherPolicy.Owners = method => method == text ? ["harmony.somemod"] : null;

        Assert.Equal(ForeignHandling.Disown, ForeignPatcherPolicy.Judge(text).Handling);

        Assert.True(ForeignPatcherPolicy.IsDisowned(version));
        Assert.True(registry.Claim(version, [1], ["mod.a"]).Poisoned);
    }

    [Fact]
    public void ACheckpointAsksAboutTheInstantiationThatWasClaimedNotTheCanonicalKey() {
        WriterRegistry registry = Fresh([]);
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        ForeignPatcherPolicy.Owners = method => method == text ? ["harmony.somemod"] : null;

        registry.Claim(text, [1], ["mod.a"]);
        IReadOnlyList<ContentionReport> found = registry.Checkpoint();

        Assert.Single(found);
        Assert.Equal(ContentionFinding.ForeignOwner, found[0].Finding);
        Assert.True(found[0].Poisoned);
    }

    [Fact]
    public void TheSummaryAsksAboutTheInstantiationThatWasClaimedNotTheCanonicalKey() {
        WriterRegistry registry = Fresh([]);
        MethodBase text = typeof(Held<string>).GetMethod(nameof(Held<string>.Ping))!;
        ForeignPatcherPolicy.Owners = method => method == text ? ["harmony.somemod"] : null;

        registry.Claim(text, [1], ["mod.a"]);

        Assert.Contains("1 target(s) permanently poisoned", registry.Summary());
    }

    [Fact]
    public void TheProcessClaimGateIsOneObjectASecondCopyOfThisAssemblyAlsoHolds() {
        Type copy = SecondCopyOfTheRegistry();

        Assert.NotSame(typeof(WriterRegistry), copy);
        Assert.Same(Member(typeof(WriterRegistry), "SlotGate"), Member(copy, "SlotGate"));
    }

    [Fact]
    public void BothCopiesOfThisAssemblyReadOneProcessWideClaimMap() {
        Type copy = SecondCopyOfTheRegistry();

        Assert.Same(ProcessClaimsOf(typeof(WriterRegistry)), ProcessClaimsOf(copy));
    }

    [Fact]
    public void TheFingerprintIsStableAcrossRunsAndChangesWithTheBody() {
        Assert.Equal(0xcbf29ce484222325UL, WriterRegistry.Fingerprint([]));
        Assert.Equal(0xaf63a74c8601927dUL, WriterRegistry.Fingerprint([0x2a]));
        Assert.Equal(0x082f2407b4e8902aUL, WriterRegistry.Fingerprint([1, 2]));
        Assert.Equal(0x08395307b4f1348cUL, WriterRegistry.Fingerprint([2, 1]));
    }

    [Fact]
    public void ConcurrentClaimsOnOneTargetAllLandAndCountOnce() {
        WriterRegistry registry = Fresh([]);

        Parallel.For(0, 8, thread => {
            for (int i = 0; i < 50; i++) {
                registry.Claim(One, [(byte)thread], ["t" + thread]);
                registry.Published(One, 1);
            }
        });

        Assert.Equal(1, registry.ClaimCount);
        Assert.Equal(400, registry.PublishedBodies);
        Assert.Equal(400, registry.RecordFor(One)!.PublishCount);
    }

    [Fact]
    public void ASummaryLooksForHarmonyOnceNotOncePerClaim() {
        WriterRegistry registry = Fresh([]);
        foreach (MethodBase target in Eight()) {
            registry.Claim(target, [1], ["mod.a"]);
        }

        Assert.Equal(8, registry.ClaimCount);
        int before = ForeignPatcherPolicy.Scans;

        Assert.Contains("0 target(s) permanently poisoned", registry.Summary());

        Assert.Equal(1, ForeignPatcherPolicy.Scans - before);
    }

    [Fact]
    public void ACheckpointLooksForHarmonyOnceNotOncePerClaim() {
        WriterRegistry registry = Fresh([]);
        foreach (MethodBase target in Eight()) {
            registry.Claim(target, [1], ["mod.a"]);
        }

        Assert.Equal(8, registry.ClaimCount);
        int before = ForeignPatcherPolicy.Scans;

        Assert.Empty(registry.Checkpoint());

        Assert.Equal(1, ForeignPatcherPolicy.Scans - before);
    }

    [Fact]
    public void ASummaryAsksTheProbeOncePerClaimBecauseEveryClaimNeedsItsOwnAnswer() {
        WriterRegistry registry = Fresh([]);
        foreach (MethodBase target in Eight()) {
            registry.Claim(target, [1], ["mod.a"]);
        }

        Assert.Equal(8, registry.ClaimCount);
        int asked = 0;
        ForeignPatcherPolicy.Owners = _ => {
            asked++;
            return null;
        };

        Assert.Contains("0 target(s) permanently poisoned", registry.Summary());

        Assert.Equal(8, asked);
    }

    [Fact]
    public void ACheckpointAsksTheProbeOncePerClaimAndReportsEveryClaimedTarget() {
        WriterRegistry registry = Fresh([]);
        foreach (MethodBase target in Eight()) {
            registry.Claim(target, [1], ["mod.a"]);
        }

        Assert.Equal(8, registry.ClaimCount);
        int asked = 0;
        ForeignPatcherPolicy.Owners = _ => {
            asked++;
            return ["harmony.somemod"];
        };

        IReadOnlyList<ContentionReport> found = registry.Checkpoint();

        Assert.Equal(8, found.Count);
        Assert.Equal(8, asked);
    }

    [Fact]
    public void AHarmonyThatBindsBetweenTwoSummariesIsNoticedOnTheSecond() {
        WriterRegistry registry = Fresh([]);
        registry.Claim(One, [1], ["mod.a"]);
        int before = ForeignPatcherPolicy.Scans;

        Assert.Contains("0 target(s) permanently poisoned", registry.Summary());
        Assert.Equal(1, ForeignPatcherPolicy.Scans - before);

        ForeignPatcherPolicy.Owners = static _ => ["harmony.somemod"];

        Assert.Contains("1 target(s) permanently poisoned", registry.Summary());
    }

    [Fact]
    public void AHarmonyThatBindsBetweenTwoCheckpointsIsNoticedOnTheSecond() {
        WriterRegistry registry = Fresh([]);
        registry.Claim(One, [1], ["mod.a"]);
        int before = ForeignPatcherPolicy.Scans;

        Assert.Empty(registry.Checkpoint());
        Assert.Equal(1, ForeignPatcherPolicy.Scans - before);

        ForeignPatcherPolicy.Owners = static _ => ["harmony.somemod"];

        Assert.Equal(ContentionFinding.ForeignOwner, Assert.Single(registry.Checkpoint()).Finding);
    }

    private static WriterRegistry Fresh(List<string> logged) {
        return new WriterRegistry(logged.Add, new ConcurrentDictionary<MethodBase, string>());
    }

    private static MethodBase[] Eight() {
        return typeof(string)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsGenericMethod)
            .Take(8)
            .Cast<MethodBase>()
            .ToArray();
    }

    private static Type SecondCopyOfTheRegistry() {
        Assembly mine = typeof(WriterRegistry).Assembly;
        Assembly other = Assembly.Load(File.ReadAllBytes(mine.Location));
        return other.GetType(typeof(WriterRegistry).FullName!, true)!;
    }

    private static object Member(Type registry, string name) {
        return registry.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    }

    private static object ProcessClaimsOf(Type registry) {
        return registry
            .GetMethod("ProcessClaims", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
    }
}
