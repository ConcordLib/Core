using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class LocalHost {
    // A Debug build gives every `return <expr>;` a temp of the return type, so the local under test
    // is a long: that keeps exactly one candidate of its type in the body.
    public int OneLongLocal(int seed) {
        long doubled = seed * 2L;
        return (int)doubled + 1;
    }
}

public static class LocalRecorder {
    public static int SeenInt;

    public static long SeenLong;
}

public class LocalMethods {
    public void ReadDoubled([Local] int doubled) {
        LocalRecorder.SeenInt = doubled;
    }

    public void ReadDoubledLong([Local] long doubled) {
        LocalRecorder.SeenLong = doubled;
    }
}

public class TwoIntHost {
    public int TwoIntLocals(int seed) {
        int first = seed * 2;
        int second = seed * 3;
        return first + second;
    }
}

public class OrdinalMethods {
    public void ReadSecond([Local(Ordinal = 2)] int value) {
        LocalRecorder.SeenInt = value;
    }
}

public sealed class LocalCaptureTests {
    [Fact]
    public void ImplicitTypeMatch_BindsTheOnlyLocalOfThatType() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.OneLongLocal))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<LocalHost, int, int> run = result.Wrapper.CreateDelegate<Func<LocalHost, int, int>>();

        LocalRecorder.SeenLong = 0;
        int returned = run(new LocalHost(), 5);

        Assert.Equal(11, returned);
        Assert.Equal(10L, LocalRecorder.SeenLong);
    }

    [Fact]
    public void ImplicitMatch_RejectsAmbiguity_AndNamesEveryCandidate() {
        MethodBase target = typeof(TwoIntHost).GetMethod(nameof(TwoIntHost.TwoIntLocals))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubled))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC147", error.Code);
        Assert.Contains("slot 0", error.Message);
        Assert.Contains("slot 1", error.Message);
        Assert.Contains("target body", error.Message);
    }

    [Fact]
    public void Ordinal_PicksTheNthLocalOfThatType() {
        MethodBase target = typeof(TwoIntHost).GetMethod(nameof(TwoIntHost.TwoIntLocals))!;
        MethodBase injection = typeof(OrdinalMethods).GetMethod(nameof(OrdinalMethods.ReadSecond))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<TwoIntHost, int, int> run = result.Wrapper.CreateDelegate<Func<TwoIntHost, int, int>>();

        LocalRecorder.SeenInt = 0;
        run(new TwoIntHost(), 5);

        Assert.Equal(15, LocalRecorder.SeenInt);
    }
}
