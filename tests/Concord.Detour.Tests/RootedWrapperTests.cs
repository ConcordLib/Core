using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Detour.Tests;

public static class RootedWrapperTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }
}

public static class RootedWrapperInjections {
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }

    public static void AddTwo(ControlHandle<int> ch) {
        ch.ReturnValue += 2;
    }
}

public sealed class RootedWrapperTests {
    [Fact]
    public void EveryInstalledWrapperIsRootedAndNeverReleased() {
        MethodBase target = typeof(RootedWrapperTarget).GetMethod(nameof(RootedWrapperTarget.Value))!;
        MethodInfo addOne = typeof(RootedWrapperInjections).GetMethod(nameof(RootedWrapperInjections.AddOne))!;
        MethodInfo addTwo = typeof(RootedWrapperInjections).GetMethod(nameof(RootedWrapperInjections.AddTwo))!;
        IDetourBackend backend = new MonoModDetourBackend();

        int start = TargetDetourRegistry.RootedWrapperCount;

        IDetourHandle first = backend.ApplyComposed(target, [new Injection(addOne, new InjectAt.Tail(), "A", 0)]);
        Assert.Equal(start + 1, TargetDetourRegistry.RootedWrapperCount);

        IDetourHandle second = backend.ApplyComposed(target, [new Injection(addTwo, new InjectAt.Tail(), "B", 1)]);
        Assert.Equal(start + 2, TargetDetourRegistry.RootedWrapperCount);

        second.Dispose();
        first.Dispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(start + 3, TargetDetourRegistry.RootedWrapperCount);
        Assert.Equal(1, RootedWrapperTarget.Value());
    }
}
