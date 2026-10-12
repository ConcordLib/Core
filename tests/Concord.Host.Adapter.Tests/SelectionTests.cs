using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public static class LooksLikeTheApi {
    public static bool IsSupported => true;

    public static void ReplaceMethodBody() {
    }

    public static void Revert() {
    }
}

public static class TurnedOffByTheRuntime {
    public static bool IsSupported => false;

    public static void ReplaceMethodBody() {
    }

    public static void Revert() {
    }
}

public static class NoSupportFlag {
    public static void ReplaceMethodBody() {
    }

    public static void Revert() {
    }
}

public static class FlagButNoEntryPoints {
    public static bool IsSupported => true;
}

public static class FlagAndReplaceButNoRevert {
    public static bool IsSupported => true;

    public static void ReplaceMethodBody() {
    }
}

public static class SupportFlagThatThrows {
    public static bool IsSupported => throw new EntryPointNotFoundException("MethodBodyUpdater_IsSupported");

    public static void ReplaceMethodBody() {
    }

    public static void Revert() {
    }
}

public sealed class NonStaticSupportFlag {
    public bool IsSupported => true;

    public static void ReplaceMethodBody() {
    }

    public static void Revert() {
    }
}

public sealed class CapabilityTests {
    [Fact]
    public void AnAbsentApiIsTheOnlyAnswerWhenTheTypeIsNotThere() {
        Assert.Equal(BodyReplacement.ApiAbsent, Support(null, true));
        Assert.Equal(BodyReplacement.ApiAbsent, Support(null, false));
    }

    [Fact]
    public void TheRuntimesOwnSwitchBeatsTheMethodsBinding() {
        Assert.Equal(BodyReplacement.DisabledByRuntime, Support(typeof(TurnedOffByTheRuntime), true));
    }

    [Fact]
    public void AnApiWithoutAStaticBoolSupportFlagIsNotTheApiThisWasBuiltAgainst() {
        Assert.Equal(BodyReplacement.ApiUnrecognized, Support(typeof(NoSupportFlag), true));
        Assert.Equal(BodyReplacement.ApiUnrecognized, Support(typeof(NonStaticSupportFlag), true));
    }

    [Fact]
    public void ASupportFlagWithoutBothEntryPointsIsNotTheApiEither() {
        Assert.Equal(BodyReplacement.ApiUnrecognized, Support(typeof(FlagButNoEntryPoints), true));
        Assert.Equal(BodyReplacement.ApiUnrecognized, Support(typeof(FlagAndReplaceButNoRevert), true));
    }

    [Fact]
    public void AWholeApiStillNeedsTheTokenMintersToBind() {
        Assert.Equal(BodyReplacement.MintingAbsent, Support(typeof(LooksLikeTheApi), false));
        Assert.Equal(BodyReplacement.Available, Support(typeof(LooksLikeTheApi), true));
    }

    [Fact]
    public void ASupportFlagThatThrowsIsReportedAsAMismatchRatherThanTakingTheProcessDown() {
        (BodyReplacement support, string? failure) = RuntimeCapability.Probe(typeof(SupportFlagThatThrows), true);

        Assert.Equal(BodyReplacement.ProbeFailed, support);
        Assert.Contains(nameof(EntryPointNotFoundException), failure!);
        Assert.Contains("MethodBodyUpdater_IsSupported", failure!);
        Assert.Contains(failure!, RuntimeCapability.Explain(support, failure));
        Assert.Contains("no detail was captured", RuntimeCapability.Explain(support));
    }

    private static BodyReplacement Support(Type? updater, bool mintingBound) {
        (BodyReplacement support, string? failure) = RuntimeCapability.Probe(updater, mintingBound);
        Assert.Null(failure);
        return support;
    }

    [Fact]
    public void EveryOutcomeExplainsItselfWithoutRepeatingAnother() {
        BodyReplacement[] all = Enum.GetValues<BodyReplacement>();
        List<string> explanations = new List<string>();

        foreach (BodyReplacement support in all) {
            string explained = RuntimeCapability.Explain(support);
            Assert.NotEmpty(explained);
            Assert.DoesNotContain(explained, explanations);
            explanations.Add(explained);
        }

        Assert.Equal(all.Length, explanations.Count);
    }

