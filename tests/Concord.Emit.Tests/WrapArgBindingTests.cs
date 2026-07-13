using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class BindingRules {
    public static int Apply(int basePrice) {
        return basePrice + 10;
    }
}

public class BindingHost {
    public int Total(int listed) {
        return BindingRules.Apply(listed + 5) + 1;
    }
}

public class BindingInjectionMethods {
    public int DoubleTheRealArg(int basePrice, Operation<int, int> op) {
        return op.Invoke(basePrice * 2);
    }
}

public sealed class WrapArgBindingTests {
    [Fact]
    public void LeadingParameter_ReadsCallSiteArgument_NotTargetParameter() {
        MethodBase target = typeof(BindingHost).GetMethod(nameof(BindingHost.Total))!;
        MethodBase injection = typeof(BindingInjectionMethods).GetMethod(nameof(BindingInjectionMethods.DoubleTheRealArg))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(BindingRules), nameof(BindingRules.Apply), At.Around, 0), "test", 0);
        ComposeResult result = WrapperComposer.Compose(target, [wrap]);
        System.Func<BindingHost, int, int> run = result.Wrapper.CreateDelegate<System.Func<BindingHost, int, int>>();

        Assert.Equal(41, run(new BindingHost(), 10));
    }
}
