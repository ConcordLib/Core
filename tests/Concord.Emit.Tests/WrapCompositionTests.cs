using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class ChainFoo {
    public static int Bar(int x) {
        return x + 1;
    }
}

public class ChainTarget {
    public int RunTwice(int x) {
        return ChainFoo.Bar(x) + ChainFoo.Bar(x + 100);
    }

    public int RunGuarded(int x) {
        try {
            return ChainFoo.Bar(x);
        } finally {
            LastFinally = x;
        }
    }

    public int LastFinally;
}

public class ChainInjectionMethods {
    public int AddTen(int a, Operation<int, int> op) {
        return op.Invoke(a) + 10;
    }

    public int AddThousand(int a, Operation<int, int> op) {
        return op.Invoke(a) + 1000;
    }
}

public sealed class WrapCompositionTests {
    private static Injection Wrap(string name, uint by, int priority) {
        MethodBase injection = typeof(ChainInjectionMethods).GetMethod(name)!;
        return new Injection(injection, new InjectAt.Invoke(typeof(ChainFoo), nameof(ChainFoo.Bar), At.Around, by), "test-" + name + priority, priority);
    }

    [Fact]
    public void ByOrdinal_WrapsOnlyTheChosenSite() {
        MethodBase target = typeof(ChainTarget).GetMethod(nameof(ChainTarget.RunTwice))!;
        ComposeResult result = WrapperComposer.Compose(target, [Wrap(nameof(ChainInjectionMethods.AddTen), 2, 0)]);
        System.Func<ChainTarget, int, int> run = result.Wrapper.CreateDelegate<System.Func<ChainTarget, int, int>>();

        Assert.Equal(3 + 1 + 103 + 1 + 10, run(new ChainTarget(), 3));
    }

    [Fact]
    public void TwoWrapsOnOneSite_ChainInPriorityOrder() {
        MethodBase target = typeof(ChainTarget).GetMethod(nameof(ChainTarget.RunGuarded))!;
        ComposeResult result = WrapperComposer.Compose(
            target,
            [Wrap(nameof(ChainInjectionMethods.AddTen), 0, 0), Wrap(nameof(ChainInjectionMethods.AddThousand), 0, 1)]);
        System.Func<ChainTarget, int, int> run = result.Wrapper.CreateDelegate<System.Func<ChainTarget, int, int>>();

        Assert.Equal(3 + 1 + 10 + 1000, run(new ChainTarget(), 3));
    }

    [Fact]
    public void WrapInsideTryFinally_PreservesHandler() {
        MethodBase target = typeof(ChainTarget).GetMethod(nameof(ChainTarget.RunGuarded))!;
        ComposeResult result = WrapperComposer.Compose(target, [Wrap(nameof(ChainInjectionMethods.AddTen), 0, 0)]);
        System.Func<ChainTarget, int, int> run = result.Wrapper.CreateDelegate<System.Func<ChainTarget, int, int>>();

        ChainTarget instance = new ChainTarget();
        Assert.Equal(3 + 1 + 10, run(instance, 3));
        Assert.Equal(3, instance.LastFinally);
    }
}
