using System.Reflection;
using Concord;
using Xunit;

namespace Concord.Emit.Tests;

public static class TailSiteTarget {
    public static int Pick(int which) {
        if (which == 0) {
            return 10;
        }

        if (which == 1) {
            return 20;
        }

        return 30;
    }
}

public static class TailVoidTarget {
    public static int Calls;

    public static void Run(int which) {
        if (which == 0) {
            return;
        }

        Calls++;
    }
}

public static class TailInjectionMethods {
    public static void Double(ControlHandle<int> ch) {
        ch.ReturnValue *= 2;
    }

    public static void Bump(ControlHandle ch) {
        TailVoidTarget.Calls += 100;
    }

    public static int AroundPick(int which, ControlHandle<int> ch) {
        int result = TailSiteTarget.Pick(which);
        return result * 10;
    }
}

public static class MixedReturnLog {
    public static int FinallyRuns;
}

public static class MixedReturnTarget {
    public static int PickMixed(int which) {
        if (which != 0) {
            return 1;
        }

        try {
            return 5;
        } finally {
            MixedReturnLog.FinallyRuns++;
        }
    }
}

public static class MixedReturnInjectionMethods {
    public static void Double(ControlHandle<int> ch) {
        ch.ReturnValue *= 2;
    }
}

public static class NestedTryFinallyLog {
    public static int FinallyRuns;
}

public static class NestedTryFinallyTarget {
    public static int PickNested(int which) {
        try {
            try {
                if (which == 0) {
                    return 5;
                }
            } finally {
                NestedTryFinallyLog.FinallyRuns++;
            }

            return 11;
        } finally {
            NestedTryFinallyLog.FinallyRuns++;
        }
    }
}

public static class NestedTryFinallyInjectionMethods {
    public static void Double(ControlHandle<int> ch) {
        ch.ReturnValue *= 2;
    }
}

public static class MultiReturnInTryLog {
    public static int FinallyRuns;
}

public static class MultiReturnInTryTarget {
    public static int PickMulti(int which) {
        try {
            if (which == 0) {
                return 1;
            }

            if (which == 1) {
                return 2;
            }

            return 3;
        } finally {
            MultiReturnInTryLog.FinallyRuns++;
        }
    }
}

public static class MultiReturnInTryInjectionMethods {
    public static void Double(ControlHandle<int> ch) {
        ch.ReturnValue *= 2;
    }
}

public sealed class TailSiteTests {
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 60)]
    public void Tail_FiresOnlyAtLastReturn(int which, int expected) {
        MethodBase target = typeof(TailSiteTarget).GetMethod(nameof(TailSiteTarget.Pick))!;
        MethodBase injectionMethod = typeof(TailInjectionMethods).GetMethod(nameof(TailInjectionMethods.Double))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Tail_ReplacesValue_AtLastReturnOnly() {
        MethodBase target = typeof(TailSiteTarget).GetMethod(nameof(TailSiteTarget.Pick))!;
        MethodBase injectionMethod = typeof(TailInjectionMethods).GetMethod(nameof(TailInjectionMethods.Double))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);

        Assert.Equal(10, result.Wrapper.Invoke(null, [0]));
        Assert.Equal(20, result.Wrapper.Invoke(null, [1]));
        Assert.Equal(60, result.Wrapper.Invoke(null, [2]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 101)]
    public void Tail_Void_FiresOnlyWhenLastReturnReached(int which, int expectedCalls) {
        TailVoidTarget.Calls = 0;
        MethodBase target = typeof(TailVoidTarget).GetMethod(nameof(TailVoidTarget.Run))!;
        MethodBase injectionMethod = typeof(TailInjectionMethods).GetMethod(nameof(TailInjectionMethods.Bump))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expectedCalls, TailVoidTarget.Calls);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 200)]
    [InlineData(2, 300)]
    public void Tail_WithAround_TailIsCurrentlyDropped(int which, int expected) {
        MethodBase target = typeof(TailSiteTarget).GetMethod(nameof(TailSiteTarget.Pick))!;
        MethodBase aroundInjectionMethod = typeof(TailInjectionMethods).GetMethod(nameof(TailInjectionMethods.AroundPick))!;
        MethodBase tailInjectionMethod = typeof(TailInjectionMethods).GetMethod(nameof(TailInjectionMethods.Double))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);
        Injection tail = new Injection(tailInjectionMethod, new InjectAt.Tail(), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [around, tail]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 1)]
    public void Tail_MixedProtectedAndUnprotectedReturns_FiresOnlyOnLastReturn(int which, int expected) {
        MixedReturnLog.FinallyRuns = 0;
        MethodBase target = typeof(MixedReturnTarget).GetMethod(nameof(MixedReturnTarget.PickMixed))!;
        MethodBase injectionMethod = typeof(MixedReturnInjectionMethods).GetMethod(nameof(MixedReturnInjectionMethods.Double))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
        Assert.Equal(which == 0 ? 1 : 0, MixedReturnLog.FinallyRuns);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 2)]
    public void Return_MixedProtectedAndUnprotectedReturns_TransformsEverySite(int which, int expected) {
        MixedReturnLog.FinallyRuns = 0;
        MethodBase target = typeof(MixedReturnTarget).GetMethod(nameof(MixedReturnTarget.PickMixed))!;
        MethodBase injectionMethod = typeof(MixedReturnInjectionMethods).GetMethod(nameof(MixedReturnInjectionMethods.Double))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
        Assert.Equal(which == 0 ? 1 : 0, MixedReturnLog.FinallyRuns);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 22)]
    public void Tail_NestedTryFinally_FiresOnlyAtLastReturn(int which, int expected) {
        NestedTryFinallyLog.FinallyRuns = 0;
        MethodBase target = typeof(NestedTryFinallyTarget).GetMethod(nameof(NestedTryFinallyTarget.PickNested))!;
        MethodBase injectionMethod = typeof(NestedTryFinallyInjectionMethods).GetMethod(nameof(NestedTryFinallyInjectionMethods.Double))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
        Assert.Equal(2, NestedTryFinallyLog.FinallyRuns);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 22)]
    public void Return_NestedTryFinally_TransformsEverySite(int which, int expected) {
        NestedTryFinallyLog.FinallyRuns = 0;
        MethodBase target = typeof(NestedTryFinallyTarget).GetMethod(nameof(NestedTryFinallyTarget.PickNested))!;
        MethodBase injectionMethod = typeof(NestedTryFinallyInjectionMethods).GetMethod(nameof(NestedTryFinallyInjectionMethods.Double))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
        Assert.Equal(2, NestedTryFinallyLog.FinallyRuns);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 6)]
    public void Tail_MultipleReturnsInOneTry_FiresOnlyAtLastReturn(int which, int expected) {
        MultiReturnInTryLog.FinallyRuns = 0;
        MethodBase target = typeof(MultiReturnInTryTarget).GetMethod(nameof(MultiReturnInTryTarget.PickMulti))!;
        MethodBase injectionMethod = typeof(MultiReturnInTryInjectionMethods).GetMethod(nameof(MultiReturnInTryInjectionMethods.Double))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
        Assert.Equal(1, MultiReturnInTryLog.FinallyRuns);
    }
}
