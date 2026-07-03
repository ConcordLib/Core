using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class ControlReturnTargets {
    public static int SpineRuns;

    public static void VoidWork() {
        SpineRuns++;
    }

    public static int IntWork() {
        SpineRuns++;
        return 5;
    }
}

public static class ControlReturnInjectionMethods {
    public static int FinallyRan;

    public static Control CancelAlways() {
        return Control.Cancel;
    }

    public static Control ContinueAlways() {
        return Control.Continue;
    }

    public static Control CancelWithValue(ControlHandle<int> ch) {
        ch.ReturnValue = 42;
        return Control.Cancel;
    }

    public static Control CancelWithoutValue(ControlHandle<int> ch) {
        return Control.Cancel;
    }

    public static Control CancelWithFinally() {
        try {
            return Control.Cancel;
        }
        finally {
            FinallyRan++;
        }
    }
}

public static class ControlInvokeScratchHelper {
    public static void Step() {
    }
}

public class ControlInvokeScratchTarget {
    public void Run() {
        ControlInvokeScratchHelper.Step();
    }
}

public sealed class ControlReturnTests {
    [Fact]
    public void Compose_ControlCancel_SkipsSpine() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.VoidWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.CancelAlways))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ControlReturnTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        result.Wrapper.Invoke(null, []);

        Assert.Equal(0, ControlReturnTargets.SpineRuns);
    }

    [Fact]
    public void Compose_ControlContinue_RunsSpine() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.VoidWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.ContinueAlways))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ControlReturnTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        result.Wrapper.Invoke(null, []);

        Assert.Equal(1, ControlReturnTargets.SpineRuns);
    }

    [Fact]
    public void Compose_ControlCancelWithReturnValue_ReturnsValueSkipsSpine() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.CancelWithValue))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ControlReturnTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(42, value);
        Assert.Equal(0, ControlReturnTargets.SpineRuns);
    }

    [Fact]
    public void Compose_StackedHeads_ContinueDoesNotUncancel() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.VoidWork))!;
        MethodBase cancel = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.CancelAlways))!;
        MethodBase cont = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.ContinueAlways))!;
        Injection first = new Injection(cancel, new InjectAt.Head(), "test", 0);
        Injection second = new Injection(cont, new InjectAt.Head(), "test", 1);

        ControlReturnTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [first, second]);
        result.Wrapper.Invoke(null, []);

        Assert.Equal(0, ControlReturnTargets.SpineRuns);
    }

    [Fact]
    public void Compose_ControlReturn_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.VoidWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.ContinueAlways))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);
        Action invoke = result.Wrapper.CreateDelegate<Action>();
        invoke();

        long before = TestPolyfills.GetAllocatedBytes();
        invoke();
        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Compose_ControlOnTail_ThrowsCONC015() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.CancelAlways))!;
        Injection tail = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [tail]));

        Assert.Equal("CONC015", ex.Code);
    }

    [Fact]
    public void Compose_ControlOnReturnSite_ThrowsCONC015() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.CancelAlways))!;
        Injection returnSite = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [returnSite]));

        Assert.Equal("CONC015", ex.Code);
    }

    [Fact]
    public void Compose_ControlOnAround_ThrowsCONC015() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.CancelAlways))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around]));

        Assert.Equal("CONC015", ex.Code);
    }

    [Fact]
    public void Compose_ControlOnInvoke_ThrowsCONC015() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.CancelAlways))!;
        Injection invoke = new Injection(injectionMethod, new InjectAt.Invoke(typeof(ControlInvokeScratchHelper), nameof(ControlInvokeScratchHelper.Step), At.Head, 0), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [invoke]));

        Assert.Equal("CONC015", ex.Code);
    }

    [Fact]
    public void Compose_ControlCancelWithFinally_RunsFinallyAndSkipsSpine() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.VoidWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.CancelWithFinally))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ControlReturnTargets.SpineRuns = 0;
        ControlReturnInjectionMethods.FinallyRan = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        result.Wrapper.Invoke(null, []);

        Assert.Equal(0, ControlReturnTargets.SpineRuns);
        Assert.Equal(1, ControlReturnInjectionMethods.FinallyRan);
    }

    [Fact]
    public void Compose_ControlCancelWithoutReturnValueOnNonVoid_ThrowsCONC012() {
        MethodBase target = typeof(ControlReturnTargets).GetMethod(nameof(ControlReturnTargets.IntWork))!;
        MethodBase injectionMethod = typeof(ControlReturnInjectionMethods).GetMethod(nameof(ControlReturnInjectionMethods.CancelWithoutValue))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC012", ex.Code);
    }
}
