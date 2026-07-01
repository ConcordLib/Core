using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class NamingTests {
    [Fact]
    public void WrapperName_ContainsGuillemets() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.Contains("‹concord›", result.Wrapper.Name);
        Assert.Contains(target.Name, result.Wrapper.Name);
    }
}
