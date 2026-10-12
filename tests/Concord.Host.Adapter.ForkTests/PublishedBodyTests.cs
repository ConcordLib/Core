using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Published {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Twice() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Reverted() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Generic<T>() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadBack() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Lazy() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int OffThread() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int StackShort() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int StackGenerous() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Counters() {
        return 1;
    }
}

public sealed class PublishedBodyTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so nothing can be published.";

    private const int Terms = 12;

    private const int Short = 9;

    private readonly List<MethodBase> written = [];

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        foreach (MethodBase target in written) {
            ForkRuntime.Restore(target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ASecondPublishReplacesTheFirstRatherThanChainingOntoIt() {
        MethodInfo target = typeof(Published).GetMethod(nameof(Published.Twice))!;

        Write(target, 5);
        Assert.Equal(5, Published.Twice());

        ForkRuntime.Write(target, 9);

        Assert.Equal(9, Published.Twice());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void RevertGoesBackToTheOriginalBodyNotThePreviousOne() {
        MethodInfo target = typeof(Published).GetMethod(nameof(Published.Reverted))!;

        ForkRuntime.Write(target, 5);
        ForkRuntime.Write(target, 9);
        Assert.Equal(9, Published.Reverted());

        ForkRuntime.Restore(target);

        Assert.Equal(1, Published.Reverted());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void OneBodyOnTheTypicalDefinitionReachesEveryInstantiation() {
        MethodInfo typical = typeof(Published).GetMethod(nameof(Published.Generic))!;
        Assert.True(typical.IsGenericMethodDefinition);

        Assert.Equal(1, Published.Generic<string>());
        Assert.Equal(1, Published.Generic<int>());

        Write(typical, 5);

        Assert.Equal(5, Published.Generic<string>());
        Assert.Equal(5, Published.Generic<int>());
        Assert.Equal(5, Published.Generic<DateTime>());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AReadReturnsTheOriginalBodyNotTheLiveOne() {
        MethodInfo target = typeof(Published).GetMethod(nameof(Published.ReadBack))!;
        byte[] before = target.GetMethodBody()!.GetILAsByteArray()!;

        Write(target, 5);
        Assert.Equal(5, Published.ReadBack());

        byte[] after = target.GetMethodBody()!.GetILAsByteArray()!;

        Assert.Equal(before, after);
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void PublishingABodyTheJitWillRejectStillReturnsCleanly() {
        MethodInfo target = typeof(Published).GetMethod(nameof(Published.Lazy))!;

        ForkRuntime.WriteSum(target, Terms, Short);
        written.Add(target);
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void PublishingWorksOffTheMainThread() {
        MethodInfo target = typeof(Published).GetMethod(nameof(Published.OffThread))!;

        Thread worker = new Thread(() => Write(target, 5));
        worker.Start();
        worker.Join();

        Assert.Equal(5, Published.OffThread());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AMaxStackShortOfWhatTheBodyNeedsThrowsAtTheFirstCall() {
        MethodInfo target = typeof(Published).GetMethod(nameof(Published.StackShort))!;

        ForkRuntime.WriteSum(target, Terms, Short);
        written.Add(target);

        Assert.Throws<InvalidProgramException>(() => Published.StackShort());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AGenerousMaxStackRunsTheSameAsAnExactOne() {
        MethodInfo target = typeof(Published).GetMethod(nameof(Published.StackGenerous))!;

        ForkRuntime.WriteSum(target, Terms, 65000);
        written.Add(target);

        Assert.Equal(Terms, Published.StackGenerous());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void PublishingABodyLeavesTheUpdateCountersFlat() {
        MethodInfo target = typeof(Published).GetMethod(nameof(Published.Counters))!;
        int version = ForkRuntime.MetadataUpdateVersion();
        int[] tokens = ForkRuntime.UpdatedMethodTokens(target.Module);

        Write(target, 5);

        Assert.Equal(5, Published.Counters());
        Assert.Equal(version, ForkRuntime.MetadataUpdateVersion());
        Assert.Equal(tokens, ForkRuntime.UpdatedMethodTokens(target.Module));
    }

    private void Write(MethodBase target, int value) {
        ForkRuntime.Write(target, value);
        written.Add(target);
    }

    private void Write(MethodBase target, int value, int maxStack) {
        ForkRuntime.Write(target, value, maxStack);
        written.Add(target);
    }
}
