using System.Reflection;
using Concord;
using Xunit;

namespace Concord.Emit.Tests;

public static class AroundHandleTarget {
    public static int Triple(int x) {
        return x * 3;
    }

    public static void VoidStep() {
        AroundHandleLog.Entries.Add("spine");
    }

    public static async Task<int> AsyncDouble(int x) {
        await Task.Yield();
        return x * 2;
    }
}

public static class AroundHandleLog {
    public static List<string> Entries = new List<string>();

    public static void Clear() {
        Entries.Clear();
    }
}

public static class AroundHandleInjectionMethods {
    public static int WrapTriple(int x, Operation<int, int> original) {
        int inner = original.Invoke(x);
        return inner + 1;
    }

    public static int WrapTripleChangedArg(int x, Operation<int, int> original) {
        return original.Invoke(x - 2);
    }

    public static void WrapVoidStep(Operation original) {
        AroundHandleLog.Entries.Add("pre");
        original.Invoke();
        AroundHandleLog.Entries.Add("post");
    }

    public static void HeadOnAsync(ControlHandle ch) {
        AroundHandleLog.Entries.Add("head");
    }
}

public static class TryFinallyAroundTarget {
    public static int Compute(int x) {
        try {
            return x * 2;
        } finally {
            AroundHandleLog.Entries.Add("finally");
        }
    }
}

public static class MultiInvokeAroundInjectionMethods {
    public static int WrapTripleTwice(int x, Operation<int, int> original) {
        return original.Invoke(x) + original.Invoke(x + 1);
    }

    public static int WrapSkip(int x, Operation<int, int> original) {
        return 99;
    }

    public static int WrapTryFinallyTwice(int x, Operation<int, int> original) {
        int a = original.Invoke(x);
        int b = original.Invoke(x);
        return a + b;
    }

    public static int WrapMidExpressionWithHandlers(int x, Operation<int, int> original) {
        return 10 + original.Invoke(x);
    }
}

public sealed class InstanceAroundTarget {
    public int Seed;

    public InstanceAroundTarget(int seed) {
        Seed = seed;
    }

    public int AddSeed(int x) {
        return Seed + x;
    }
}

public static class InstanceAroundInjectionMethods {
    public static int WrapAddSeedChangedArg(int x, Operation<int, int> original) {
        return original.Invoke(x + 100);
    }
}

public static class ByRefParamTarget {
    public static int Add(ref int x) {
        return x + 1;
    }
}

public static class ByRefReturnTarget {
    public static int Backing;

    public static ref int GetBacking() {
        return ref Backing;
    }
}

public static class SpanParamTarget {
    public static int Sum(Span<int> values) {
        int total = 0;
        foreach (int value in values) {
            total += value;
        }

        return total;
    }
}

public static class InvalidTargetInjectionMethods {
    public static int WrapByRefParam(Operation<int, int> original) {
        return original.Invoke(0);
    }

    public static int WrapByRefReturn(Operation<int> original) {
        return original.Invoke();
    }

    public static int WrapSpanParam(Operation<int> original) {
        return original.Invoke();
    }

    public static int WrapAsync(Operation<int, int> original) {
        return original.Invoke(0);
    }
}

public static class TwoOperationParamsInjectionMethods {
    public static int WrapTriple(int x, Operation<int, int> first, Operation<int, int> second) {
        return first.Invoke(x) + second.Invoke(x);
    }
}

public static class OperationPlusControlHandleInjectionMethods {
    public static int WrapTriple(int x, Operation<int, int> original, ControlHandle<int> ch) {
        return original.Invoke(x);
    }
}

public static class StrayOperationUseInjectionMethods {
    public static int WrapTriple(int x, Operation<int, int> original) {
        Operation<int, int> captured = original;
        return captured.Invoke(x);
    }
}

public static class LoopOperationInvokeInjectionMethods {
    public static int WrapTriple(int x, Operation<int, int> original) {
        int total = 0;
        for (int i = 0; i < 2; i++) {
            total += original.Invoke(x);
        }

        return total;
    }
}

