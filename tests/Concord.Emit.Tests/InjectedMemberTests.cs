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

// Mirrors Pawn_FlightTracker: a private nested enum field whose type cannot be named from a
// patch class, so an [InjectField] has no way to declare it exactly.
public sealed class UnnameableFieldTarget {
    private FlightState flightState = FlightState.Grounded;
    private string label = "grounded";

    private enum FlightState {
        Grounded = 0,
        Flying = 1,
    }

    public int StateSnapshot => (int)flightState;
    public string LabelSnapshot => label;

    public int Tick(int delta) {
        return delta;
    }
}

#pragma warning disable CS0414, CS0649
public abstract class ObjectTypedFieldInjectionMethod {
    [InjectField("flightState")]
    private object flightState = null!;

    public void Head(ControlHandle<int> ch, int delta) {
        // Reads the boxed enum back out and writes a new one in, exercising both directions.
        if (flightState is not null) {
            flightState = System.Enum.ToObject(flightState.GetType(), 1);
        }
    }
}

public abstract class ObjectTypedReferenceFieldInjectionMethod {
    [InjectField("label")]
    private object label = null!;

    public void Head(ControlHandle<int> ch, int delta) {
        label = "flying";
    }
}

public abstract class ObjectTypedFieldAddressInjectionMethod {
    [InjectField("flightState")]
    private object flightState = null!;

    public void Head(ControlHandle<int> ch, int delta) {
        Consume(ref flightState);
    }

    private static void Consume(ref object value) {
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

        long before = TestPolyfills.GetAllocatedBytes();
        invoke(instance, 1);
        long after = TestPolyfills.GetAllocatedBytes();

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

    // The escape hatch for a target whose type cannot be named: declare object, and Concord boxes
    // on read / unboxes on write against the real field.
    [Fact]
    public void Compose_ObjectTypedFieldOverPrivateNestedEnum_ReadsAndWritesTheRealField() {
        MethodBase target = typeof(UnnameableFieldTarget).GetMethod(nameof(UnnameableFieldTarget.Tick))!;
        MethodBase injectionMethod = typeof(ObjectTypedFieldInjectionMethod).GetMethod(nameof(ObjectTypedFieldInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);

        UnnameableFieldTarget instance = new UnnameableFieldTarget();
        Assert.Equal(0, instance.StateSnapshot);

        result.Wrapper.Invoke(null, [instance, 3]);

        Assert.Equal(1, instance.StateSnapshot);
    }

    [Fact]
    public void Compose_ObjectTypedFieldOverReferenceType_WritesTheRealField() {
        MethodBase target = typeof(UnnameableFieldTarget).GetMethod(nameof(UnnameableFieldTarget.Tick))!;
        MethodBase injectionMethod =
            typeof(ObjectTypedReferenceFieldInjectionMethod).GetMethod(nameof(ObjectTypedReferenceFieldInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);

        UnnameableFieldTarget instance = new UnnameableFieldTarget();
        result.Wrapper.Invoke(null, [instance, 3]);

        Assert.Equal("flying", instance.LabelSnapshot);
    }

    // A boxed value has no stable address, so ldflda against it cannot mean anything useful.
    [Fact]
    public void Compose_ObjectTypedFieldAddress_ThrowsCONC126() {
        MethodBase target = typeof(UnnameableFieldTarget).GetMethod(nameof(UnnameableFieldTarget.Tick))!;
        MethodBase injectionMethod =
            typeof(ObjectTypedFieldAddressInjectionMethod).GetMethod(nameof(ObjectTypedFieldAddressInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC126", ex.Code);
    }
}
