using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public static class NoRevertUpdater {
    public static readonly List<MethodBase> Writes = [];

    public static void ReplaceMethodBody(MethodBase method, ReadOnlySpan<byte> methodBody, ReadOnlySpan<FakeUpdater.Entry> ilOffsetMap) {
        Writes.Add(method);
    }
}

public static class UnrevertableUpdater {
    public static void ReplaceMethodBody(MethodBase method, ReadOnlySpan<byte> methodBody, ReadOnlySpan<FakeUpdater.Entry> ilOffsetMap) {
        throw new InvalidOperationException("this runtime refuses that method");
    }

    public static void Revert(MethodBase method) {
        throw new InvalidOperationException("the revert entry point is wedged");
    }
}

public static class FaultSubject {
    public static int Marker(int value) {
        return value;
    }

    public static int Unrevertable(int value) {
        return value + 11;
    }

    public static int Wedged(int value) {
        return value + 13;
    }
}

[Collection("AdapterStatics")]
public sealed class ForkBodyWriterFaultTests {
    private static readonly MethodInfo Unrevertable = typeof(FaultSubject).GetMethod(nameof(FaultSubject.Unrevertable))!;

    private static readonly MethodInfo Wedged = typeof(FaultSubject).GetMethod(nameof(FaultSubject.Wedged))!;

    [Fact]
    public void AWriterThatCannotRevertRefusesToWriteRatherThanPublishAnUnrevertableBody() {
        NoRevertUpdater.Writes.Clear();
        ForkBodyWriter writer = new ForkBodyWriter(typeof(NoRevertUpdater));
        Assert.False(writer.IsAvailable);

        PlatformNotSupportedException failure = Assert.Throws<PlatformNotSupportedException>(
            () => writer.Write(Unrevertable, [1, 2, 3], []));

        Assert.Contains("Revert", failure.Message, StringComparison.Ordinal);
        Assert.Contains("refused", failure.Message, StringComparison.Ordinal);
        Assert.Empty(NoRevertUpdater.Writes);
    }

    [Fact]
    public void PublishingThroughAWriterThatCannotRevertLeavesTheBodyAloneAndFallsBackToADetour() {
        NoRevertUpdater.Writes.Clear();
        List<string> logged = [];
        RecordingBackend detours = new RecordingBackend();

        Publish(detours, Unrevertable, typeof(NoRevertUpdater), logged);

        Assert.Empty(NoRevertUpdater.Writes);
        Assert.Single(detours.Composed);
        Assert.Contains(logged, line => line.Contains("Revert", StringComparison.Ordinal));
    }

    [Fact]
    public void AWriteThatThrowsHandsOffAndNeverClaimsAnUndoItNeverAttempted() {
        List<string> logged = [];
        RecordingBackend detours = new RecordingBackend();

        ComposedBodyPublisher publisher = Publish(detours, Wedged, typeof(UnrevertableUpdater), logged);

        Assert.True(publisher.IsHandedOff(Wedged));
        Assert.Single(detours.Composed);
        Assert.DoesNotContain(
            logged,
            line => line.StartsWith(WaylithTargetStack.UndoFailedMarker, StringComparison.Ordinal));
    }

    private static ComposedBodyPublisher Publish(
        RecordingBackend detours,
        MethodInfo target,
        Type updater,
        List<string> logged) {
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(detours, new ForkBodyWriter(updater));
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        Action<string> log = WaylithDetourBackend.Log;
        WaylithDetourBackend.Tokens = static _ => new Tokens();
        WaylithDetourBackend.Log = logged.Add;

        try {
            _ = new WaylithDetourBackend(detours, publisher, new ForkBodyWriter(updater));
            publisher.PublishComposed(
                target,
                [new Injection(typeof(FaultSubject).GetMethod(nameof(FaultSubject.Marker))!, new InjectAt.Head(), "test", 0)]);
        } finally {
            WaylithDetourBackend.Tokens = tokens;
            WaylithDetourBackend.Log = log;
        }

        return publisher;
    }
}
