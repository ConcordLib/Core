using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests.NeutralRoundTrip;

public sealed class BranchingRoundTripTests {
    [Theory]
    [InlineData(-5, -2)]
    [InlineData(0, 0)]
    [InlineData(7, 2)]
    public void HeadInjection_OnIfElseChain_RoundTripsThroughNeutralBody(int input, int expected) {
        MethodBase target = typeof(BranchingTargets).GetMethod(nameof(BranchingTargets.ClassifyAndDouble))!;
        MethodBase headMethod = typeof(BranchingInjectionMethods).GetMethod(nameof(BranchingInjectionMethods.Head))!;
        Injection head = new Injection(headMethod, new InjectAt.Head(), "spike", 0);

        BranchingInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [head]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        Assert.Equal(["head"], BranchingInjectionMethods.Log);
    }

    [Theory]
    [InlineData(-5, -2)]
    [InlineData(0, 0)]
    [InlineData(7, 2)]
    public void AroundInjection_OnIfElseChain_RoundTripsThroughNeutralBody(int input, int expected) {
        MethodBase target = typeof(BranchingTargets).GetMethod(nameof(BranchingTargets.ClassifyAndDouble))!;
        MethodBase aroundMethod = typeof(BranchingInjectionMethods).GetMethod(nameof(BranchingInjectionMethods.AroundClassify))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "spike", 0);

        BranchingInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [around]);
        object? value = wrapper.Invoke(null, [input]);

        Assert.Equal(expected, value);
        Assert.Equal(["pre", "post"], BranchingInjectionMethods.Log);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 30)]
    [InlineData(3, 40)]
    [InlineData(99, -1)]
    public void ReturnInjection_OnSwitch_RoundTripsThroughNeutralBody(int which, int expected) {
        MethodBase target = typeof(BranchingTargets).GetMethod(nameof(BranchingTargets.SwitchOnValue))!;
        MethodBase returnMethod = typeof(BranchingInjectionMethods).GetMethod(nameof(BranchingInjectionMethods.Return))!;
        Injection returnInjection = new Injection(returnMethod, new InjectAt.Return(), "spike", 0);

        BranchingInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [returnInjection]);
        object? value = wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
        Assert.Equal(["return:" + expected], BranchingInjectionMethods.Log);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 30)]
    [InlineData(3, 40)]
    [InlineData(99, -1)]
    public void AroundInjection_OnSwitch_RoundTripsThroughNeutralBody(int which, int expected) {
        MethodBase target = typeof(BranchingTargets).GetMethod(nameof(BranchingTargets.SwitchOnValue))!;
        MethodBase aroundMethod = typeof(BranchingInjectionMethods).GetMethod(nameof(BranchingInjectionMethods.AroundSwitch))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "spike", 0);

        BranchingInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [around]);
        object? value = wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
        Assert.Equal(["pre", "post"], BranchingInjectionMethods.Log);
    }
}
