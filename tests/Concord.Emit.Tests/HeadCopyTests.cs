using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class HeadCopyTests {
    [Fact]
    public void Compose_HeadInjection_RunsBeforeSpine() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Tracked))!;
        MethodBase injectionMethod = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Bump))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        EmitTargets.Counter = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        object? value = result.Wrapper.Invoke(null, [2, 3]);

        Assert.Equal(5, value);
        Assert.Equal(1, EmitTargets.Counter);
    }
}
