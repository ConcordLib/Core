using Xunit;

namespace Concord.Host.Adapter.Tests;

public class AccessCheckProbeTests {
    [Fact]
    public void RimWorld17ShipsTheKeyAsABooleanTrue() {
        Assert.True(RuntimeCapability.SkipsAccessChecks(true, null));
    }

    [Fact]
    public void AHostThatSetsTheKeyFalseKeepsTheChecks() {
        Assert.False(RuntimeCapability.SkipsAccessChecks(false, null));
    }

    [Fact]
    public void AStockRuntimeCarriesNeitherSource() {
        Assert.False(RuntimeCapability.SkipsAccessChecks(null, null));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void TheKeyIsHonoredWhenTheHostWroteItAsText(string configured) {
        Assert.True(RuntimeCapability.SkipsAccessChecks(configured, null));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void TheEnvironmentSwitchTurnsTheChecksOff(string environment) {
        Assert.True(RuntimeCapability.SkipsAccessChecks(null, environment));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("no")]
    public void AnUnsetOrDeniedEnvironmentSwitchKeepsTheChecks(string environment) {
        Assert.False(RuntimeCapability.SkipsAccessChecks(null, environment));
    }

    [Fact]
    public void TheRuntimeConfigKeyWinsOverTheEnvironmentSwitch() {
        Assert.False(RuntimeCapability.SkipsAccessChecks(false, "1"));
    }
}
