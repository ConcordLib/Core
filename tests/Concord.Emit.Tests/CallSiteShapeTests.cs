using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class ShapeProbe {
    public float Age => 1f;

    public int Combine(int a, string b) {
        return a + b.Length;
    }

    public static void Fire(long id) {
    }
}

public sealed class CallSiteShapeTests {
    [Fact]
    public void Resolve_InstanceGetter_HasReceiverAndNoArgs() {
        MethodBase getter = typeof(ShapeProbe).GetProperty(nameof(ShapeProbe.Age))!.GetGetMethod()!;

        CallSiteShape shape = CallSiteShape.Resolve(getter);

        Assert.True(shape.HasThis);
        Assert.Equal(typeof(ShapeProbe), shape.ReceiverType);
        Assert.Empty(shape.ParameterTypes);
        Assert.Equal(typeof(float), shape.ReturnType);
    }

    [Fact]
    public void Resolve_StaticVoid_HasNoReceiver() {
        MethodBase fire = typeof(ShapeProbe).GetMethod(nameof(ShapeProbe.Fire))!;

        CallSiteShape shape = CallSiteShape.Resolve(fire);

        Assert.False(shape.HasThis);
        Assert.Null(shape.ReceiverType);
        Assert.Equal([typeof(long)], shape.ParameterTypes);
        Assert.Equal(typeof(void), shape.ReturnType);
    }

    [Fact]
    public void ExpectedOperationType_MatchesEveryShape() {
        Assert.Equal(typeof(Operation<float>), CallSiteShape.Resolve(typeof(ShapeProbe).GetProperty(nameof(ShapeProbe.Age))!.GetGetMethod()!).ExpectedOperationType());
        Assert.Equal(typeof(Operation<int, string, int>), CallSiteShape.Resolve(typeof(ShapeProbe).GetMethod(nameof(ShapeProbe.Combine))!).ExpectedOperationType());
        Assert.Equal(typeof(VoidOperation<long>), CallSiteShape.Resolve(typeof(ShapeProbe).GetMethod(nameof(ShapeProbe.Fire))!).ExpectedOperationType());
    }
}

public class ValidateShapeHost {
    public int Run(int x) {
        return ValidateShapeTarget.Compute(x);
    }
}

public static class ValidateShapeTarget {
    public static int Compute(int a) {
        return a + 1;
    }
}

public class WrongOperationInjection {
    public int BadDeclaration(Operation<string> op) {
        return op.Invoke().Length;
    }
}

public class NoOperationInjection {
    public int NoOperationParam(int x) {
        return x * 2;
    }
}

public readonly struct ValueTypeTarget {
    public readonly int Value;

    public ValueTypeTarget(int value) => Value = value;

    public int Double => Value * 2;
}

public class ValueTypeHost {
    public int CallOnStruct() {
        ValueTypeTarget target = new ValueTypeTarget(5);
        return target.Double;
    }
}

public sealed class CallSiteShapeValidationTests {
    [Fact]
    public void ValidateOperationShape_ThrowsConc039_OnMismatchedDeclaration() {
        MethodBase target = typeof(ValidateShapeHost).GetMethod(nameof(ValidateShapeHost.Run))!;
        MethodBase injection = typeof(WrongOperationInjection).GetMethod(nameof(WrongOperationInjection.BadDeclaration))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(ValidateShapeTarget), nameof(ValidateShapeTarget.Compute), At.Around, 0), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [wrap]));
        Assert.Equal("CONC039", ex.Code);
        Assert.Contains("declares", ex.Message);
    }

    [Fact]
    public void ValidateOperationShape_ThrowsConc039_OnNoOperationParameter() {
        MethodBase target = typeof(ValidateShapeHost).GetMethod(nameof(ValidateShapeHost.Run))!;
        MethodBase injection = typeof(NoOperationInjection).GetMethod(nameof(NoOperationInjection.NoOperationParam))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(ValidateShapeTarget), nameof(ValidateShapeTarget.Compute), At.Around, 0), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [wrap]));
        Assert.Equal("CONC039", ex.Code);
        Assert.Contains("no Operation", ex.Message);
    }

    [Fact]
    public void ValidateOperationShape_ThrowsConc039_OnValueTypeReceiver() {
        // Value-type receiver call sites are not naturally reachable through composition
        // (other validations fail first), so the branch is unit-tested directly.
        MethodBase injectionMethod = typeof(NoOperationInjection).GetMethod(nameof(NoOperationInjection.NoOperationParam))!;
        MethodBase target = typeof(ValueTypeHost).GetMethod(nameof(ValueTypeHost.CallOnStruct))!;

        CallSiteShape shape = new CallSiteShape(true, typeof(ValueTypeTarget), [], typeof(int));

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.ValidateOperationShape(injectionMethod, shape, target));
        Assert.Equal("CONC039", ex.Code);
        Assert.Contains("value type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
