using System;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class ProfilerLog {
    public static readonly List<string> Entries = [];

    public static void Clear() {
        Entries.Clear();
    }

    public static void Start() {
        Entries.Add("start");
    }

    public static void Stop() {
        Entries.Add("stop");
    }
}

public static class ThrowingHandlerTarget {
    public static int Compute(int x) {
        try {
            if (x < 0) {
                throw new InvalidOperationException("negative");
            }

            return x * 2;
        } finally {
            ProfilerLog.Entries.Add("target-finally");
        }
    }
}

public static class ProfilerAroundInjectionMethods {
    public static int Wrap(int x, Operation<int, int> original) {
        ProfilerLog.Start();
        try {
            return original.Invoke(x);
        } finally {
            ProfilerLog.Stop();
        }
    }
}

public sealed class AroundInjectionFinallyTests {
    [Fact]
    public void Around_InjectionOwnedTryFinally_RunsStopOnNormalReturn() {
        ComposeResult result = Compose();
        ProfilerLog.Clear();

        Assert.Equal(10, result.Wrapper.Invoke(null, [5]));
        Assert.Equal(["start", "target-finally", "stop"], ProfilerLog.Entries);
    }

    [Fact]
    public void Around_InjectionOwnedTryFinally_RunsStopWhenTheTargetThrows() {
        ComposeResult result = Compose();
        ProfilerLog.Clear();

        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(() => result.Wrapper.Invoke(null, [-1]));

        Assert.IsType<InvalidOperationException>(thrown.InnerException);
        Assert.Equal(["start", "target-finally", "stop"], ProfilerLog.Entries);
    }

    private static ComposeResult Compose() {
        MethodBase target = typeof(ThrowingHandlerTarget).GetMethod(nameof(ThrowingHandlerTarget.Compute))!;
        MethodBase injectionMethod = typeof(ProfilerAroundInjectionMethods).GetMethod(nameof(ProfilerAroundInjectionMethods.Wrap))!;

        return WrapperComposer.Compose(target, [new Injection(injectionMethod, new InjectAt.Around(), "test", 0)]);
    }
}
