using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class ReversePatchTarget {
    public int Value() {
        return 7;
    }

    private int Secret() {
        return 42;
    }
}

public static class ReversePatchInjectionMethods {
    public static void ReturnNinetyNineThenCancel(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public sealed class ReversePatchTests {
    [Fact]
    public void Bind_BypassesCancelingPatch_ReturnsOriginalValue() {
        MethodBase original = typeof(ReversePatchTarget).GetMethod(nameof(ReversePatchTarget.Value))!;
        MethodBase injectionMethod = typeof(ReversePatchInjectionMethods).GetMethod(nameof(ReversePatchInjectionMethods.ReturnNinetyNineThenCancel))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult composed = WrapperComposer.Compose(original, [head]);
        Func<ReversePatchTarget, int> wrapper = composed.Wrapper.CreateDelegate<Func<ReversePatchTarget, int>>();

        Func<ReversePatchTarget, int> reverse =
            (Func<ReversePatchTarget, int>)ReversePatchFactory.Bind(original, typeof(Func<ReversePatchTarget, int>));

        ReversePatchTarget instance = new ReversePatchTarget();
        Assert.Equal(99, wrapper(instance));
        Assert.Equal(7, reverse(instance));
    }

    [Fact]
    public void Bind_PrivateMethod_ReturnsCorrectValue() {
        MethodBase secret = typeof(ReversePatchTarget).GetMethod("Secret", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Func<ReversePatchTarget, int> reverse =
            (Func<ReversePatchTarget, int>)ReversePatchFactory.Bind(secret, typeof(Func<ReversePatchTarget, int>));

        ReversePatchTarget instance = new ReversePatchTarget();
        Assert.Equal(42, reverse(instance));
    }

    [Fact]
    public void Bind_ZeroAllocPerCall_OnWarmInvocation() {
        MethodBase original = typeof(ReversePatchTarget).GetMethod(nameof(ReversePatchTarget.Value))!;

        Func<ReversePatchTarget, int> reverse =
            (Func<ReversePatchTarget, int>)ReversePatchFactory.Bind(original, typeof(Func<ReversePatchTarget, int>));

        ReversePatchTarget instance = new ReversePatchTarget();
        reverse(instance);

        long before = TestPolyfills.GetAllocatedBytes();
        reverse(instance);
        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }
}
