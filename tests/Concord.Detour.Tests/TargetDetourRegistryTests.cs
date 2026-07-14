using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Detour.Tests;

public static class OrderedRegistryTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }
}

public static class OrderedRegistryInjections {
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }

    public static void MultiplyTen(ControlHandle<int> ch) {
        ch.ReturnValue *= 10;
    }
}

public sealed class TargetDetourRegistryTests {
    [Fact]
    public void ApplyComposed_CycleLeavesActiveWrapperUnchanged() {
        MethodBase target = typeof(OrderedRegistryTarget).GetMethod(nameof(OrderedRegistryTarget.Value))!;
        MethodInfo addOne = typeof(OrderedRegistryInjections).GetMethod(nameof(OrderedRegistryInjections.AddOne))!;
        MethodInfo multiplyTen = typeof(OrderedRegistryInjections).GetMethod(nameof(OrderedRegistryInjections.MultiplyTen))!;
        IDetourBackend backend = new MonoModDetourBackend();
        Injection first = new Injection(addOne, new InjectAt.Tail(), "A", 0) {
            BeforeOwners = ["B"]
        };
        Injection cyclic = new Injection(multiplyTen, new InjectAt.Tail(), "B", 0) {
            BeforeOwners = ["A"]
        };

        using IDetourHandle handle = backend.ApplyComposed(target, [first]);
        Assert.Equal(2, OrderedRegistryTarget.Value());

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => backend.ApplyComposed(target, [cyclic]));

        Assert.Equal("CONC052", error.Code);
        Assert.True(handle.IsApplied);
        Assert.Equal(2, OrderedRegistryTarget.Value());
    }
}