    [Fact]
    public void ReadingEveryMemberOnARuntimeWithoutTheApiNeitherThrowsNorReportsSupport() {
        bool present = Type.GetType(RuntimeCapability.UpdaterTypeName, throwOnError: false) is not null;

        Assert.Equal(present, RuntimeCapability.Updater() is not null);
        Assert.Equal(
            RuntimeCapability.Support == BodyReplacement.ProbeFailed,
            RuntimeCapability.ProbeFailure is not null);
        Assert.NotEmpty(RuntimeCapability.Runtime);
        Assert.NotEmpty(RuntimeCapability.Explain());
        Assert.Equal(RuntimeCapability.Support == BodyReplacement.Available, RuntimeCapability.IsAvailable);

        if (present) {
            return;
        }

        Assert.Equal(BodyReplacement.ApiAbsent, RuntimeCapability.Support);
        Assert.False(RuntimeCapability.IsAvailable);
        Assert.Contains(RuntimeCapability.Runtime, RuntimeCapability.Explain());
    }
}

public sealed class SwitchParsingTests {
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("on")]
    [InlineData("yes")]
    [InlineData("enabled")]
    [InlineData("  on  ")]
    public void EveryOnSpellingForcesTheNewBackend(string raw) {
        Assert.Equal(SwitchState.ForceWaylith, AdapterEntryPoint.ReadSwitch(raw));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("Off")]
    [InlineData("no")]
    [InlineData("disabled")]
    [InlineData("\t0\n")]
    public void EveryOffSpellingForcesTheOldBackend(string raw) {
        Assert.Equal(SwitchState.ForceDetour, AdapterEntryPoint.ReadSwitch(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingSetIsNotAChoice(string? raw) {
        Assert.Equal(SwitchState.Unset, AdapterEntryPoint.ReadSwitch(raw));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("offf")]
    [InlineData("waylith")]
    [InlineData("-1")]
    public void AValueNobodyCanReadIsNeitherOnNorOff(string raw) {
        Assert.Equal(SwitchState.Unreadable, AdapterEntryPoint.ReadSwitch(raw));
    }
}

[CollectionDefinition("AdapterStatics", DisableParallelization = true)]
public sealed class AdapterStaticsCollection;

[Collection("AdapterStatics")]
public sealed class ChoiceTests : IDisposable {
    private readonly string? before = Environment.GetEnvironmentVariable(AdapterEntryPoint.SwitchVariable);

    public void Dispose() {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.SwitchVariable, before);
    }

    [Fact]
    public void WithNothingSetASupportedRuntimeGetsTheNewBackend() {
        Set(null);

        Selection chosen = AdapterEntryPoint.Choose(null, BodyReplacement.Available, Sink());

        Assert.True(chosen.Waylith);
        Assert.Contains("the default for this runtime", chosen.Reason);
        Assert.Contains("body replacement", chosen.Message);
    }

    [Fact]
    public void TheEnvironmentVariableBeatsASettingThatSaysTheOpposite() {
        Set("0");
        Selection off = AdapterEntryPoint.Choose(true, BodyReplacement.Available, Sink());

        Set("1");
        Selection on = AdapterEntryPoint.Choose(false, BodyReplacement.Available, Sink());

        Assert.False(off.Waylith);
        Assert.Contains(AdapterEntryPoint.SwitchVariable + "=0", off.Reason);
        Assert.DoesNotContain("the mod setting", off.Reason);

        Assert.True(on.Waylith);
        Assert.Contains(AdapterEntryPoint.SwitchVariable + "=1", on.Reason);
        Assert.DoesNotContain("the mod setting", on.Reason);
    }

    [Fact]
    public void WithNoEnvironmentVariableTheSettingDecides() {
        Set(null);

        Assert.False(AdapterEntryPoint.Choose(false, BodyReplacement.Available, Sink()).Waylith);
        Assert.True(AdapterEntryPoint.Choose(true, BodyReplacement.Available, Sink()).Waylith);
        Assert.Contains("the mod setting", AdapterEntryPoint.Choose(false, BodyReplacement.Available, Sink()).Reason);
    }

    [Fact]
    public void AnUnreadableEnvironmentVariableIsReportedAndThenIgnored() {
        Set("maybe");
        List<string> written = new List<string>();

        Selection chosen = AdapterEntryPoint.Choose(false, BodyReplacement.Available, written.Add);

        Assert.False(chosen.Waylith);
        Assert.Contains("the mod setting", chosen.Reason);
        Assert.Single(written);
        Assert.Contains("is set to 'maybe', which is neither", written[0]);
    }

    [Fact]
    public void ForcingItOnCannotBeatARuntimeThatCannotRunIt() {
        Set("on");

        foreach (BodyReplacement support in Enum.GetValues<BodyReplacement>()) {
            if (support == BodyReplacement.Available) {
                continue;
            }

            Selection chosen = AdapterEntryPoint.Choose(true, support, Sink());

            Assert.False(chosen.Waylith);
            Assert.Contains("asked for it, but it cannot run here", chosen.Reason);
            Assert.Contains("native detour", chosen.Message);
        }
    }

    [Fact]
    public void TurningItOffNeverMentionsTheRuntimeBecauseTheRuntimeWasNotConsulted() {
        Set("off");

        Selection chosen = AdapterEntryPoint.Choose(true, BodyReplacement.Available, Sink());

        Assert.False(chosen.Waylith);
        Assert.Equal("turned off by " + AdapterEntryPoint.SwitchVariable + "=off.", chosen.Reason);
    }

    [Fact]
    public void ARuntimeThatCannotRunItSaysWhichOfTheFiveReasonsItWas() {
        Set(null);

        Selection disabled = AdapterEntryPoint.Choose(null, BodyReplacement.DisabledByRuntime, Sink());
        Selection absent = AdapterEntryPoint.Choose(null, BodyReplacement.ApiAbsent, Sink());

        Assert.False(disabled.Waylith);
        Assert.False(absent.Waylith);
        Assert.Contains("DOTNET_MethodBodyUpdater=0", disabled.Reason);
        Assert.NotEqual(absent.Reason, disabled.Reason);
    }

    private static Action<string> Sink() {
        return _ => {
        };
    }

    private static void Set(string? value) {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.SwitchVariable, value);
    }
}

