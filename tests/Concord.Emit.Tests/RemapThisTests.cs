using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class RemapThisTests {
    [Fact]
    public void Compose_HeadWithThis_RemapsToRealInstance() {
        MethodBase target = typeof(Counterish).GetMethod(nameof(Counterish.Step))!;
        MethodBase injectionMethod = typeof(CounterInjectionMethod).GetMethod(nameof(CounterInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        Counterish instance = new Counterish();
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        result.Wrapper.Invoke(null, [instance, 1]);

        // Head runs first: N += 1*10 = 10; then Step: N += 1 = 11
        Assert.Equal(11, instance.N);
    }

    [Fact]
    public void Compose_HeadWithThis_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(Counterish).GetMethod(nameof(Counterish.Step))!;
        MethodBase injectionMethod = typeof(CounterInjectionMethod).GetMethod(nameof(CounterInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        Counterish instance = new Counterish();
        ComposeResult result = WrapperComposer.Compose(target, [head]);

        Func<Counterish, int, int> invoke = result.Wrapper.CreateDelegate<Func<Counterish, int, int>>();
        invoke(instance, 1);

        long before = TestPolyfills.GetAllocatedBytes();
        invoke(instance, 1);
        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }
}
