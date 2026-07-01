using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class OriginalBodyTests {
    [Fact]
    public void Clone_StaticMethod_InvokesIdentically() {
        MethodBase original = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;

        MethodInfo clone = OriginalBody.Clone(original);
        object? result = clone.Invoke(null, [2, 3]);

        Assert.Equal(5, result);
    }

    [Fact]
    public void Clone_InstanceMethod_InvokesAgainstReceiver() {
        MethodBase original = typeof(SeedTarget).GetMethod("Seed", BindingFlags.NonPublic | BindingFlags.Instance)!;
        SeedTarget receiver = new SeedTarget();

        MethodInfo clone = OriginalBody.Clone(original);
        object? result = clone.Invoke(null, [receiver]);

        Assert.Equal(7, result);
    }
}
