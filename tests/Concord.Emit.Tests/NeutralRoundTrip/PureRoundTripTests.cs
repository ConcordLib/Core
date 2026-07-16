using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests.NeutralRoundTrip;

[Collection("ExceptionRegionTargets")]
public class PureRoundTripTests {
    private static MethodInfo RoundTripOf(Type declaring, string name) {
        MethodBase target = declaring.GetMethod(name)!;
        NeutralBody neutral = RoundTrip.Extract(target);
        return RoundTrip.Generate(neutral, target);
    }

    [Fact]
    public void StraightLine_RoundTripsUnchanged() {
        MethodInfo generated = RoundTripOf(typeof(StraightLineTargets), nameof(StraightLineTargets.Add));
        Assert.Equal(14, generated.Invoke(null, [3, 4]));
    }

    [Theory]
    [InlineData(-5, -2)]
    [InlineData(0, 0)]
    [InlineData(7, 2)]
    public void Branching_RoundTripsUnchanged(int input, int expected) {
        MethodInfo generated = RoundTripOf(typeof(BranchingTargets), nameof(BranchingTargets.ClassifyAndDouble));
        Assert.Equal(expected, generated.Invoke(null, [input]));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 30)]
    [InlineData(3, 40)]
    [InlineData(99, -1)]
    public void Switch_RoundTripsUnchanged(int input, int expected) {
        MethodInfo generated = RoundTripOf(typeof(BranchingTargets), nameof(BranchingTargets.SwitchOnValue));
        Assert.Equal(expected, generated.Invoke(null, [input]));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(3, 6)]
    public void TryCatch_RoundTripsUnchanged(int input, int expected) {
        MethodInfo generated = RoundTripOf(typeof(ExceptionRegionTargets), nameof(ExceptionRegionTargets.TryCatch));
        ExceptionRegionTargets.Trace.Clear();
        Assert.Equal(expected, generated.Invoke(null, [input]));
        Assert.Contains("try", ExceptionRegionTargets.Trace);
    }

    [Fact]
    public void TryFinally_RoundTripsUnchanged() {
        MethodInfo generated = RoundTripOf(typeof(ExceptionRegionTargets), nameof(ExceptionRegionTargets.TryFinally));
        ExceptionRegionTargets.Trace.Clear();
        Assert.Equal(8, generated.Invoke(null, [4]));
        Assert.Equal(["try", "finally"], ExceptionRegionTargets.Trace);
    }

    [Theory]
    [InlineData(0, -2)]
    [InlineData(2, 6)]
    public void NestedRegions_RoundTripUnchanged(int input, int expected) {
        MethodInfo generated = RoundTripOf(typeof(ExceptionRegionTargets), nameof(ExceptionRegionTargets.NestedTryCatchFinally));
        ExceptionRegionTargets.Trace.Clear();
        Assert.Equal(expected, generated.Invoke(null, [input]));
        Assert.Contains("outer-finally", ExceptionRegionTargets.Trace);
    }

    [Theory]
    [InlineData(-5, -1)]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(50, 2)]
    public void EarlyReturns_RoundTripUnchanged(int input, int expected) {
        MethodInfo generated = RoundTripOf(typeof(EarlyReturnTargets), nameof(EarlyReturnTargets.Classify));
        Assert.Equal(expected, generated.Invoke(null, [input]));
    }

    [Fact]
    public void ClassInstance_RoundTripsUnchanged() {
        MethodBase target = typeof(InstanceClassTarget).GetMethod(nameof(InstanceClassTarget.AddToBase))!;
        NeutralBody neutral = RoundTrip.Extract(target);
        MethodInfo generated = RoundTrip.Generate(neutral, target);
        InstanceClassTarget receiver = new InstanceClassTarget(10);
        Assert.Equal(13, generated.Invoke(null, [receiver, 3]));
    }

    [Fact]
    public void StructInstance_RoundTripsUnchanged() {
        MethodBase target = typeof(InstanceStructTarget).GetMethod(nameof(InstanceStructTarget.AddToBase))!;
        NeutralBody neutral = RoundTrip.Extract(target);
        MethodInfo generated = RoundTrip.Generate(neutral, target);
        object[] arguments = [new InstanceStructTarget(10), 3];
        Assert.Equal(13, generated.Invoke(null, arguments));
    }
}
