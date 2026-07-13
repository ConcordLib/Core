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
}
