using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class InjectAttributeTests {
    [Inject(At.Head, nameof(ToString))]
    private void DefaultHead() { }

    [Inject(At.Tail, nameof(ToString))]
    private void ExplicitReturn() { }

    [Fact]
    public void Ctor_SingleArg_DefaultsToHead() {
        InjectAttribute attr = GetAttr(nameof(DefaultHead));
        Assert.Equal("ToString", attr.Method);
        Assert.Equal(At.Head, attr.At);
        Assert.IsType<InjectAt.Head>(attr.ResolvedAt);
    }

    [Fact]
    public void Ctor_WithAt_SetsAtAndResolvedAt() {
        InjectAttribute attr = GetAttr(nameof(ExplicitReturn));
        Assert.Equal(At.Tail, attr.At);
        Assert.IsType<InjectAt.Tail>(attr.ResolvedAt);
    }

    private InjectAttribute GetAttr(string method) {
        return typeof(InjectAttributeTests)
                   .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                   .GetCustomAttributes(typeof(InjectAttribute), false)[0] as InjectAttribute ??
               throw new InvalidOperationException();
    }
}
