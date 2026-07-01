using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class SealedProjectionTarget {
    public int PublicBonus = 4;
    private int _fuel = 10;
    private int Mood { get; set; } = 2;

    public int FuelSnapshot => _fuel;
    public int MoodSnapshot => Mood;

    public int Tick(int delta) {
        _fuel += delta;
        return _fuel;
    }

    private int Recalculate(int add) {
        _fuel += add;
        return _fuel + Mood;
    }
}

#pragma warning disable CS0414, CS0649
public abstract class SealedProjectionInjectionMethod {
    [InjectInstance]
    protected abstract SealedProjectionTarget Self { get; }

    [InjectField("_fuel")]
    private int fuel;

    [InjectProperty("Mood")]
    protected abstract int HiddenMood { get; set; }

    [InjectMethod("Recalculate")]
    protected abstract int Recalculate(int add);

    public void Head(ControlHandle<int> ch, int delta) {
        fuel += delta;
        HiddenMood = Self.PublicBonus + HiddenMood;
        ch.ReturnValue = Recalculate(5);
        ch.Cancel();
    }
}

public abstract class MissingFieldProjectionInjectionMethod {
    [InjectField("missing")]
    private int fuel;

    public void Head(ControlHandle<int> ch) {
        fuel = 1;
    }
}

public abstract class MismatchedFieldProjectionInjectionMethod {
    [InjectField("_fuel")]
    private long fuel;

    public void Head(ControlHandle<int> ch) {
        fuel = 1;
    }
}
#pragma warning restore CS0414, CS0649

public sealed class StaticProjectionTarget {
    public static int Run() {
        return 1;
    }
}

public abstract class StaticTargetInstanceInjectionMethod {
    [InjectInstance]
    protected abstract StaticProjectionTarget Self { get; }

    public void Head(ControlHandle<int> ch) {
        ch.ReturnValue = 2;
        ch.Cancel();
    }
}

public sealed class InjectedMemberTests {
    [Fact]
    public void Compose_AbstractExplicitTarget_LowersInjectedInstanceAndMembers() {
        MethodBase target = typeof(SealedProjectionTarget).GetMethod(nameof(SealedProjectionTarget.Tick))!;
        MethodBase injectionMethod = typeof(SealedProjectionInjectionMethod).GetMethod(nameof(SealedProjectionInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);
        Func<SealedProjectionTarget, int, int> invoke = result.Wrapper.CreateDelegate<Func<SealedProjectionTarget, int, int>>();

        SealedProjectionTarget instance = new SealedProjectionTarget();
        int value = invoke(instance, 3);

        Assert.Equal(24, value);
        Assert.Equal(18, instance.FuelSnapshot);
        Assert.Equal(6, instance.MoodSnapshot);
    }

    [Fact]
    public void Compose_InjectedMembers_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(SealedProjectionTarget).GetMethod(nameof(SealedProjectionTarget.Tick))!;
        MethodBase injectionMethod = typeof(SealedProjectionInjectionMethod).GetMethod(nameof(SealedProjectionInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);
        Func<SealedProjectionTarget, int, int> invoke = result.Wrapper.CreateDelegate<Func<SealedProjectionTarget, int, int>>();

        SealedProjectionTarget instance = new SealedProjectionTarget();
        invoke(instance, 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        invoke(instance, 1);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Compose_InjectedFieldMissing_ThrowsCONC071() {
        MethodBase target = typeof(SealedProjectionTarget).GetMethod(nameof(SealedProjectionTarget.Tick))!;
        MethodBase injectionMethod = typeof(MissingFieldProjectionInjectionMethod).GetMethod(nameof(MissingFieldProjectionInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC071", ex.Code);
    }

    [Fact]
    public void Compose_InjectedFieldMismatch_ThrowsCONC072() {
        MethodBase target = typeof(SealedProjectionTarget).GetMethod(nameof(SealedProjectionTarget.Tick))!;
        MethodBase injectionMethod = typeof(MismatchedFieldProjectionInjectionMethod).GetMethod(nameof(MismatchedFieldProjectionInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC072", ex.Code);
    }

    [Fact]
    public void Compose_InjectedInstanceOnStaticTarget_ThrowsCONC074() {
        MethodBase target = typeof(StaticProjectionTarget).GetMethod(nameof(StaticProjectionTarget.Run))!;
        MethodBase injectionMethod = typeof(StaticTargetInstanceInjectionMethod).GetMethod(nameof(StaticTargetInstanceInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC074", ex.Code);
    }
}
