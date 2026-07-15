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

    public static int AroundPick(int which, Operation<int, int> original) {
        int result = original.Invoke(which);
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

public static class TryCatchVoidTailTarget {
    public static int BodyCalls;
    public static int CatchCalls;

    public static void Run() {
        try {
            BodyCalls++;
        } catch (Exception) {
            BodyCalls = -1;
        }
    }

    public static void RunCaughtException() {
        try {
            throw new InvalidOperationException();
        } catch (InvalidOperationException) {
            CatchCalls++;
        }
    }
}

public static class TryCatchVoidTailInjectionMethods {
    public static int TailCalls;
    public static int ReturnCalls;
    public static int AroundBeforeCalls;
    public static int AroundAfterCalls;
    public static int ObservedCatchCalls;
    public static int ObservedFinallyCalls;

    public static void Mark() {
        TailCalls++;
    }

    public static void MarkReturn() {
        ReturnCalls++;
    }

    public static void Around(Operation original) {
        AroundBeforeCalls++;
        original.Invoke();
        AroundAfterCalls++;
    }

    public static void ObserveCatch() {
        ObservedCatchCalls = TryCatchVoidTailTarget.CatchCalls;
    }

    public static void ObserveFinally() {
        ObservedFinallyCalls = TryFinallyVoidTailTarget.FinallyCalls;
    }
}

public static class TryCatchValueTailTarget {
    public static int Run() {
        try {
            return 42;
        } catch (Exception) {
            throw;
        }
    }
}

public static class TryCatchValueTailInjectionMethods {
    public static int ObservedReturnValue;

    public static void Observe(ControlHandle<int> handle) {
        ObservedReturnValue = handle.ReturnValue;
    }
}

public static class TryFinallyVoidTailTarget {
    public static int BodyCalls;
    public static int FinallyCalls;

    public static void Run() {
        try {
            BodyCalls++;
        } finally {
            FinallyCalls++;
        }
    }
}

public static class TryCatchReturnPlacementTarget {
    public static int AfterTryCalls;

    public static void Run(bool returnInsideTry) {
        try {
            if (returnInsideTry) {
                return;
            }
        } catch (Exception) {
            AfterTryCalls = -1;
        }

        AfterTryCalls++;
    }
}

public sealed class TailSiteTests {
    [Fact]
    public void Tail_RunsOnNormalExit_WhenTargetBodyHasTryCatch() {
        TryCatchVoidTailTarget.BodyCalls = 0;
        TryCatchVoidTailInjectionMethods.TailCalls = 0;
        MethodBase target = typeof(TryCatchVoidTailTarget).GetMethod(nameof(TryCatchVoidTailTarget.Run))!;
        MethodBase injectionMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.Mark))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        result.Wrapper.Invoke(null, null);

        Assert.Equal((1, 1), (TryCatchVoidTailTarget.BodyCalls, TryCatchVoidTailInjectionMethods.TailCalls));
    }

    [Fact]
    public void Tail_ReadsReturnValueOnNormalExit_WhenNonVoidTargetBodyHasTryCatch() {
        TryCatchValueTailInjectionMethods.ObservedReturnValue = 0;
        MethodBase target = typeof(TryCatchValueTailTarget).GetMethod(nameof(TryCatchValueTailTarget.Run))!;
        MethodBase injectionMethod = typeof(TryCatchValueTailInjectionMethods).GetMethod(nameof(TryCatchValueTailInjectionMethods.Observe))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        object? returnValue = result.Wrapper.Invoke(null, null);

        Assert.Equal((42, 42), (returnValue, TryCatchValueTailInjectionMethods.ObservedReturnValue));
    }

    [Fact]
    public void Tail_RunsAfterFinally_WhenTargetBodyHasTryFinally() {
        TryFinallyVoidTailTarget.BodyCalls = 0;
        TryFinallyVoidTailTarget.FinallyCalls = 0;
        TryCatchVoidTailInjectionMethods.ObservedFinallyCalls = 0;
        MethodBase target = typeof(TryFinallyVoidTailTarget).GetMethod(nameof(TryFinallyVoidTailTarget.Run))!;
        MethodBase injectionMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.ObserveFinally))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        result.Wrapper.Invoke(null, null);

        Assert.Equal((1, 1, 1), (TryFinallyVoidTailTarget.BodyCalls, TryFinallyVoidTailTarget.FinallyCalls, TryCatchVoidTailInjectionMethods.ObservedFinallyCalls));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void Tail_RunsOnFinalExit_WhenTargetReturnsInsideOrAfterTry(bool returnInsideTry, int expectedAfterTryCalls) {
        TryCatchReturnPlacementTarget.AfterTryCalls = 0;
        TryCatchVoidTailInjectionMethods.TailCalls = 0;
        MethodBase target = typeof(TryCatchReturnPlacementTarget).GetMethod(nameof(TryCatchReturnPlacementTarget.Run))!;
        MethodBase injectionMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.Mark))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        result.Wrapper.Invoke(null, [returnInsideTry]);

        Assert.Equal((expectedAfterTryCalls, 1), (TryCatchReturnPlacementTarget.AfterTryCalls, TryCatchVoidTailInjectionMethods.TailCalls));
    }

    [Fact]
    public void Tail_RunsAfterCaughtException_WhenTargetCompletesNormally() {
        TryCatchVoidTailTarget.CatchCalls = 0;
        TryCatchVoidTailInjectionMethods.ObservedCatchCalls = 0;
        MethodBase target = typeof(TryCatchVoidTailTarget).GetMethod(nameof(TryCatchVoidTailTarget.RunCaughtException))!;
        MethodBase injectionMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.ObserveCatch))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        result.Wrapper.Invoke(null, null);

        Assert.Equal((1, 1), (TryCatchVoidTailTarget.CatchCalls, TryCatchVoidTailInjectionMethods.ObservedCatchCalls));
    }

    [Fact]
    public void Return_RunsOnNormalExit_WhenTargetBodyHasTryCatch() {
        TryCatchVoidTailTarget.BodyCalls = 0;
        TryCatchVoidTailInjectionMethods.ReturnCalls = 0;
        MethodBase target = typeof(TryCatchVoidTailTarget).GetMethod(nameof(TryCatchVoidTailTarget.Run))!;
        MethodBase injectionMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.MarkReturn))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret]);
        result.Wrapper.Invoke(null, null);

        Assert.Equal((1, 1), (TryCatchVoidTailTarget.BodyCalls, TryCatchVoidTailInjectionMethods.ReturnCalls));
    }

    [Fact]
    public void Around_WrapsNormalExit_WhenTargetBodyHasTryCatch() {
        TryCatchVoidTailTarget.BodyCalls = 0;
        TryCatchVoidTailInjectionMethods.AroundBeforeCalls = 0;
        TryCatchVoidTailInjectionMethods.AroundAfterCalls = 0;
        MethodBase target = typeof(TryCatchVoidTailTarget).GetMethod(nameof(TryCatchVoidTailTarget.Run))!;
        MethodBase injectionMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.Around))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [around]);
        result.Wrapper.Invoke(null, null);

        Assert.Equal(
            (1, 1, 1),
            (TryCatchVoidTailTarget.BodyCalls, TryCatchVoidTailInjectionMethods.AroundBeforeCalls, TryCatchVoidTailInjectionMethods.AroundAfterCalls));
    }

    [Fact]
    public void Tail_WithAround_RunsOnNormalExit_WhenTargetBodyHasTryCatch() {
        TryCatchVoidTailTarget.BodyCalls = 0;
        TryCatchVoidTailInjectionMethods.TailCalls = 0;
        MethodBase target = typeof(TryCatchVoidTailTarget).GetMethod(nameof(TryCatchVoidTailTarget.Run))!;
        MethodBase aroundMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.Around))!;
        MethodBase tailMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.Mark))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "test", 0);
        Injection tail = new Injection(tailMethod, new InjectAt.Tail(), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [around, tail]);
        result.Wrapper.Invoke(null, null);

        Assert.Equal((1, 1), (TryCatchVoidTailTarget.BodyCalls, TryCatchVoidTailInjectionMethods.TailCalls));
    }

    [Fact]
    public void Return_WithAround_RunsOnNormalExit_WhenTargetBodyHasTryCatch() {
        TryCatchVoidTailTarget.BodyCalls = 0;
        TryCatchVoidTailInjectionMethods.ReturnCalls = 0;
        MethodBase target = typeof(TryCatchVoidTailTarget).GetMethod(nameof(TryCatchVoidTailTarget.Run))!;
        MethodBase aroundMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.Around))!;
        MethodBase returnMethod = typeof(TryCatchVoidTailInjectionMethods).GetMethod(nameof(TryCatchVoidTailInjectionMethods.MarkReturn))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "test", 0);
        Injection ret = new Injection(returnMethod, new InjectAt.Return(0), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [around, ret]);
        result.Wrapper.Invoke(null, null);

        Assert.Equal((1, 1), (TryCatchVoidTailTarget.BodyCalls, TryCatchVoidTailInjectionMethods.ReturnCalls));
    }

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
    [InlineData(2, 600)]
    public void Tail_WithAround_FiresBeforePostCode(int which, int expected) {
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
