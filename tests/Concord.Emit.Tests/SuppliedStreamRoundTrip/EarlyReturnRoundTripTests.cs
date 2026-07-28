using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests.SuppliedStreamRoundTrip;

public sealed class EarlyReturnRoundTripTests {
    [Theory]
    [InlineData(-5, -1)]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(50, 2)]
    public void HeadInjection_OnMultipleReturnSites_RoundTripsThroughStream(int input, int expected) {
        MethodBase target = typeof(EarlyReturnTargets).GetMethod(nameof(EarlyReturnTargets.Classify))!;
        MethodBase headMethod = typeof(EarlyReturnInjectionMethods).GetMethod(nameof(EarlyReturnInjectionMethods.Head))!;
        Injection head = new Injection(headMethod, new InjectAt.Head(), "spike", 0);

        EarlyReturnInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughStream(target, [head]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        Assert.Equal(["head"], EarlyReturnInjectionMethods.Log);
    }

    [Theory]
    [InlineData(-5, -1)]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(50, 2)]
    public void ReturnInjection_OnEveryReturnSite_RoundTripsThroughStream(int input, int expected) {
        MethodBase target = typeof(EarlyReturnTargets).GetMethod(nameof(EarlyReturnTargets.Classify))!;
        MethodBase returnMethod = typeof(EarlyReturnInjectionMethods).GetMethod(nameof(EarlyReturnInjectionMethods.Return))!;
        Injection returnInjection = new Injection(returnMethod, new InjectAt.Return(), "spike", 0);

        EarlyReturnInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughStream(target, [returnInjection]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        Assert.Equal(["return:" + expected], EarlyReturnInjectionMethods.Log);
    }

    [Theory]
    [InlineData(-5, -1)]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(50, 2)]
    public void TailInjection_OnlyRunsOnTrueTailSite_RoundTripsThroughStream(int input, int expected) {
        MethodBase target = typeof(EarlyReturnTargets).GetMethod(nameof(EarlyReturnTargets.Classify))!;
        MethodBase tailMethod = typeof(EarlyReturnInjectionMethods).GetMethod(nameof(EarlyReturnInjectionMethods.Tail))!;
        Injection tail = new Injection(tailMethod, new InjectAt.Tail(), "spike", 0);

        EarlyReturnInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughStream(target, [tail]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        if (input == 50) {
            Assert.Equal(["tail:2"], EarlyReturnInjectionMethods.Log);
        } else {
            Assert.Empty(EarlyReturnInjectionMethods.Log);
        }
    }

    [Theory]
    [InlineData(-5, -1)]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(50, 2)]
    public void AroundInjection_OnMultipleReturnSites_RoundTripsThroughStream(int input, int expected) {
        MethodBase target = typeof(EarlyReturnTargets).GetMethod(nameof(EarlyReturnTargets.Classify))!;
        MethodBase aroundMethod = typeof(EarlyReturnInjectionMethods).GetMethod(nameof(EarlyReturnInjectionMethods.Around))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "spike", 0);

        EarlyReturnInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughStream(target, [around]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        Assert.Equal(["pre", "post"], EarlyReturnInjectionMethods.Log);
    }
}