public static class LateInstallSubject {
    public static int Bumped { get; set; }

    public static void Bump() {
        Bumped++;
    }

    public static int Value() {
        return 1;
    }

    public static int Pinned() {
        return 2;
    }

    public static int PinnedReplacement() {
        return 3;
    }
}

public sealed class NotifyingHost : IForeignPatchHost {
    public IDisposable? EnterHostLock() {
        return null;
    }

    public string? ValidateRoute(MethodBase target, IReadOnlyList<Injection> added) {
        throw new NotSupportedException();
    }

    public string? RevalidateRouted(MethodBase target, object hostPatchState) {
        throw new NotSupportedException();
    }

    public ForeignRouteResult TryRoute(MethodBase target, IReadOnlyList<Injection> added, bool forceRoute) {
        throw new NotSupportedException();
    }

    public ForeignRouteResult RouteInto(MethodBase target, IReadOnlyList<Injection> added, object hostPatchState) {
        throw new NotSupportedException();
    }

    public IDetourHandle ApplyToRouted(MethodBase target, IReadOnlyList<Injection> added) {
        throw new NotSupportedException();
    }

    public IReadOnlyList<string> ForeignOwners(MethodBase target) {
        return [];
    }

    public IReadOnlyList<string> ConcordOwners(MethodBase target) {
        return [];
    }

    public bool TryInstallNotifier(IForeignPatchObserver observer, IDetourBackend rawBackend) {
        return true;
    }
}

public sealed class StandInBackend : IDetourBackend {
    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        throw new NotSupportedException();
    }

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        throw new NotSupportedException();
    }
}

[Collection("AdapterStatics")]
public sealed class InstallTests : IDisposable {
    private readonly string? before = Environment.GetEnvironmentVariable(AdapterEntryPoint.SwitchVariable);

    private readonly IDetourBackend backendBefore = DetourBackend.Current;

