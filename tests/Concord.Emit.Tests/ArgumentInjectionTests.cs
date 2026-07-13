using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class ArgRules {
    public static int Apply(int basePrice, string label) {
        return basePrice + label.Length;
    }
}

public class ArgHost {
    public int Total(int listed) {
        return ArgRules.Apply(listed + 5, "tag");
    }

    public int Pick(int a, int b) {
        return System.Math.Max(a + 1, b + 2);
    }
}

public class ArgInjectionMethods {
    public int ClampBase(int original) {
        return original > 10 ? 10 : original;
    }

    public string Longer(string original) {
        return original + "gg";
    }
}

public sealed class ArgumentInjectionTests {
    private static ComposeResult ComposeArg(string injectionName, uint arg) {
        MethodBase target = typeof(ArgHost).GetMethod(nameof(ArgHost.Total))!;
        MethodBase injection = typeof(ArgInjectionMethods).GetMethod(injectionName)!;
        Injection rewrite = new Injection(injection, new InjectAt.Invoke(typeof(ArgRules), nameof(ArgRules.Apply), At.Argument, 0, null, arg), "test", 0);
        return WrapperComposer.Compose(target, [rewrite]);
    }

    [Fact]
    public void ExplicitArg_RewritesFirstArgument() {
        ComposeResult result = ComposeArg(nameof(ArgInjectionMethods.ClampBase), 1);
        System.Func<ArgHost, int, int> run = result.Wrapper.CreateDelegate<System.Func<ArgHost, int, int>>();

        Assert.Equal(13, run(new ArgHost(), 20));
        Assert.Equal(10, run(new ArgHost(), 2));
    }

    [Fact]
    public void InferredArg_PicksUniqueTypeMatch() {
        ComposeResult result = ComposeArg(nameof(ArgInjectionMethods.Longer), 0);
        System.Func<ArgHost, int, int> run = result.Wrapper.CreateDelegate<System.Func<ArgHost, int, int>>();

        Assert.Equal(2 + 5 + 5, run(new ArgHost(), 2));
    }

    [Fact]
    public void AmbiguousInference_Throws() {
        MethodBase target = typeof(ArgHost).GetMethod(nameof(ArgHost.Pick))!;
        MethodBase injection = typeof(ArgInjectionMethods).GetMethod(nameof(ArgInjectionMethods.ClampBase))!;
        Injection rewrite = new Injection(injection, new InjectAt.Invoke(typeof(System.Math), nameof(System.Math.Max), At.Argument, 0, [typeof(int), typeof(int)], 0), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [rewrite]));
        Assert.Contains("arg:", ex.Message);
    }
}
