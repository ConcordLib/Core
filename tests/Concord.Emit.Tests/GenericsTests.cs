using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class Box<T> {
    private readonly T _value;
    public Box(T value) { _value = value; }

    public T Get() {
        return _value;
    }
}

public sealed class GenericsTests {
    [Fact]
    public void Compose_ClosedGenericSpine_IntReturnsCorrectValue() {
        MethodBase target = typeof(Box<int>).GetMethod(nameof(Box<int>.Get))!;

        Box<int> instance = new Box<int>(5);
        ComposeResult result = WrapperComposer.Compose(target, []);
        Func<Box<int>, int> invoke = result.Wrapper.CreateDelegate<Func<Box<int>, int>>();

        int value = invoke(instance);

        Assert.Equal(5, value);
    }

    [Fact]
    public void Compose_ClosedGenericSpine_StringReturnsCorrectValue() {
        MethodBase target = typeof(Box<string>).GetMethod(nameof(Box<string>.Get))!;

        Box<string> instance = new Box<string>("hi");
        ComposeResult result = WrapperComposer.Compose(target, []);
        Func<Box<string>, string> invoke = result.Wrapper.CreateDelegate<Func<Box<string>, string>>();

        string value = invoke(instance);

        Assert.Equal("hi", value);
    }

    [Fact]
    public void Compose_TwoClosedInstantiations_AreIndependent() {
        MethodBase intTarget = typeof(Box<int>).GetMethod(nameof(Box<int>.Get))!;
        MethodBase stringTarget = typeof(Box<string>).GetMethod(nameof(Box<string>.Get))!;

        ComposeResult intResult = WrapperComposer.Compose(intTarget, []);
        ComposeResult stringResult = WrapperComposer.Compose(stringTarget, []);

        Func<Box<int>, int> intInvoke = intResult.Wrapper.CreateDelegate<Func<Box<int>, int>>();
        Func<Box<string>, string> stringInvoke = stringResult.Wrapper.CreateDelegate<Func<Box<string>, string>>();

        Box<int> intBox = new Box<int>(5);
        Box<string> stringBox = new Box<string>("hi");

        Assert.Equal(5, intInvoke(intBox));
        Assert.Equal("hi", stringInvoke(stringBox));
    }

    [Fact]
    public void Compose_ClosedGenericIntSpine_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(Box<int>).GetMethod(nameof(Box<int>.Get))!;

        Box<int> instance = new Box<int>(5);
        ComposeResult result = WrapperComposer.Compose(target, []);
        Func<Box<int>, int> invoke = result.Wrapper.CreateDelegate<Func<Box<int>, int>>();
        invoke(instance);

        long before = TestPolyfills.GetAllocatedBytes();
        for (int i = 0; i < 10; i++) {
            invoke(instance);
        }

        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Compose_ClosedGenericStringSpine_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(Box<string>).GetMethod(nameof(Box<string>.Get))!;

        Box<string> instance = new Box<string>("hi");
        ComposeResult result = WrapperComposer.Compose(target, []);
        Func<Box<string>, string> invoke = result.Wrapper.CreateDelegate<Func<Box<string>, string>>();
        invoke(instance);

        long before = TestPolyfills.GetAllocatedBytes();
        for (int i = 0; i < 10; i++) {
            invoke(instance);
        }

        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }
}
