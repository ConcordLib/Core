using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class WrapFoo {
    public static int BarCalls;

    public static int Bar(int x) {
        BarCalls++;
        return x + 1;
    }
}

public class WrapTarget {
    public int Run(int x) {
        return WrapFoo.Bar(x) * 10;
    }
}

public class WrapInjectionMethods {
    public int SubstituteArg(int x, Operation<int> op) {
        return op.Invoke(x * 2);
    }

    public int SkipOriginal(int x, Operation<int> op) {
        return 0;
    }
}

public sealed class InvokeWrapTests {
    [Fact]
    public void Compose_WrapInjection_SubstitutedArgRunsOriginal() {
        MethodBase target = typeof(WrapTarget).GetMethod(nameof(WrapTarget.Run))!;
        MethodBase injectionMethod = typeof(WrapInjectionMethods).GetMethod(nameof(WrapInjectionMethods.SubstituteArg))!;

        Injection injection = new Injection(injectionMethod, new InjectAt.Invoke(typeof(WrapFoo), nameof(WrapFoo.Bar), At.Around, 0), "test", 0);

        WrapFoo.BarCalls = 0;
        ComposeResult result = WrapperComposer.Compose(target, [injection]);

        WrapTarget instance = new WrapTarget();
        Func<WrapTarget, int, int> run = result.Wrapper.CreateDelegate<Func<WrapTarget, int, int>>();
        int value = run(instance, 3);

        Assert.Equal(70, value);
        Assert.Equal(1, WrapFoo.BarCalls);
    }

    [Fact]
    public void Compose_WrapInjection_SkippingInvokeReplacesResultAndSkipsOriginal() {
        MethodBase target = typeof(WrapTarget).GetMethod(nameof(WrapTarget.Run))!;
        MethodBase injectionMethod = typeof(WrapInjectionMethods).GetMethod(nameof(WrapInjectionMethods.SkipOriginal))!;

        Injection injection = new Injection(injectionMethod, new InjectAt.Invoke(typeof(WrapFoo), nameof(WrapFoo.Bar), At.Around, 0), "test", 0);

        WrapFoo.BarCalls = 0;
        ComposeResult result = WrapperComposer.Compose(target, [injection]);

        WrapTarget instance = new WrapTarget();
        Func<WrapTarget, int, int> run = result.Wrapper.CreateDelegate<Func<WrapTarget, int, int>>();
        int value = run(instance, 5);

        Assert.Equal(0, value);
        Assert.Equal(0, WrapFoo.BarCalls);
    }

    [Fact]
    public void Compose_WrapInjection_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(WrapTarget).GetMethod(nameof(WrapTarget.Run))!;
        MethodBase injectionMethod = typeof(WrapInjectionMethods).GetMethod(nameof(WrapInjectionMethods.SubstituteArg))!;

        Injection injection = new Injection(injectionMethod, new InjectAt.Invoke(typeof(WrapFoo), nameof(WrapFoo.Bar), At.Around, 0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [injection]);
        WrapTarget instance = new WrapTarget();
        Func<WrapTarget, int, int> run = result.Wrapper.CreateDelegate<Func<WrapTarget, int, int>>();
        run(instance, 3);

        long before = TestPolyfills.GetAllocatedBytes();
        for (int i = 0; i < 10; i++) {
            run(instance, 3);
        }

        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }
}
