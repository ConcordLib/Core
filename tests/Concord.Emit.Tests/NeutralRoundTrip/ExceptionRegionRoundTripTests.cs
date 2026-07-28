using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests.NeutralRoundTrip;

[Collection("ExceptionRegionTargets")]
public sealed class ExceptionRegionRoundTripTests {
    [Theory]
    [InlineData(0, -1)]
    [InlineData(5, 10)]
    public void HeadInjection_OnTryCatch_RoundTripsThroughNeutralBody(int input, int expected) {
        MethodBase target = typeof(ExceptionRegionTargets).GetMethod(nameof(ExceptionRegionTargets.TryCatch))!;
        MethodBase headMethod = typeof(ExceptionRegionInjectionMethods).GetMethod(nameof(ExceptionRegionInjectionMethods.Head))!;
        Injection head = new Injection(headMethod, new InjectAt.Head(), "spike", 0);

        ExceptionRegionTargets.Trace.Clear();
        ExceptionRegionInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [head]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        Assert.Equal(["head"], ExceptionRegionInjectionMethods.Log);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(5, 10)]
    public void AroundInjection_OnTryCatch_RoundTripsThroughNeutralBody(int input, int expected) {
        MethodBase target = typeof(ExceptionRegionTargets).GetMethod(nameof(ExceptionRegionTargets.TryCatch))!;
        MethodBase aroundMethod = typeof(ExceptionRegionInjectionMethods).GetMethod(nameof(ExceptionRegionInjectionMethods.Around))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "spike", 0);

        ExceptionRegionTargets.Trace.Clear();
        ExceptionRegionInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [around]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        Assert.Equal(["pre", "post"], ExceptionRegionInjectionMethods.Log);
    }

    [Fact]
    public void ReturnInjection_OnTryFinally_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(ExceptionRegionTargets).GetMethod(nameof(ExceptionRegionTargets.TryFinally))!;
        MethodBase returnMethod = typeof(ExceptionRegionInjectionMethods).GetMethod(nameof(ExceptionRegionInjectionMethods.Return))!;
        Injection returnInjection = new Injection(returnMethod, new InjectAt.Return(), "spike", 0);

        ExceptionRegionTargets.Trace.Clear();
        ExceptionRegionInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [returnInjection]);
        object? value = wrapper.Invoke(null, [5]);

        Assert.Equal(10, value);
        Assert.Equal(["try", "finally"], ExceptionRegionTargets.Trace);
        Assert.Equal(["return:10"], ExceptionRegionInjectionMethods.Log);
    }

    [Fact]
    public void TailInjection_OnTryFinally_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(ExceptionRegionTargets).GetMethod(nameof(ExceptionRegionTargets.TryFinally))!;
        MethodBase tailMethod = typeof(ExceptionRegionInjectionMethods).GetMethod(nameof(ExceptionRegionInjectionMethods.Tail))!;
        Injection tail = new Injection(tailMethod, new InjectAt.Tail(), "spike", 0);

        ExceptionRegionTargets.Trace.Clear();
        ExceptionRegionInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [tail]);
        object? value = wrapper.Invoke(null, [5]);

        Assert.Equal(10, value);
        Assert.Equal(["try", "finally"], ExceptionRegionTargets.Trace);
        Assert.Equal(["tail:10"], ExceptionRegionInjectionMethods.Log);
    }

    [Fact]
    public void AroundInjection_OnTryFinally_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(ExceptionRegionTargets).GetMethod(nameof(ExceptionRegionTargets.TryFinally))!;
        MethodBase aroundMethod = typeof(ExceptionRegionInjectionMethods).GetMethod(nameof(ExceptionRegionInjectionMethods.Around))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "spike", 0);

        ExceptionRegionTargets.Trace.Clear();
        ExceptionRegionInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [around]);
        object? value = wrapper.Invoke(null, [5]);

        Assert.Equal(10, value);
        Assert.Equal(["try", "finally"], ExceptionRegionTargets.Trace);
        Assert.Equal(["pre", "post"], ExceptionRegionInjectionMethods.Log);
    }

    [Theory]
    [InlineData(0, -2)]
    [InlineData(4, 12)]
    public void HeadInjection_OnNestedTryCatchFinally_RoundTripsThroughNeutralBody(int input, int expected) {
        MethodBase target = typeof(ExceptionRegionTargets).GetMethod(nameof(ExceptionRegionTargets.NestedTryCatchFinally))!;
        MethodBase headMethod = typeof(ExceptionRegionInjectionMethods).GetMethod(nameof(ExceptionRegionInjectionMethods.Head))!;
        Injection head = new Injection(headMethod, new InjectAt.Head(), "spike", 0);

        ExceptionRegionTargets.Trace.Clear();
        ExceptionRegionInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [head]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        Assert.Equal(["head"], ExceptionRegionInjectionMethods.Log);
    }

    [Theory]
    [InlineData(0, -2)]
    [InlineData(4, 12)]
    public void AroundInjection_OnNestedTryCatchFinally_RoundTripsThroughNeutralBody(int input, int expected) {
        MethodBase target = typeof(ExceptionRegionTargets).GetMethod(nameof(ExceptionRegionTargets.NestedTryCatchFinally))!;
        MethodBase aroundMethod = typeof(ExceptionRegionInjectionMethods).GetMethod(nameof(ExceptionRegionInjectionMethods.Around))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "spike", 0);

        ExceptionRegionTargets.Trace.Clear();
        ExceptionRegionInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [around]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        Assert.Equal(["pre", "post"], ExceptionRegionInjectionMethods.Log);
    }
}
