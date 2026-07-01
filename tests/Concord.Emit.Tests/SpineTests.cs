using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class SpineTests {
    [Fact]
    public void Compose_NoInjections_StaticMethodRoundTrips() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;

        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.Equal(5, result.Wrapper.Invoke(null, [2, 3]));
        Assert.NotNull(result.OriginalBody);
        Assert.Equal(5, result.OriginalBody.Invoke(null, [2, 3]));
    }

    [Fact]
    public void Compose_NoInjections_RefTypeReturnRoundTrips() {
        MethodBase target = typeof(SpineRefTarget).GetMethod(nameof(SpineRefTarget.Echo))!;

        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.Equal("hello", result.Wrapper.Invoke(null, ["hello"]));
    }

    [Fact]
    public void Compose_NoInjections_InstanceMethodRoundTrips() {
        MethodBase target = typeof(SeedTarget).GetMethod("Seed", BindingFlags.NonPublic | BindingFlags.Instance)!;
        SeedTarget receiver = new SeedTarget();

        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.Equal(7, result.Wrapper.Invoke(null, [receiver]));
    }
}