public sealed class AroundHandleTests {
    [Fact]
    public void Around_HandleForm_RunsBodyAndWrapsResult() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.Triple))!;
        MethodBase injectionMethod = typeof(AroundHandleInjectionMethods).GetMethod(nameof(AroundHandleInjectionMethods.WrapTriple))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [around]);

        Assert.Equal(16, result.Wrapper.Invoke(null, [5]));
    }

    [Fact]
    public void Around_HandleForm_InvokeChangedArg_RunsBodyWithChangedArg() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.Triple))!;
        MethodBase injectionMethod = typeof(AroundHandleInjectionMethods).GetMethod(nameof(AroundHandleInjectionMethods.WrapTripleChangedArg))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [around]);

        Assert.Equal(9, result.Wrapper.Invoke(null, [5]));
    }

    [Fact]
    public void Around_HandleForm_InstanceTarget_InvokeChangedArgKeepsAmbientThis() {
        MethodBase target = typeof(InstanceAroundTarget).GetMethod(nameof(InstanceAroundTarget.AddSeed))!;
        MethodBase injectionMethod = typeof(InstanceAroundInjectionMethods).GetMethod(nameof(InstanceAroundInjectionMethods.WrapAddSeedChangedArg))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [around]);
        Func<InstanceAroundTarget, int, int> invoke = result.Wrapper.CreateDelegate<Func<InstanceAroundTarget, int, int>>();

        InstanceAroundTarget instance = new InstanceAroundTarget(10);
        int value = invoke(instance, 5);

        Assert.Equal(115, value);
        Assert.Equal(10, instance.Seed);
    }

    [Fact]
    public void Around_HandleForm_Void_RunsBodyBetweenPreAndPost() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.VoidStep))!;
        MethodBase injectionMethod = typeof(AroundHandleInjectionMethods).GetMethod(nameof(AroundHandleInjectionMethods.WrapVoidStep))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        AroundHandleLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [around]);
        result.Wrapper.Invoke(null, []);

        Assert.Equal(["pre", "spine", "post"], AroundHandleLog.Entries);
    }

    [Fact]
    public void Compose_WholeMethodAround_ByRefParam_ThrowsCONC108() {
        MethodBase target = typeof(ByRefParamTarget).GetMethod(nameof(ByRefParamTarget.Add))!;
        MethodBase injectionMethod = typeof(InvalidTargetInjectionMethods).GetMethod(nameof(InvalidTargetInjectionMethods.WrapByRefParam))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC108", ex.Code);
    }

    [Fact]
    public void Compose_WholeMethodAround_ByRefReturn_ThrowsCONC109() {
        MethodBase target = typeof(ByRefReturnTarget).GetMethod(nameof(ByRefReturnTarget.GetBacking))!;
        MethodBase injectionMethod = typeof(InvalidTargetInjectionMethods).GetMethod(nameof(InvalidTargetInjectionMethods.WrapByRefReturn))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC109", ex.Code);
    }

    [Fact]
    public void Compose_WholeMethodAround_SpanParam_ThrowsCONC109() {
        MethodBase target = typeof(SpanParamTarget).GetMethod(nameof(SpanParamTarget.Sum))!;
        MethodBase injectionMethod = typeof(InvalidTargetInjectionMethods).GetMethod(nameof(InvalidTargetInjectionMethods.WrapSpanParam))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC109", ex.Code);
    }

    [Fact]
    public void Compose_WholeMethodAround_AsyncTarget_ThrowsCONC110() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.AsyncDouble))!;
        MethodBase injectionMethod = typeof(InvalidTargetInjectionMethods).GetMethod(nameof(InvalidTargetInjectionMethods.WrapAsync))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC110", ex.Code);
    }

    [Fact]
    public void Compose_WholeMethodAround_TwoOperationParams_ThrowsCONC111() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.Triple))!;
        MethodBase injectionMethod = typeof(TwoOperationParamsInjectionMethods).GetMethod(nameof(TwoOperationParamsInjectionMethods.WrapTriple))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC111", ex.Code);
    }

    [Fact]
    public void Compose_WholeMethodAround_OperationPlusControlHandle_ThrowsCONC111() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.Triple))!;
        MethodBase injectionMethod = typeof(OperationPlusControlHandleInjectionMethods).GetMethod(nameof(OperationPlusControlHandleInjectionMethods.WrapTriple))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC111", ex.Code);
    }

    [Fact]
    public void Compose_WholeMethodAround_StrayOperationUse_ThrowsCONC013() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.Triple))!;
        MethodBase injectionMethod = typeof(StrayOperationUseInjectionMethods).GetMethod(nameof(StrayOperationUseInjectionMethods.WrapTriple))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC013", ex.Code);
    }

    [Fact]
    public void Compose_WholeMethodAround_InvokeInLoop_ThrowsCONC113() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.Triple))!;
        MethodBase injectionMethod = typeof(LoopOperationInvokeInjectionMethods).GetMethod(nameof(LoopOperationInvokeInjectionMethods.WrapTriple))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC113", ex.Code);
    }

    [Fact]
    public void Around_WholeMethod_MultipleInvoke_RunsBodyTwiceAndSumsResults() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.Triple))!;
        MethodBase injectionMethod = typeof(MultiInvokeAroundInjectionMethods).GetMethod(nameof(MultiInvokeAroundInjectionMethods.WrapTripleTwice))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [around]);

        Assert.Equal(33, result.Wrapper.Invoke(null, [5]));
    }

    [Fact]
    public void Around_WholeMethod_ZeroInvoke_SkipsBodyAndDropsHandlers() {
        MethodBase target = typeof(TryFinallyAroundTarget).GetMethod(nameof(TryFinallyAroundTarget.Compute))!;
        MethodBase injectionMethod = typeof(MultiInvokeAroundInjectionMethods).GetMethod(nameof(MultiInvokeAroundInjectionMethods.WrapSkip))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        AroundHandleLog.Clear();
        string dump = WrapperComposer.ComposeDump(target, [around]);
        ComposeResult result = WrapperComposer.Compose(target, [around]);

        Assert.Equal(99, result.Wrapper.Invoke(null, [5]));
        Assert.Empty(AroundHandleLog.Entries);
        Assert.Contains("exceptionHandlers[0]:", dump);
    }

    [Fact]
    public void Around_WholeMethod_MultipleInvokeOverTryFinally_RunsFinallyOncePerInvoke() {
        MethodBase target = typeof(TryFinallyAroundTarget).GetMethod(nameof(TryFinallyAroundTarget.Compute))!;
        MethodBase injectionMethod = typeof(MultiInvokeAroundInjectionMethods).GetMethod(nameof(MultiInvokeAroundInjectionMethods.WrapTryFinallyTwice))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        AroundHandleLog.Clear();
        string dump = WrapperComposer.ComposeDump(target, [around]);
        ComposeResult result = WrapperComposer.Compose(target, [around]);
        object? value = result.Wrapper.Invoke(null, [5]);

        Assert.Equal(20, value);
        Assert.Equal(["finally", "finally"], AroundHandleLog.Entries);
        Assert.Contains("exceptionHandlers[2]:", dump);
    }

    [Fact]
    public void Compose_WholeMethodAround_MidExpressionInvokeOnHandlerBearingTarget_ThrowsCONC107() {
        MethodBase target = typeof(TryFinallyAroundTarget).GetMethod(nameof(TryFinallyAroundTarget.Compute))!;
        MethodBase injectionMethod = typeof(MultiInvokeAroundInjectionMethods).GetMethod(nameof(MultiInvokeAroundInjectionMethods.WrapMidExpressionWithHandlers))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC107", ex.Code);
    }

    [Fact]
    public void Compose_AsyncTarget_HeadInjection_StillComposesNormally() {
        MethodBase target = typeof(AroundHandleTarget).GetMethod(nameof(AroundHandleTarget.AsyncDouble))!;
        MethodBase injectionMethod = typeof(AroundHandleInjectionMethods).GetMethod(nameof(AroundHandleInjectionMethods.HeadOnAsync))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);

        Assert.NotNull(result.Wrapper);
        Assert.Equal("MoveNext", WrapperComposer.ResolveStateMachineTarget(target).Name);
    }
}
