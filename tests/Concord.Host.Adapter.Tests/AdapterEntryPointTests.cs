using System.Collections.Concurrent;
using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.Tests;

[Collection("AdapterStatics")]
public sealed class EmptyMapSwitchTests : IDisposable {
    private readonly string? before = Environment.GetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP");

    public EmptyMapSwitchTests() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", null);
        AdapterEntryPoint.ResetForTests();
    }

    public void Dispose() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", before);
        AdapterEntryPoint.ResetForTests();
    }

    [Fact]
    public void TheVariableTheFallbackReadsIsTheOneDocumentedOutsideTheProcess() {
        Assert.Equal("CONCORD_WAYLITH_EMPTY_MAP", AdapterEntryPoint.EmptyMapVariable);
        Assert.NotEqual(AdapterEntryPoint.SwitchVariable, AdapterEntryPoint.EmptyMapVariable);
    }

    [Fact]
    public void NothingSetLeavesTheFallbackOff() {
        Assert.False(AdapterEntryPoint.EmptyMapRequested(Sink()));
        Assert.False(WaylithDetourBackend.EmptyMapAlways);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("on")]
    [InlineData("YES")]
    [InlineData("  enabled  ")]
    public void EveryOnSpellingTakesTheWayBack(string raw) {
        Set(raw);

        Assert.True(AdapterEntryPoint.EmptyMapRequested(Sink()));
        Assert.True(WaylithDetourBackend.EmptyMapAlways);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("Off")]
    [InlineData("no")]
    [InlineData("disabled")]
    [InlineData("")]
    [InlineData("   ")]
    public void EveryOffSpellingAndAnEmptyValueLeaveItOff(string raw) {
        Set(raw);

        Assert.False(AdapterEntryPoint.EmptyMapRequested(Sink()));
        Assert.False(WaylithDetourBackend.EmptyMapAlways);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("offf")]
    [InlineData("empty")]
    public void AValueNobodyCanReadIsReportedAndThenTreatedAsOff(string raw) {
        Set(raw);
        List<string> written = [];

        Assert.False(AdapterEntryPoint.EmptyMapRequested(written.Add));

        string line = Assert.Single(written);
        Assert.Contains(AdapterEntryPoint.EmptyMapVariable + " is set to '" + raw + "'", line, StringComparison.Ordinal);
        Assert.Contains("which is neither", line, StringComparison.Ordinal);
        Assert.EndsWith(
            ". Ignoring it, so every body keeps the sentinel offset map that tells injected code apart from the target's own il.",
            line,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OneUnreadableValueIsReportedOnceNotOncePerMethodPublished() {
        Set("2");
        List<string> written = [];

        for (int i = 0; i < 5; i++) {
            Assert.False(AdapterEntryPoint.EmptyMapRequested(written.Add));
        }

        Assert.Single(written);
    }

    [Fact]
    public void ASecondUnreadableValueIsReportedOnItsOwn() {
        List<string> written = [];

        Set("2");
        _ = AdapterEntryPoint.EmptyMapRequested(written.Add);
        Set("3");
        _ = AdapterEntryPoint.EmptyMapRequested(written.Add);

        Assert.Equal(2, written.Count);
    }

    [Fact]
    public void TheReportIsForgottenWhenTheAdapterIsResetSoNoTestInheritsIt() {
        Set("2");
        List<string> written = [];

        _ = AdapterEntryPoint.EmptyMapRequested(written.Add);
        AdapterEntryPoint.ResetForTests();
        _ = AdapterEntryPoint.EmptyMapRequested(written.Add);

        Assert.Equal(2, written.Count);
    }

    [Fact]
    public void ReadingTheVariableNeverThrowsWhateverIsInIt() {
        foreach (string raw in new[] { "\t", "on;off", "=", new string('x', 4096) }) {
            Set(raw);

            Assert.False(AdapterEntryPoint.EmptyMapRequested(Sink()));
        }
    }

    [Fact]
    public void AnInProcessTrueIsNotLoweredByTheEnvironment() {
        Set("off");
        WaylithDetourBackend.EmptyMapAlways = true;

        try {
            Assert.True(WaylithDetourBackend.EmptyMapAlways);
        } finally {
            WaylithDetourBackend.EmptyMapAlways = false;
        }
    }

    [Fact]
    public void AnInProcessFalseDoesNotPinTheFallbackAgainstTheEnvironment() {
        WaylithDetourBackend.EmptyMapAlways = false;
        Set("on");

        Assert.True(WaylithDetourBackend.EmptyMapAlways);
    }

    [Fact]
    public void TheEnvironmentIsReadEveryTimeNotOnceAtStartup() {
        Set("on");
        Assert.True(WaylithDetourBackend.EmptyMapAlways);
        Set("off");
        Assert.False(WaylithDetourBackend.EmptyMapAlways);
        Set("on");
        Assert.True(WaylithDetourBackend.EmptyMapAlways);
    }

    private static Action<string> Sink() {
        return _ => {
        };
    }

    private static void Set(string? value) {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", value);
    }
}

public static class Polled {
    public static int Late() {
        return 1;
    }

    public static int Quiet() {
        return 2;
    }
}

[Collection("AdapterStatics")]
public sealed class ContentionPollTests : IDisposable {
    private static readonly MethodBase Late = typeof(Polled).GetMethod(nameof(Polled.Late))!;

    private static readonly MethodBase Quiet = typeof(Polled).GetMethod(nameof(Polled.Quiet))!;

    private static readonly TimeSpan Appears = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(400);

    private readonly string? before = Environment.GetEnvironmentVariable(AdapterEntryPoint.SwitchVariable);

    private readonly IDetourBackend backendBefore = DetourBackend.Current;

    private readonly ConcurrentQueue<string> written = new ConcurrentQueue<string>();

    public ContentionPollTests() {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.SwitchVariable, null);
        DetourBackend.Current = new MonoModDetourBackend();
        ForeignPatcherPolicy.ResetForTests();
        AdapterEntryPoint.ResetForTests();
        AdapterEntryPoint.PatchedTargets = static () => 0;
        AdapterEntryPoint.PollInterval = TimeSpan.FromMilliseconds(25);
    }

    public void Dispose() {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.SwitchVariable, before);
        DetourBackend.Current = backendBefore;
        AdapterEntryPoint.ResetForTests();
        AdapterEntryPoint.PatchedTargets = null;
        ForeignPatcherPolicy.ResetForTests();
        Patcher.UseLocalNameResolver(null);
    }

    [Fact]
    public void AForeignPatcherTakingAPublishedTargetIsReportedWithNothingElsePatching() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;

        Selection chosen = InstallWith(BodyReplacement.Available);

        Assert.True(chosen.Waylith, chosen.Reason);
        Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(Late).Handling);

        held[Late] = ["com.example.late"];

        Assert.True(Wait(() => Contested() > 0, Appears), Lines());
        Assert.Contains("com.example.late", Lines(), StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondPollDoesNotRepeatAContentionTheFirstOneReported() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;

        _ = InstallWith(BodyReplacement.Available);
        ForeignPatcherPolicy.Judge(Late);
        held[Late] = ["com.example.late"];

        Assert.True(Wait(() => Contested() > 0, Appears), Lines());
        _ = Wait(() => Contested() > 1, Silence);

        Assert.Equal(1, Contested());
    }

    [Fact]
    public void NoContentionWritesNothingAfterTheInstallLine() {
        ForeignPatcherPolicy.Owners = static _ => null;

        Selection chosen = InstallWith(BodyReplacement.Available);

        Assert.Equal(ForeignHandling.Allow, ForeignPatcherPolicy.Judge(Quiet).Handling);
        _ = Wait(() => written.Count > 1, Silence);

        Assert.Equal(chosen.Message, Assert.Single(written));
    }

    [Fact]
    public void ADetourSelectionNeverPollsBecauseTheBridgeOwnsCoexistenceThere() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;

        Selection chosen = InstallWith(BodyReplacement.ApiAbsent);

        Assert.False(chosen.Waylith);
        ForeignPatcherPolicy.Judge(Late);
        held[Late] = ["com.example.late"];
        _ = Wait(() => Contested() > 0, Silence);

        Assert.Equal(0, Contested());
    }

    [Fact]
    public void WithThePollTurnedOffTheSameContentionIsNeverReported() {
        Dictionary<MethodBase, string[]> held = [];
        ForeignPatcherPolicy.Owners = target => held.TryGetValue(target, out string[]? owners) ? owners : null;
        AdapterEntryPoint.PollInterval = TimeSpan.Zero;

        Selection chosen = InstallWith(BodyReplacement.Available);

        Assert.True(chosen.Waylith, chosen.Reason);
        ForeignPatcherPolicy.Judge(Late);
        held[Late] = ["com.example.late"];
        _ = Wait(() => Contested() > 0, Silence);

        Assert.Equal(0, Contested());
        Assert.Single(ForeignPatcherPolicy.Contentions());
    }

    private static bool Wait(Func<bool> until, TimeSpan limit) {
        DateTime deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline) {
            if (until()) {
                return true;
            }

            Thread.Sleep(10);
        }

        return until();
    }

    private Selection InstallWith(BodyReplacement support) {
        return AdapterEntryPoint.Install(
            written.Enqueue, null, [AppContext.BaseDirectory], support, _ => new StandInBackend());
    }

    private int Contested() {
        int found = 0;
        foreach (string line in written) {
            if (line.Contains(ForeignPatcherPolicy.ContestedMarker, StringComparison.Ordinal)) {
                found++;
            }
        }

        return found;
    }

    private string Lines() {
        return string.Join(" | ", written);
    }
}
