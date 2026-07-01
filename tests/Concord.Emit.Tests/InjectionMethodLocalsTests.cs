using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class InjectionMethodLocalsTargets {
    public static int SpineRuns;

    public static int CapturedLocal;

    public static int Sum(int a, int b) {
        SpineRuns++;
        return a + b;
    }

    public static int Seed() {
        SpineRuns++;
        return 100;
    }
}

public static class InjectionMethodLocalsInjectionMethods {
    public static void HeadWithLocals(ControlHandle<int> ch, int a, int b) {
        int[] values = [a, b, a + b];
        int sum = 0;
        foreach (int value in values) {
            sum += value;
        }

        ch.ReturnValue = sum;
        ch.Cancel();
    }

    public static void ReturnWithLocals(ControlHandle<int> ch) {
        int original = ch.ReturnValue;
        int doubled = 0;
        for (int i = 0; i < 2; i++) {
            doubled += original;
        }

        ch.ReturnValue = doubled + 7;
    }

    public static void ReturnLocalIndependentOfReturnValue(ControlHandle<int> ch) {
        int scratch = ch.ReturnValue + 1;
        ch.ReturnValue = 9000;
        InjectionMethodLocalsTargets.CapturedLocal = scratch;
    }
}

public class WrapLocalsFoo {
    public static int BarCalls;

    public static int Bar(int x) {
        BarCalls++;
        return x + 1;
    }
}

public class WrapLocalsTarget {
    public int Run(int x) {
        return WrapLocalsFoo.Bar(x) * 10;
    }
}

public class WrapLocalsInjectionMethods {
    public int AccumulateThenInvoke(int x, Operation<int> op) {
        int acc = 0;
        for (int i = 0; i < 3; i++) {
            acc += x;
        }

        return op.Invoke(acc);
    }
}

public sealed class InjectionMethodLocalsTests {
    [Fact]
    public void Compose_HeadInjectionMethodWithLocals_ComputesViaLocalsAndCancels() {
        MethodBase target = typeof(InjectionMethodLocalsTargets).GetMethod(nameof(InjectionMethodLocalsTargets.Sum))!;
        MethodBase injectionMethod = typeof(InjectionMethodLocalsInjectionMethods).GetMethod(nameof(InjectionMethodLocalsInjectionMethods.HeadWithLocals))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        InjectionMethodLocalsTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        object? value = result.Wrapper.Invoke(null, [2, 3]);

        Assert.Equal(10, value);
        Assert.Equal(0, InjectionMethodLocalsTargets.SpineRuns);
    }

    [Fact]
    public void Compose_ReturnInjectionMethodWithLocals_KeepsLocalsIndependentFromReturnValue() {
        MethodBase target = typeof(InjectionMethodLocalsTargets).GetMethod(nameof(InjectionMethodLocalsTargets.Seed))!;
        MethodBase injectionMethod = typeof(InjectionMethodLocalsInjectionMethods).GetMethod(nameof(InjectionMethodLocalsInjectionMethods.ReturnWithLocals))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        InjectionMethodLocalsTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [ret]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(207, value);
        Assert.Equal(1, InjectionMethodLocalsTargets.SpineRuns);
    }

    [Fact]
    public void Compose_ReturnInjectionMethodLocal_DoesNotAliasProtocolReturnValueLocal() {
        MethodBase target = typeof(InjectionMethodLocalsTargets).GetMethod(nameof(InjectionMethodLocalsTargets.Seed))!;
        MethodBase injectionMethod =
            typeof(InjectionMethodLocalsInjectionMethods).GetMethod(nameof(InjectionMethodLocalsInjectionMethods.ReturnLocalIndependentOfReturnValue))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        InjectionMethodLocalsTargets.SpineRuns = 0;
        InjectionMethodLocalsTargets.CapturedLocal = 0;
        ComposeResult result = WrapperComposer.Compose(target, [ret]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(9000, value);
        Assert.Equal(101, InjectionMethodLocalsTargets.CapturedLocal);
        Assert.Equal(1, InjectionMethodLocalsTargets.SpineRuns);
    }

    [Fact]
    public void Compose_WrapInjectionMethodWithLocals_AccumulatesBeforeInvokingOriginal() {
        MethodBase target = typeof(WrapLocalsTarget).GetMethod(nameof(WrapLocalsTarget.Run))!;
        MethodBase injectionMethod = typeof(WrapLocalsInjectionMethods).GetMethod(nameof(WrapLocalsInjectionMethods.AccumulateThenInvoke))!;
        Injection injection = new Injection(
            injectionMethod,
            new InjectAt.Invoke(typeof(WrapLocalsFoo), nameof(WrapLocalsFoo.Bar), At.Around, 0),
            "test",
            0);

        WrapLocalsFoo.BarCalls = 0;
        ComposeResult result = WrapperComposer.Compose(target, [injection]);

        WrapLocalsTarget instance = new WrapLocalsTarget();
        Func<WrapLocalsTarget, int, int> run = result.Wrapper.CreateDelegate<Func<WrapLocalsTarget, int, int>>();
        int value = run(instance, 4);

        Assert.Equal(130, value);
        Assert.Equal(1, WrapLocalsFoo.BarCalls);
    }
}
