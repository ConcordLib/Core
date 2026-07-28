using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Detour.Tests;

public static class EvictionTargetA {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 5;
    }
}

public static class EvictionTargetB {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 5;
    }
}

public static class EvictionTargetC {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 5;
    }
}

public static class EvictionTargetD {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 5;
    }
}

public static class EvictionInjections {
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }

    public static IEnumerable<CodeInstruction> Throws(IEnumerable<CodeInstruction> instructions) {
        throw new InvalidOperationException("author blew up");
    }

    internal static int FlakyCalls;

    public static IEnumerable<CodeInstruction> FlakyOnSecondCall(IEnumerable<CodeInstruction> instructions) {
        FlakyCalls++;
        if (FlakyCalls == 2) {
            throw new InvalidOperationException("flaky transpiler blew up");
        }

        return instructions;
    }
}

public sealed class TranspilerEvictionTests {
    private static Injection Good() {
        MethodInfo method = typeof(EvictionInjections).GetMethod(nameof(EvictionInjections.AddOne))!;
        return new Injection(method, new InjectAt.Tail(), "good", 0);
    }

    private static Injection Bad() {
        MethodInfo method = typeof(EvictionInjections).GetMethod(nameof(EvictionInjections.Throws))!;
        return new Injection(method, new InjectAt.Transpiler(false), "bad", 0);
    }

    [Fact]
    public void ThrowingTranspiler_LeavesOtherModsPatchIntact() {
        MethodBase target = typeof(EvictionTargetA).GetMethod(nameof(EvictionTargetA.Value))!;
        IDetourBackend backend = new MonoModDetourBackend();

        IDetourHandle good = backend.ApplyComposed(target, [Good()]);
        Assert.Equal(6, EvictionTargetA.Value());

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => backend.ApplyComposed(target, [Bad()]));
        Assert.Equal("CONC117", error.Code);

        Assert.True(good.IsApplied);
        Assert.Equal(6, EvictionTargetA.Value());

        good.Dispose();
        Assert.Equal(5, EvictionTargetA.Value());
    }

    [Fact]
    public void ThrowingTranspiler_TargetRemainsUsableAfterward() {
        MethodBase target = typeof(EvictionTargetB).GetMethod(nameof(EvictionTargetB.Value))!;
        IDetourBackend backend = new MonoModDetourBackend();

        Assert.Throws<ConcordEmitException>(() => backend.ApplyComposed(target, [Bad()]));
        Assert.Equal(5, EvictionTargetB.Value());

        using IDetourHandle good = backend.ApplyComposed(target, [Good()]);
        Assert.Equal(6, EvictionTargetB.Value());
    }

    [Fact]
    public void ThrowingTranspiler_DoesNotPreventRemovingAGoodPatch() {
        MethodBase target = typeof(EvictionTargetC).GetMethod(nameof(EvictionTargetC.Value))!;
        IDetourBackend backend = new MonoModDetourBackend();

        IDetourHandle good = backend.ApplyComposed(target, [Good()]);
        Assert.Equal(6, EvictionTargetC.Value());

        Assert.Throws<ConcordEmitException>(() => backend.ApplyComposed(target, [Bad()]));
        Assert.Equal(6, EvictionTargetC.Value());

        good.Dispose();
        Assert.False(good.IsApplied);
        Assert.Equal(5, EvictionTargetC.Value());
    }

    [Fact]
    public void ThrowingTranspiler_DuringRemove_KeepsBookkeepingAndWrapperInSync() {
        MethodBase target = typeof(EvictionTargetD).GetMethod(nameof(EvictionTargetD.Value))!;
        IDetourBackend backend = new MonoModDetourBackend();
        EvictionInjections.FlakyCalls = 0;

        MethodInfo addOneMethod = typeof(EvictionInjections).GetMethod(nameof(EvictionInjections.AddOne))!;
        MethodInfo flakyMethod = typeof(EvictionInjections).GetMethod(nameof(EvictionInjections.FlakyOnSecondCall))!;
        Injection good = new Injection(addOneMethod, new InjectAt.Tail(), "good", 0);
        Injection flaky = new Injection(flakyMethod, new InjectAt.Transpiler(false), "flaky", 0);

        IDetourHandle goodHandle = backend.ApplyComposed(target, [good]);
        Assert.Equal(6, EvictionTargetD.Value());

        IDetourHandle flakyHandle = backend.ApplyComposed(target, [flaky]);
        Assert.Equal(6, EvictionTargetD.Value());

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => goodHandle.Dispose());
        Assert.Equal("CONC117", error.Code);
        Assert.Equal(6, EvictionTargetD.Value());

        flakyHandle.Dispose();

        Assert.True(goodHandle.IsApplied);
        Assert.Equal(6, EvictionTargetD.Value());

        goodHandle.Dispose();
        Assert.False(goodHandle.IsApplied);
        Assert.Equal(5, EvictionTargetD.Value());
    }
}
