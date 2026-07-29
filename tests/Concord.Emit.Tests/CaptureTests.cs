using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class CaptureRules {
    public static volatile float Rate = 2f;

    public static int Bonus = 3;

    public static int Apply(int basePrice, float rate) {
        return (int)(basePrice * rate);
    }

    public static void Mutate(ref int amount) {
        amount = 99;
    }
}

public sealed class CaptureQuote {
    public CaptureQuote(int listed, float rate) {
        Total = (int)(listed * rate);
    }

    public int Total { get; }
}

public class CaptureHost {
    public int Priced(int listed) {
        int adjusted = listed + 5;
        return CaptureRules.Apply(adjusted, 2f);
    }

    public int Computed(int listed) {
        return CaptureRules.Apply(listed * 3, 2f);
    }

    public int Mutated(int listed) {
        int amount = listed;
        CaptureRules.Mutate(ref amount);
        return amount;
    }

    public int Prefixed(int listed) {
        return CaptureRules.Apply(listed, CaptureRules.Rate);
    }

    public int Branched(int listed, bool doubled) {
        return CaptureRules.Apply(listed, doubled ? 2f : 1f);
    }

    public int Constructed(int listed) {
        return new CaptureQuote(listed + 5, 2f).Total;
    }

    public int Fielded(int listed) {
        return CaptureRules.Bonus + listed;
    }
}

public static class CaptureRecorder {
    public static int SeenBase;
    public static float SeenRate;
    public static int SeenAmount;
}

public class CaptureMethods {
    public void AfterApply([Capture(1)] int basePrice, [Capture(2)] float rate) {
        CaptureRecorder.SeenBase = basePrice;
        CaptureRecorder.SeenRate = rate;
    }

    public void AfterMutate([Capture(1)] int amount) {
        CaptureRecorder.SeenAmount = amount;
    }
}

public class BadCaptureMethods {
    public void TooFar([Capture(9)] int value) {
        CaptureRecorder.SeenBase = value;
    }

    public int WrongShift([Capture(1)] int value, Operation<int, float, int> original) {
        return original.Invoke(value, 1f);
    }

    public void WrongPosition([Capture(1)] int value) {
        CaptureRecorder.SeenBase = value;
    }
}

