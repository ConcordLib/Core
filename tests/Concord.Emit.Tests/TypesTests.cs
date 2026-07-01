using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class TypesTests {
    [Fact]
    public void Injection_HasRecordValueEquality() {
        MethodBase injectionMethod = typeof(TypesTests).GetMethod(nameof(Sample), BindingFlags.NonPublic | BindingFlags.Static)!;
        Injection left = new Injection(injectionMethod, new InjectAt.Head(), "owner", 0);
        Injection right = new Injection(injectionMethod, new InjectAt.Head(), "owner", 0);

        Assert.Equal(left, right);
    }

    [Fact]
    public void InjectAt_SubtypesPatternMatch() {
        InjectAt head = new InjectAt.Head();
        InjectAt @return = new InjectAt.Tail();
        InjectAt invoke = new InjectAt.Invoke(typeof(string), "Concat", At.Head, 0);

        Assert.True(head is InjectAt.Head);
        Assert.True(@return is InjectAt.Tail);

        InjectAt.Invoke matched = Assert.IsType<InjectAt.Invoke>(invoke);
        Assert.Equal(typeof(string), matched.DeclaringType);
        Assert.Equal("Concat", matched.Method);
    }

    private static void Sample() { }
}
