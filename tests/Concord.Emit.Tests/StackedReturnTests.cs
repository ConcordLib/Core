using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class StackedReturnTarget {
    public static int IntWork() {
        return 0;
    }
}

public static class StackedReturnInjectionMethods {
    public static void AddTen(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 10;
    }

    public static void AddHundred(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 100;
    }

    public static void AddTenUnlessOverThreshold(ControlHandle<int> ch) {
        if (ch.ReturnValue > 1000) {
            return;
        }

        ch.ReturnValue = ch.ReturnValue + 10;
    }
}

public sealed class StackedReturnTests {
    [Fact]
    public void Compose_TwoTailInjections_BothRunInOrder() {
        MethodBase target = typeof(StackedReturnTarget).GetMethod(nameof(StackedReturnTarget.IntWork))!;
        MethodBase first = typeof(StackedReturnInjectionMethods).GetMethod(nameof(StackedReturnInjectionMethods.AddTen))!;
        MethodBase second = typeof(StackedReturnInjectionMethods).GetMethod(nameof(StackedReturnInjectionMethods.AddHundred))!;

        Injection firstInjection = new Injection(first, new InjectAt.Tail(), "test", 0);
        Injection secondInjection = new Injection(second, new InjectAt.Tail(), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [firstInjection, secondInjection]);
        Func<int> invoke = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(110, invoke());
    }

    [Fact]
    public void Compose_TwoTailInjections_EarlyReturnInFirst_SecondStillRuns() {
        MethodBase target = typeof(StackedReturnTarget).GetMethod(nameof(StackedReturnTarget.IntWork))!;
        MethodBase first = typeof(StackedReturnInjectionMethods).GetMethod(nameof(StackedReturnInjectionMethods.AddTenUnlessOverThreshold))!;
        MethodBase second = typeof(StackedReturnInjectionMethods).GetMethod(nameof(StackedReturnInjectionMethods.AddHundred))!;

        Injection firstInjection = new Injection(first, new InjectAt.Tail(), "test", 0);
        Injection secondInjection = new Injection(second, new InjectAt.Tail(), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [firstInjection, secondInjection]);
        Func<int> invoke = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(110, invoke());
    }

    [Fact]
    public void Compose_TwoTailInjections_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(StackedReturnTarget).GetMethod(nameof(StackedReturnTarget.IntWork))!;
        MethodBase first = typeof(StackedReturnInjectionMethods).GetMethod(nameof(StackedReturnInjectionMethods.AddTen))!;
        MethodBase second = typeof(StackedReturnInjectionMethods).GetMethod(nameof(StackedReturnInjectionMethods.AddHundred))!;

        Injection firstInjection = new Injection(first, new InjectAt.Tail(), "test", 0);
        Injection secondInjection = new Injection(second, new InjectAt.Tail(), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [firstInjection, secondInjection]);
        Func<int> invoke = result.Wrapper.CreateDelegate<Func<int>>();
        invoke();

        long before = TestPolyfills.GetAllocatedBytes();
        invoke();
        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }
}
