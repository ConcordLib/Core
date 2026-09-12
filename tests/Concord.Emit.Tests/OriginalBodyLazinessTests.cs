using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class OriginalBodyLazinessTests {
    [Fact]
    public void Compose_DoesNotCloneTheOriginalBodyUntilItIsRead() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;

        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.False(result.OriginalBodyMaterialized);

        Assert.Equal(5, result.OriginalBody.Invoke(null, [2, 3]));
        Assert.True(result.OriginalBodyMaterialized);
    }

    [Fact]
    public void ComposeResult_ClonesTheOriginalBodyOnce() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.Same(result.OriginalBody, result.OriginalBody);
    }
}
