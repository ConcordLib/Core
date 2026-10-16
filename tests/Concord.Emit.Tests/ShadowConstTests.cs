using System;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

#pragma warning disable SA1306
public class ConstCollisionTarget {
    private int Limit = 5;

    public int Probe() {
        return Limit;
    }
}

#pragma warning disable CS0414
public class ConstCollisionDeclaration {
    private const float Limit = 5f;

    public void Head(ControlHandle ch) {
        GC.KeepAlive(Limit);
    }
}
#pragma warning restore CS0414, SA1306

public sealed class ShadowConstTests {
    [Fact]
    public void Compose_DeclarationConstNamedLikeATargetFieldOfAnotherType_DoesNotThrowCONC002() {
        MethodBase target = typeof(ConstCollisionTarget).GetMethod(nameof(ConstCollisionTarget.Probe))!;
        MethodBase injectionMethod = typeof(ConstCollisionDeclaration).GetMethod(nameof(ConstCollisionDeclaration.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);

        Assert.Empty(result.Rejected);
        Assert.Equal(5, (int)result.Wrapper.Invoke(null, [new ConstCollisionTarget()])!);
    }
}
