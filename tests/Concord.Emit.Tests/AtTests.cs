using Xunit;

namespace Concord.Emit.Tests;

public sealed class AtTests {
    [Fact]
    public void InjectAttribute_InvokeCtor_ResolvedAt_ReturnsInjectAtInvoke() {
        InjectAttribute attr = new InjectAttribute("M", typeof(string), "Foo", At.Around, 0);
        Assert.Equal(At.Around, attr.At);
        Assert.Equal(new InjectAt.Invoke(typeof(string), "Foo", At.Around, 0), attr.ResolvedAt);
    }

    [Fact]
    public void InjectAttribute_Head_ResolvedAt_ReturnsHead() {
        InjectAttribute attr = new InjectAttribute(At.Head, "M");
        Assert.Equal(At.Head, attr.At);
        Assert.Equal(new InjectAt.Head(), attr.ResolvedAt);
    }

    [Fact]
    public void InjectAttribute_Tail_ResolvedAt_ReturnsTail() {
        InjectAttribute attr = new InjectAttribute(At.Tail, "M");
        Assert.Equal(At.Tail, attr.At);
        Assert.Equal(new InjectAt.Tail(), attr.ResolvedAt);
    }

    [Fact]
    public void InjectAttribute_Around_ResolvedAt_ReturnsAround() {
        InjectAttribute attr = new InjectAttribute(At.Around, "M");
        Assert.Equal(At.Around, attr.At);
        Assert.Equal(new InjectAt.Around(), attr.ResolvedAt);
    }

    [Fact]
    public void InjectAttribute_Return_ResolvedAt_ReturnsReturn() {
        InjectAttribute attr = new InjectAttribute(At.Return, "M");
        Assert.Equal(At.Return, attr.At);
        Assert.Equal(new InjectAt.Return(0), attr.ResolvedAt);
    }

    [Fact]
    public void InjectAttribute_ReturnByN_ResolvedAt_ReturnsReturnWithBy() {
        InjectAttribute attr = new InjectAttribute(At.Return, "M", 2);
        Assert.Equal(At.Return, attr.At);
        Assert.Equal(new InjectAt.Return(2), attr.ResolvedAt);
    }

    [Fact]
    public void InjectAttribute_Transpiler_ResolvedAt_ReturnsTranspiler() {
        InjectAttribute attr = new InjectAttribute(At.Transpiler, "M");
        Assert.Equal(At.Transpiler, attr.At);
        Assert.Equal(new InjectAt.Transpiler(false), attr.ResolvedAt);
    }

    [Fact]
    public void InjectAttribute_TranspilerFinal_ResolvedAt_ReturnsTranspilerFinal() {
        InjectAttribute attr = new InjectAttribute(At.TranspilerFinal, "M");
        Assert.Equal(At.TranspilerFinal, attr.At);
        Assert.Equal(new InjectAt.Transpiler(true), attr.ResolvedAt);
    }

    [Fact]
    public void InjectAttribute_UnmappedAt_Throws() {
        InjectAttribute attr = new InjectAttribute((At)999, "M");
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => attr.ResolvedAt);
        Assert.Equal("CONC116", ex.Code);
    }
}
