using System.Reflection;
using Concord.Emit.Tests.ForeignTargets;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class CrossModuleInjectionMethodLocalsTests {
    [Fact]
    public void Compose_ForeignInjectionMethodWithBclLocals_ImportsLocalTypesWithoutThrowing() {
        MethodBase target = typeof(ForeignLocalTarget).GetMethod(nameof(ForeignLocalTarget.Work))!;
        MethodInfo injectionMethod = typeof(ForeignLocalsInjectionMethods).GetMethod(nameof(ForeignLocalsInjectionMethods.HeadWithForeignBclLocals))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ForeignLocalTarget.Runs = 0;
        ForeignLocalsInjectionMethods.Marker = string.Empty;

        ComposeResult result = WrapperComposer.Compose(target, [head]);
        ForeignLocalTarget instance = new ForeignLocalTarget();
        result.Wrapper.Invoke(null, [instance]);

        Assert.Equal("ran", ForeignLocalsInjectionMethods.Marker);
        Assert.Equal(0, ForeignLocalTarget.Runs);
    }
}
