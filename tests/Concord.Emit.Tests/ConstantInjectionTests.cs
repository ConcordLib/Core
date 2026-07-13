using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class ConstantHost {
    public bool ShouldEject(float age) {
        return age >= 18f;
    }

    public int SmallInt() {
        return 8;
    }

    public string Greeting() {
        return "hello";
    }

    public static int Pad(int v) {
        return v;
    }

    public int TwoLiterals() {
        return Pad(5) + Pad(5);
    }
}

public class ConstantInjectionMethods {
    public static float LiveEjectAge = 20f;

    public float EjectAge(float original) {
        return LiveEjectAge;
    }

    public float PlusOne(float original) {
        return original + 1f;
    }

    public int NineFromEight(int original) {
        return original + 1;
    }

    public string Louder(string original) {
        return original + "!";
    }

    public int BumpFive(int original) {
        return original + 1;
    }
}

public sealed class ConstantInjectionTests {
    private static ComposeResult ComposeConstant(string targetName, object value, string injectionName, uint by = 0) {
        MethodBase target = typeof(ConstantHost).GetMethod(targetName)!;
        MethodBase injection = typeof(ConstantInjectionMethods).GetMethod(injectionName)!;
        Injection constant = new Injection(injection, new InjectAt.Constant(value, by), "test", 0);
        return WrapperComposer.Compose(target, [constant]);
    }

    [Fact]
    public void FloatConstant_ReplacedAndLive() {
        ComposeResult result = ComposeConstant(nameof(ConstantHost.ShouldEject), 18f, nameof(ConstantInjectionMethods.EjectAge));
        System.Func<ConstantHost, float, bool> run = result.Wrapper.CreateDelegate<System.Func<ConstantHost, float, bool>>();

        ConstantInjectionMethods.LiveEjectAge = 20f;
        Assert.False(run(new ConstantHost(), 19f));
        Assert.True(run(new ConstantHost(), 21f));

        ConstantInjectionMethods.LiveEjectAge = 16f;
        Assert.True(run(new ConstantHost(), 17f));
    }

    [Fact]
    public void MacroIntConstant_Matches() {
        ComposeResult result = ComposeConstant(nameof(ConstantHost.SmallInt), 8, nameof(ConstantInjectionMethods.NineFromEight));
        System.Func<ConstantHost, int> run = result.Wrapper.CreateDelegate<System.Func<ConstantHost, int>>();

        Assert.Equal(9, run(new ConstantHost()));
    }

    [Fact]
    public void StringConstant_Matches() {
        ComposeResult result = ComposeConstant(nameof(ConstantHost.Greeting), "hello", nameof(ConstantInjectionMethods.Louder));
        System.Func<ConstantHost, string> run = result.Wrapper.CreateDelegate<System.Func<ConstantHost, string>>();

        Assert.Equal("hello!", run(new ConstantHost()));
    }

    [Fact]
    public void ByOrdinal_PicksOneOccurrence() {
        ComposeResult result = ComposeConstant(nameof(ConstantHost.TwoLiterals), 5, nameof(ConstantInjectionMethods.BumpFive), by: 2);
        System.Func<ConstantHost, int> run = result.Wrapper.CreateDelegate<System.Func<ConstantHost, int>>();

        Assert.Equal(11, run(new ConstantHost()));
    }

    [Fact]
    public void NoMatch_ThrowsConc037() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => ComposeConstant(nameof(ConstantHost.Greeting), 42f, nameof(ConstantInjectionMethods.EjectAge)));
        Assert.Equal("CONC037", ex.Code);
    }

    [Fact]
    public void ByBeyondMatches_ThrowsConc038() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => ComposeConstant(nameof(ConstantHost.SmallInt), 8, nameof(ConstantInjectionMethods.NineFromEight), by: 3));
        Assert.Equal("CONC038", ex.Code);
    }

    [Fact]
    public void ChainedConstantInjections_ApplyInDeterministicOrder() {
        MethodBase target = typeof(ConstantHost).GetMethod(nameof(ConstantHost.ShouldEject))!;
        MethodBase eject = typeof(ConstantInjectionMethods).GetMethod(nameof(ConstantInjectionMethods.EjectAge))!;
        MethodBase plusOne = typeof(ConstantInjectionMethods).GetMethod(nameof(ConstantInjectionMethods.PlusOne))!;
        Injection first = new Injection(eject, new InjectAt.Constant(18f, 0), "test", 0);
        Injection second = new Injection(plusOne, new InjectAt.Constant(18f, 0), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [first, second]);
        System.Func<ConstantHost, float, bool> run = result.Wrapper.CreateDelegate<System.Func<ConstantHost, float, bool>>();

        // Assemble processes injections in reverse order, so 'first' splices closest to the literal
        // and 'second' splices around its output: effective comparand is EjectAge() then +1.
        ConstantInjectionMethods.LiveEjectAge = 20f;
        Assert.True(run(new ConstantHost(), 21f));
        Assert.False(run(new ConstantHost(), 20f));
    }
}
