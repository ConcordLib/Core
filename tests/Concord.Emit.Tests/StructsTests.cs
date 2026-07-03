using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public struct Vec {
    public int X;
    public void Bump() { X += 1; }
}

public sealed class StructsTests {
    [Fact]
    public void Compose_StructSpine_MutationWritesThroughToCallerInstance() {
        MethodBase target = typeof(Vec).GetMethod(nameof(Vec.Bump))!;

        ComposeResult result = WrapperComposer.Compose(target, []);
        BumpDel del = (BumpDel)result.Wrapper.CreateDelegate(typeof(BumpDel));

        Vec instance = new Vec { X = 0 };
        del(ref instance);

        Assert.Equal(1, instance.X);
    }

    [Fact]
    public void Compose_StructSpine_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(Vec).GetMethod(nameof(Vec.Bump))!;

        ComposeResult result = WrapperComposer.Compose(target, []);
        BumpDel del = (BumpDel)result.Wrapper.CreateDelegate(typeof(BumpDel));

        Vec instance = new Vec { X = 0 };
        del(ref instance);

        long before = TestPolyfills.GetAllocatedBytes();
        for (int i = 0; i < 10; i++) {
            del(ref instance);
        }

        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }

    private delegate void BumpDel(ref Vec v);
}