    public InstallTests() {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.SwitchVariable, null);
        DetourBackend.Current = new MonoModDetourBackend();
        AdapterEntryPoint.ResetForTests();
        AdapterEntryPoint.PatchedTargets = static () => 0;
    }

    public void Dispose() {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.SwitchVariable, before);
        DetourBackend.Current = backendBefore;
        AdapterEntryPoint.ResetForTests();
        AdapterEntryPoint.PatchedTargets = null;
        Patcher.UseLocalNameResolver(null);
    }

    [Fact]
    public void ASwapAfterTargetsArePatchedIsRefusedAndSaysHowMany() {
        AdapterEntryPoint.PatchedTargets = static () => 7;
        IDetourBackend previous = DetourBackend.Current;

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.Available,
            _ => new StandInBackend());

        Assert.False(chosen.Waylith);
        Assert.Same(previous, DetourBackend.Current);
        Assert.Contains("7 target", chosen.Reason);
        Assert.Contains("already carry a composed wrapper", chosen.Reason);
    }

    [Fact]
    public void TheLateInstallGuardReadsConcordsOwnPatchCount() {
        MethodBase target = typeof(LateInstallSubject).GetMethod(nameof(LateInstallSubject.Value))!;
        MethodInfo injection = typeof(LateInstallSubject).GetMethod(nameof(LateInstallSubject.Bump))!;
        IDetourBackend previous = DetourBackend.Current;

        using IDetourHandle patched = previous.ApplyComposed(
            target, [new Injection(injection, new InjectAt.Head(), "late-install", 0)]);

        AdapterEntryPoint.PatchedTargets = null;

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.Available,
            _ => new StandInBackend());

        Assert.True(Patcher.PatchedTargetCount > 0);
        Assert.False(chosen.Waylith);
        Assert.Same(previous, DetourBackend.Current);
        Assert.Contains("already carry a composed wrapper", chosen.Reason);
    }

    [Fact]
    public void ASupportedRuntimeGetsTheBackendInstalledWithTheOldOneAsItsFallback() {
        List<string> written = new List<string>();
        IDetourBackend previous = DetourBackend.Current;
        StandInBackend installed = new StandInBackend();
        IDetourBackend? handedFallback = null;

        Selection chosen = AdapterEntryPoint.Install(
            written.Add,
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.Available,
            fallback => {
                handedFallback = fallback;
                return installed;
            });

        Assert.True(chosen.Waylith);
        Assert.Same(installed, DetourBackend.Current);
        Assert.Same(previous, handedFallback);
        Assert.Single(written);
        Assert.Equal(chosen.Message, written[0]);
    }

    [Fact]
    public void AnUnsupportedRuntimeLeavesTheBackendExactlyWhereItWas() {
        IDetourBackend previous = DetourBackend.Current;

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.ApiAbsent,
            _ => new StandInBackend());

        Assert.False(chosen.Waylith);
        Assert.Same(previous, DetourBackend.Current);
    }

    [Fact]
    public void AnotherBackendAlreadyInPlaceIsNeverInstalledOver() {
        StandInBackend wrapper = new StandInBackend();
        DetourBackend.Current = wrapper;

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.Available,
            _ => new StandInBackend());

        Assert.False(chosen.Waylith);
        Assert.Same(wrapper, DetourBackend.Current);
        Assert.Contains("installing over it would strip whatever wrapped it", chosen.Reason);
        Assert.Contains(typeof(StandInBackend).FullName!, chosen.Reason);
    }

    [Fact]
    public void ARouterIsUnwrappedAndTheBackendGoesUnderneathItRatherThanBeingRefused() {
        MonoModDetourBackend detours = new MonoModDetourBackend();
        DetourBackend.Current = new RoutingDetourBackend(detours, _ => { }) { RouteEverything = true };
        StandInBackend installed = new StandInBackend();
        IDetourBackend? handedFallback = null;

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.Available,
            fallback => {
                handedFallback = fallback;
                return installed;
            });

        Assert.True(chosen.Waylith);
        Assert.Same(detours, handedFallback);

        RoutingDetourBackend rewrapped = Assert.IsType<RoutingDetourBackend>(DetourBackend.Current);

        Assert.Same(installed, rewrapped.Inner);
        Assert.True(rewrapped.RouteEverything);
    }

    [Fact]
    public void ARouterOverSomethingUnrecognisedIsStillRefusedAndNamesIt() {
        StandInBackend unknown = new StandInBackend();
        RoutingDetourBackend router = new RoutingDetourBackend(unknown, _ => { });
        DetourBackend.Current = router;

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.Available,
            _ => new StandInBackend());

        Assert.False(chosen.Waylith);
        Assert.Same(router, DetourBackend.Current);
        Assert.Contains(typeof(StandInBackend).FullName!, chosen.Reason);
        Assert.Contains(typeof(RoutingDetourBackend).FullName!, chosen.Reason);
    }

    [Fact]
    public void ARouterThatAlreadyDecidedARouteKeepsItsRouteAndGetsTheBackend() {
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), _ => { });
        DetourBackend.Current = router;

        using IDetourHandle pinned = router.Apply(
            typeof(LateInstallSubject).GetMethod(nameof(LateInstallSubject.Pinned))!,
            typeof(LateInstallSubject).GetMethod(nameof(LateInstallSubject.PinnedReplacement))!);

        Assert.NotEmpty(router.RawPinnedTargets());

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.Available,
            _ => new StandInBackend());

        Assert.True(chosen.Waylith);
        Assert.Same(router, DetourBackend.Current);
        Assert.IsType<StandInBackend>(router.Inner);
        Assert.NotEmpty(router.RawPinnedTargets());
    }

    [Fact]
    public void ARouterWhoseHostNotifiesItKeepsTheNotifierAndGetsTheBackend() {
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), _ => { });
        router.ActivateHost(new NotifyingHost());
        DetourBackend.Current = router;

        Assert.True(router.NotifierInstalled);

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.Available,
            _ => new StandInBackend());

        Assert.True(chosen.Waylith);
        Assert.Same(router, DetourBackend.Current);
        Assert.IsType<StandInBackend>(router.Inner);
        Assert.True(router.NotifierInstalled);
    }

    [Fact]
    public void WithNoPublisherRegisteredTheRealBackendIsRefusedRatherThanInstalledBroken() {
        var publisherBefore = WaylithDetourBackend.Publisher;
        WaylithDetourBackend.Publisher = null;
        IDetourBackend previous = DetourBackend.Current;

        try {
            Selection chosen = AdapterEntryPoint.Install(
                _ => {
                },
                null,
                [AppContext.BaseDirectory],
                BodyReplacement.Available,
                null);

            Assert.False(chosen.Waylith);
            Assert.Same(previous, DetourBackend.Current);
            Assert.Contains("has nothing to write a method body with", chosen.Reason);
        } finally {
            WaylithDetourBackend.Publisher = publisherBefore;
        }
    }

    [Fact]
    public void InstallRunsOnceAndASecondCallNeitherLogsNorReinstalls() {
        List<string> written = new List<string>();
        StandInBackend first = new StandInBackend();

        Selection once = AdapterEntryPoint.Install(
            written.Add, null, [AppContext.BaseDirectory], BodyReplacement.Available, _ => first);
        Selection twice = AdapterEntryPoint.Install(
            written.Add, false, [AppContext.BaseDirectory], BodyReplacement.ApiAbsent, _ => new StandInBackend());

        Assert.Same(once, twice);
        Assert.Same(once, AdapterEntryPoint.Current);
        Assert.Same(first, DetourBackend.Current);
        Assert.Single(written);
    }

    [Fact]
    public void WithNoSearchDirectoriesTheMissingSymbolPathIsReportedBeforeTheSelection() {
        List<string> written = new List<string>();

        Selection chosen = AdapterEntryPoint.Install(written.Add, null, null, BodyReplacement.ApiAbsent, null);

        Assert.Equal(2, written.Count);
        Assert.Contains("no assembly directories were supplied", written[0]);
        Assert.Equal(chosen.Message, written[1]);
    }
}

