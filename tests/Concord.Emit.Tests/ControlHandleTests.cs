using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class ControlHandleTargets {
    public static int SpineRuns;

    public static void VoidWork() {
        SpineRuns++;
    }

    public static int IntWork() {
        SpineRuns++;
        return 5;
    }
}

public static class ControlHandleInjectionMethods {
    public static void CancelVoid(ControlHandle ch) {
        ch.Cancel();
    }

    public static void ReturnThenCancel(ControlHandle<int> ch) {
        ch.ReturnValue = 42;
        ch.Cancel();
    }

    public static void DoubleReturn(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue * 2;
    }

    public static void CancelWithoutReturn(ControlHandle<int> ch) {
        ch.Cancel();
    }
}

public sealed class ControlHandleTests {
    [Fact]
    public void Compose_VoidHeadCancel_SkipsSpine() {
        MethodBase target = typeof(ControlHandleTargets).GetMethod(nameof(ControlHandleTargets.VoidWork))!;
        MethodBase injectionMethod = typeof(ControlHandleInjectionMethods).GetMethod(nameof(ControlHandleInjectionMethods.CancelVoid))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ControlHandleTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        result.Wrapper.Invoke(null, []);

        Assert.Equal(0, ControlHandleTargets.SpineRuns);
    }

    [Fact]
    public void Compose_NonVoidHeadReturnThenCancel_ReturnsValueSkipsSpine() {
        MethodBase target = typeof(ControlHandleTargets).GetMethod(nameof(ControlHandleTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlHandleInjectionMethods).GetMethod(nameof(ControlHandleInjectionMethods.ReturnThenCancel))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ControlHandleTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(42, value);
        Assert.Equal(0, ControlHandleTargets.SpineRuns);
    }

    [Fact]
    public void Compose_NonVoidReturnInjection_DoublesSpineResult() {
        MethodBase target = typeof(ControlHandleTargets).GetMethod(nameof(ControlHandleTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlHandleInjectionMethods).GetMethod(nameof(ControlHandleInjectionMethods.DoubleReturn))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ControlHandleTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [ret]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(10, value);
        Assert.Equal(1, ControlHandleTargets.SpineRuns);
    }

    [Fact]
    public void Compose_NonVoidReturnInjection_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(ControlHandleTargets).GetMethod(nameof(ControlHandleTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlHandleInjectionMethods).GetMethod(nameof(ControlHandleInjectionMethods.DoubleReturn))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret]);
        Func<int> invoke = result.Wrapper.CreateDelegate<Func<int>>();
        invoke();

        long before = GC.GetAllocatedBytesForCurrentThread();
        invoke();
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Compose_NonVoidCancelWithoutReturn_ThrowsCONC012() {
        MethodBase target = typeof(ControlHandleTargets).GetMethod(nameof(ControlHandleTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlHandleInjectionMethods).GetMethod(nameof(ControlHandleInjectionMethods.CancelWithoutReturn))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC012", ex.Code);
    }
}
