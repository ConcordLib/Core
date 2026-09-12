using System;
using System.Linq.Expressions;
using Xunit;

namespace Concord.Emit.Tests;

// A fixed-form wrapper reaches the cloned original through a delegate, so every target signature the
// fast path accepts has to be expressible as one. These record what Expression.GetDelegateType covers.
public sealed class DelegateTypeLimitsTests {
    public static TheoryData<string, Type[]> Signatures() {
        Type[] wide = new Type[20];
        for (int i = 0; i < 20; i++) {
            wide[i] = typeof(int);
        }

        return new TheoryData<string, Type[]> {
            { "value return", [typeof(int), typeof(int), typeof(int)] },
            { "void return", [typeof(int), typeof(void)] },
            { "ref parameter", [typeof(int).MakeByRefType(), typeof(int)] },
            { "out parameter", [typeof(string).MakeByRefType(), typeof(void)] },
            { "byref return", [typeof(int), typeof(int).MakeByRefType()] },
            { "19 parameters", wide },
        };
    }

    [Theory]
    [MemberData(nameof(Signatures))]
    public void EverySupportedSignatureHasADelegateType(string label, Type[] signature) {
        Type delegateType = Expression.GetDelegateType(signature);

        Assert.NotNull(delegateType.GetMethod("Invoke"));
        Assert.True(typeof(Delegate).IsAssignableFrom(delegateType), label);
    }
}
