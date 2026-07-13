using Xunit;

namespace Concord.Emit.Tests;

public sealed class InjectAttributeResolutionTests {
    [Fact]
    public void ConstantCtor_ResolvesToInjectAtConstant() {
        InjectAttribute attribute = new InjectAttribute("Tick", 18f, At.Constant, by: 2);

        InjectAt resolved = attribute.ResolvedAt;

        InjectAt.Constant constant = Assert.IsType<InjectAt.Constant>(resolved);
        Assert.Equal(18f, constant.Value);
        Assert.Equal(2u, constant.By);
        Assert.Equal("Tick", attribute.Method);
    }

    [Fact]
    public void ConstantCtor_WithoutMethod_TargetsConstructor() {
        InjectAttribute attribute = new InjectAttribute(18f, At.Constant);

        Assert.True(attribute.TargetsConstructor);
        Assert.IsType<InjectAt.Constant>(attribute.ResolvedAt);
    }

    [Fact]
    public void InvokeCtor_CarriesArgSelector() {
        InjectAttribute attribute = new InjectAttribute("Total", typeof(string), "Concat", At.Argument, by: 1, arg: 2);

        InjectAt.Invoke invoke = Assert.IsType<InjectAt.Invoke>(attribute.ResolvedAt);
        Assert.Equal(At.Argument, invoke.Shift);
        Assert.Equal(1u, invoke.By);
        Assert.Equal(2u, invoke.Arg);
    }
}