public sealed class CaptureTests {
    [Fact]
    public void Tail_CapturesEveryArgument() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Priced))!;
        MethodBase injection = typeof(CaptureMethods).GetMethod(nameof(CaptureMethods.AfterApply))!;
        Injection capture = new Injection(injection, new InjectAt.Invoke(typeof(CaptureRules), nameof(CaptureRules.Apply), At.Tail), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [capture]);
        Func<CaptureHost, int, int> run = result.Wrapper.CreateDelegate<Func<CaptureHost, int, int>>();

        CaptureRecorder.SeenBase = 0;
        CaptureRecorder.SeenRate = 0f;
        run(new CaptureHost(), 5);

        Assert.Equal(10, CaptureRecorder.SeenBase);
        Assert.Equal(2f, CaptureRecorder.SeenRate);
    }

    [Fact]
    public void Capture_WorksOnAComputedArgument() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Computed))!;
        MethodBase injection = typeof(CaptureMethods).GetMethod(nameof(CaptureMethods.AfterApply))!;
        Injection capture = new Injection(injection, new InjectAt.Invoke(typeof(CaptureRules), nameof(CaptureRules.Apply), At.Tail), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [capture]);
        Func<CaptureHost, int, int> run = result.Wrapper.CreateDelegate<Func<CaptureHost, int, int>>();

        CaptureRecorder.SeenBase = 0;
        run(new CaptureHost(), 5);

        Assert.Equal(15, CaptureRecorder.SeenBase);
    }

    [Fact]
    public void Tail_CapturesThePassedValue_NotThePostCallValue() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Mutated))!;
        MethodBase injection = typeof(CaptureMethods).GetMethod(nameof(CaptureMethods.AfterMutate))!;
        Injection capture = new Injection(injection, new InjectAt.Invoke(typeof(CaptureRules), nameof(CaptureRules.Mutate), At.Tail), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [capture]);
        Func<CaptureHost, int, int> run = result.Wrapper.CreateDelegate<Func<CaptureHost, int, int>>();

        CaptureRecorder.SeenAmount = 0;
        int returned = run(new CaptureHost(), 5);

        Assert.Equal(99, returned);
        Assert.Equal(5, CaptureRecorder.SeenAmount);
    }

    [Fact]
    public void Capture_DoesNotSplitAPrefixFromItsInstruction() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Prefixed))!;
        MethodBase injection = typeof(CaptureMethods).GetMethod(nameof(CaptureMethods.AfterApply))!;
        Injection capture = new Injection(injection, new InjectAt.Invoke(typeof(CaptureRules), nameof(CaptureRules.Apply), At.Tail), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [capture]);
        Func<CaptureHost, int, int> run = result.Wrapper.CreateDelegate<Func<CaptureHost, int, int>>();

        CaptureRecorder.SeenBase = 0;
        CaptureRecorder.SeenRate = 0f;
        run(new CaptureHost(), 5);

        Assert.Equal(5, CaptureRecorder.SeenBase);
        Assert.Equal(2f, CaptureRecorder.SeenRate);
    }

    [Fact]
    public void Head_CapturesBeforeTheCall() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Priced))!;
        MethodBase injection = typeof(CaptureMethods).GetMethod(nameof(CaptureMethods.AfterApply))!;
        Injection capture = new Injection(injection, new InjectAt.Invoke(typeof(CaptureRules), nameof(CaptureRules.Apply), At.Head), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [capture]);
        Func<CaptureHost, int, int> run = result.Wrapper.CreateDelegate<Func<CaptureHost, int, int>>();

        CaptureRecorder.SeenBase = 0;
        run(new CaptureHost(), 5);

        Assert.Equal(10, CaptureRecorder.SeenBase);
    }

    [Fact]
    public void CaptureArgPastEnd_ThrowsConc130() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Priced))!;
        MethodBase injection = typeof(BadCaptureMethods).GetMethod(nameof(BadCaptureMethods.TooFar))!;
        Injection capture = new Injection(injection, new InjectAt.Invoke(typeof(CaptureRules), nameof(CaptureRules.Apply), At.Tail), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [capture]));
        Assert.Contains("CONC130", ex.ToString());
    }

    [Fact]
    public void CaptureOnAroundShift_ThrowsConc128() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Priced))!;
        MethodBase injection = typeof(BadCaptureMethods).GetMethod(nameof(BadCaptureMethods.WrongShift))!;
        Injection capture = new Injection(injection, new InjectAt.Invoke(typeof(CaptureRules), nameof(CaptureRules.Apply), At.Around), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [capture]));
        Assert.Contains("CONC128", ex.ToString());
    }

    [Fact]
    public void CaptureOnWholeMethodHead_ThrowsConc128() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Priced))!;
        MethodBase injection = typeof(BadCaptureMethods).GetMethod(nameof(BadCaptureMethods.WrongPosition))!;
        Injection capture = new Injection(injection, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [capture]));
        Assert.Contains("CONC128", ex.ToString());
    }

    [Fact]
    public void CaptureAcrossABranchJoin_ThrowsConc129() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Branched))!;
        MethodBase injection = typeof(CaptureMethods).GetMethod(nameof(CaptureMethods.AfterApply))!;
        Injection capture = new Injection(injection, new InjectAt.Invoke(typeof(CaptureRules), nameof(CaptureRules.Apply), At.Tail), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [capture]));
        Assert.Contains("CONC129", ex.ToString());
    }

    [Fact]
    public void NewObjSite_CapturesConstructorArguments() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Constructed))!;
        MethodBase injection = typeof(CaptureMethods).GetMethod(nameof(CaptureMethods.AfterApply))!;
        Injection capture = new Injection(injection, new InjectAt.NewObj(typeof(CaptureQuote), At.Tail), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [capture]);
        Func<CaptureHost, int, int> run = result.Wrapper.CreateDelegate<Func<CaptureHost, int, int>>();

        CaptureRecorder.SeenBase = 0;
        CaptureRecorder.SeenRate = 0f;
        int returned = run(new CaptureHost(), 5);

        Assert.Equal(20, returned);
        Assert.Equal(10, CaptureRecorder.SeenBase);
        Assert.Equal(2f, CaptureRecorder.SeenRate);
    }

    [Fact]
    public void CaptureOnAFieldRead_ThrowsConc130() {
        MethodBase target = typeof(CaptureHost).GetMethod(nameof(CaptureHost.Fielded))!;
        MethodBase injection = typeof(CaptureMethods).GetMethod(nameof(CaptureMethods.AfterMutate))!;
        Injection capture = new Injection(injection, new InjectAt.Invoke(typeof(CaptureRules), nameof(CaptureRules.Bonus), At.Tail), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [capture]));
        Assert.Contains("CONC130", ex.ToString());
    }
}