public sealed class LocalNameResolverTests {
    [Fact]
    public void AByteLoadedModuleResolvesToTheFileTheSearchPathHolds() {
        string path = typeof(AdapterEntryPoint).Assembly.Location;
        Assembly loaded = AppDomain.CurrentDomain.Load(File.ReadAllBytes(path));
        Module module = loaded.Modules.First();

        Assert.Empty(loaded.Location);
        Assert.Equal("<Unknown>", module.Name);

        Func<Module, string?> resolver = AdapterEntryPoint.LocalNameResolver([Path.GetDirectoryName(path)!]);

        Assert.Equal(path, resolver(module));
    }

    [Fact]
    public void ADirectoryWithoutTheFileResolvesToNothingRatherThanAGuess() {
        Module module = typeof(AdapterEntryPoint).Assembly.Modules.First();
        Func<Module, string?> resolver = AdapterEntryPoint.LocalNameResolver([Path.GetTempPath()]);

        Assert.Null(resolver(module));
    }

    [Fact]
    public void TheFirstDirectoryHoldingTheFileWins() {
        string path = typeof(AdapterEntryPoint).Assembly.Location;
        Module module = typeof(AdapterEntryPoint).Assembly.Modules.First();
        Func<Module, string?> resolver =
            AdapterEntryPoint.LocalNameResolver([Path.GetTempPath(), Path.GetDirectoryName(path)!]);

        Assert.Equal(path, resolver(module));
    }

    [Fact]
    public void TheCallersArrayCannotBeChangedUnderTheResolver() {
        string path = typeof(AdapterEntryPoint).Assembly.Location;
        string[] directories = [Path.GetDirectoryName(path)!];
        Func<Module, string?> resolver = AdapterEntryPoint.LocalNameResolver(directories);

        directories[0] = Path.GetTempPath();

        Assert.Equal(path, resolver(typeof(AdapterEntryPoint).Assembly.Modules.First()));
        Assert.Throws<ArgumentNullException>(() => AdapterEntryPoint.LocalNameResolver(null!));
    }
}
