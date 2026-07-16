using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests.NeutralRoundTrip;

public sealed class StraightLineRoundTripTests {
    [Fact]
    public void HeadInjection_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(StraightLineTargets).GetMethod(nameof(StraightLineTargets.Add))!;
        MethodBase headMethod = typeof(StraightLineInjectionMethods).GetMethod(nameof(StraightLineInjectionMethods.Head))!;
        Injection head = new Injection(headMethod, new InjectAt.Head(), "spike", 0);

        StraightLineInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [head]);
        object? value = wrapper.Invoke(null, [3, 4]);

        Assert.Equal(14, value);
        Assert.Equal(["head"], StraightLineInjectionMethods.Log);
    }

    [Fact]
    public void ReturnInjection_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(StraightLineTargets).GetMethod(nameof(StraightLineTargets.Add))!;
        MethodBase returnMethod = typeof(StraightLineInjectionMethods).GetMethod(nameof(StraightLineInjectionMethods.Return))!;
        Injection returnInjection = new Injection(returnMethod, new InjectAt.Return(), "spike", 0);

        StraightLineInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [returnInjection]);
        object? value = wrapper.Invoke(null, [3, 4]);

        Assert.Equal(15, value);
        Assert.Equal(["return:14"], StraightLineInjectionMethods.Log);
    }

    [Fact]
    public void AroundInjection_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(StraightLineTargets).GetMethod(nameof(StraightLineTargets.Add))!;
        MethodBase aroundMethod = typeof(StraightLineInjectionMethods).GetMethod(nameof(StraightLineInjectionMethods.Around))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "spike", 0);

        StraightLineInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [around]);
        object? value = wrapper.Invoke(null, [3, 4]);

        Assert.Equal(14, value);
        Assert.Equal(["pre", "post"], StraightLineInjectionMethods.Log);
    }
}
