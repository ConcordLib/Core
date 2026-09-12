using System;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class FinallyLog {
    public static readonly List<string> Entries = [];

    public static void Clear() {
        Entries.Clear();
    }

    public static void Add(string entry) {
        Entries.Add(entry);
    }
}

public static class FinallyTargets {
    public static int Compute(int x) {
        try {
            if (x < 0) {
                throw new InvalidOperationException("negative");
            }

            return x * 2;
        } finally {
            FinallyLog.Add("target-finally");
        }
    }

    public static int Plain(int x) {
        if (x < 0) {
            throw new InvalidOperationException("negative");
        }

        return x + 1;
    }

    public static void Void(int x) {
        if (x < 0) {
            throw new InvalidOperationException("negative");
        }

        FinallyLog.Add("body");
    }
}

public static class FinallyInjectionMethods {
    public static void Start() {
        FinallyLog.Add("start");
    }

    public static void Stop() {
        FinallyLog.Add("stop");
    }

    public static void StopById([Bound] int sectionId) {
        FinallyLog.Add("stop:" + sectionId);
    }

    public static void CancelHead(ControlHandle ch) {
        FinallyLog.Add("head-cancel");
        ch.Cancel();
    }
}

public sealed class FinallyInjectionTests {
    [Fact]
    public void Finally_RunsAfterANormalReturn() {
        MethodInfo wrapper = Compose(nameof(FinallyTargets.Plain), head: true);
        FinallyLog.Clear();

        Assert.Equal(6, wrapper.Invoke(null, [5]));
        Assert.Equal(["start", "stop"], FinallyLog.Entries);
    }

    [Fact]
    public void Finally_RunsWhenTheTargetThrows() {
        MethodInfo wrapper = Compose(nameof(FinallyTargets.Plain), head: true);
        FinallyLog.Clear();

        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(() => wrapper.Invoke(null, [-1]));

        Assert.IsType<InvalidOperationException>(thrown.InnerException);
        Assert.Equal(["start", "stop"], FinallyLog.Entries);
    }

    [Fact]
    public void Finally_NestsAroundATargetThatOwnsHandlers() {
        MethodInfo wrapper = Compose(nameof(FinallyTargets.Compute), head: true);
        FinallyLog.Clear();

        Assert.Throws<TargetInvocationException>(() => wrapper.Invoke(null, [-1]));

        Assert.Equal(["start", "target-finally", "stop"], FinallyLog.Entries);
    }

    [Fact]
    public void Finally_RunsOnAVoidTarget() {
        MethodInfo wrapper = Compose(nameof(FinallyTargets.Void), head: true);
        FinallyLog.Clear();

        wrapper.Invoke(null, [1]);

        Assert.Equal(["start", "body", "stop"], FinallyLog.Entries);
    }

    [Fact]
    public void Finally_RunsWhenAHeadCancels() {
        MethodBase target = typeof(FinallyTargets).GetMethod(nameof(FinallyTargets.Void))!;
        Injection cancel = new Injection(
            typeof(FinallyInjectionMethods).GetMethod(nameof(FinallyInjectionMethods.CancelHead))!, new InjectAt.Head(), "test", 0);
        Injection stop = new Injection(
            typeof(FinallyInjectionMethods).GetMethod(nameof(FinallyInjectionMethods.Stop))!, new InjectAt.Finally(), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [cancel, stop]);
        FinallyLog.Clear();

        result.Wrapper.Invoke(null, [1]);

        Assert.Equal(["head-cancel", "stop"], FinallyLog.Entries);
    }

    [Fact]
    public void Finally_CarriesABoundConstant() {
        MethodBase target = typeof(FinallyTargets).GetMethod(nameof(FinallyTargets.Plain))!;
        Injection stop = new Injection(
            typeof(FinallyInjectionMethods).GetMethod(nameof(FinallyInjectionMethods.StopById))!, new InjectAt.Finally(), "test", 0) {
            BoundArguments = new Dictionary<string, object?> { ["sectionId"] = 42 },
        };

        ComposeResult result = WrapperComposer.Compose(target, [stop]);
        FinallyLog.Clear();

        result.Wrapper.Invoke(null, [1]);

        Assert.Equal(["stop:42"], FinallyLog.Entries);
    }

    [Fact]
    public void Finally_WithAWholeMethodAround_ThrowsCONC138() {
        MethodBase target = typeof(FinallyTargets).GetMethod(nameof(FinallyTargets.Plain))!;
        Injection around = new Injection(
            typeof(ProfilerAroundInjectionMethods).GetMethod(nameof(ProfilerAroundInjectionMethods.Wrap))!, new InjectAt.Around(), "test", 0);
        Injection stop = new Injection(
            typeof(FinallyInjectionMethods).GetMethod(nameof(FinallyInjectionMethods.Stop))!, new InjectAt.Finally(), "test", 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [around, stop]));

        Assert.Equal("CONC138", ex.Code);
    }

    private static MethodInfo Compose(string targetName, bool head) {
        MethodBase target = typeof(FinallyTargets).GetMethod(targetName)!;
        List<Injection> injections = [];
        if (head) {
            injections.Add(new Injection(typeof(FinallyInjectionMethods).GetMethod(nameof(FinallyInjectionMethods.Start))!, new InjectAt.Head(), "test", 0));
        }

        injections.Add(new Injection(typeof(FinallyInjectionMethods).GetMethod(nameof(FinallyInjectionMethods.Stop))!, new InjectAt.Finally(), "test", 1));

        return WrapperComposer.Compose(target, injections).Wrapper;
    }
}
