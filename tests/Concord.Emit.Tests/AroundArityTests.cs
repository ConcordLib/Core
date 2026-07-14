using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class ArityProbe {
    public int Method5(int a, int b, int c, int d, int e) {
        return a + b + c + d + e;
    }

    public int Method8(int a, int b, int c, int d, int e, int f, int g, int h) {
        return a + b + c + d + e + f + g + h;
    }

    public int Method9(int a, int b, int c, int d, int e, int f, int g, int h, int i) {
        return a + b + c + d + e + f + g + h + i;
    }

    public void VoidMethod8(int a, int b, int c, int d, int e, int f, int g, int h) {
    }

    public void VoidMethod9(int a, int b, int c, int d, int e, int f, int g, int h, int i) {
    }
}

public sealed class AroundArityTests {
    [Fact]
    public void ExpectedOperationType_5ValueArgs_ResolvesTo6ArgOperation() {
        MethodBase method = typeof(ArityProbe).GetMethod(nameof(ArityProbe.Method5))!;

        CallSiteShape shape = CallSiteShape.Resolve(method);

        Assert.Equal(typeof(Operation<int, int, int, int, int, int>), shape.ExpectedOperationType());
    }

    [Fact]
    public void ExpectedOperationType_8ValueArgs_ResolvesTo9ArgOperation() {
        MethodBase method = typeof(ArityProbe).GetMethod(nameof(ArityProbe.Method8))!;

        CallSiteShape shape = CallSiteShape.Resolve(method);

        Assert.Equal(typeof(Operation<int, int, int, int, int, int, int, int, int>), shape.ExpectedOperationType());
    }

    [Fact]
    public void ExpectedOperationType_9ValueArgs_ThrowsConc039() {
        MethodBase method = typeof(ArityProbe).GetMethod(nameof(ArityProbe.Method9))!;

        CallSiteShape shape = CallSiteShape.Resolve(method);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => shape.ExpectedOperationType());
        Assert.Equal("CONC039", ex.Code);
        Assert.Contains("exceed", ex.Message);
    }

    [Fact]
    public void ExpectedOperationType_8VoidArgs_ResolvesToVoidOperation8() {
        MethodBase method = typeof(ArityProbe).GetMethod(nameof(ArityProbe.VoidMethod8))!;

        CallSiteShape shape = CallSiteShape.Resolve(method);

        Assert.Equal(typeof(VoidOperation<int, int, int, int, int, int, int, int>), shape.ExpectedOperationType());
    }

    [Fact]
    public void ExpectedOperationType_9VoidArgs_ThrowsConc039() {
        MethodBase method = typeof(ArityProbe).GetMethod(nameof(ArityProbe.VoidMethod9))!;

        CallSiteShape shape = CallSiteShape.Resolve(method);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => shape.ExpectedOperationType());
        Assert.Equal("CONC039", ex.Code);
        Assert.Contains("exceed", ex.Message);
    }

    [Fact]
    public void IsOperationType_8ValueArgs_ReturnsTrue() {
        Type operationType = typeof(Operation<int, int, int, int, int, int, int, int, int>);

        bool result = ControlHandleLowering.IsOperationType(operationType);

        Assert.True(result);
    }

    [Fact]
    public void IsOperationType_8VoidArgs_ReturnsTrue() {
        Type operationType = typeof(VoidOperation<int, int, int, int, int, int, int, int>);

        bool result = ControlHandleLowering.IsOperationType(operationType);

        Assert.True(result);
    }
}
