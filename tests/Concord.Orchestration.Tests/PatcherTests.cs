using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Concord.Orchestration.Tests.RollbackAssembly;
using Xunit;

namespace Concord.Orchestration.Tests;

[CollectionDefinition(Name)]
public sealed class SharedAssemblyApplyCollection {
    public const string Name = "SharedAssemblyApply";
}

public static class PatcherApplyTarget {
    public static int Compute() {
        return 1;
    }
}

public static class PatcherImperativeTarget {
    public static int Compute() {
        return 1;
    }
}

[Patch(typeof(PatcherApplyTarget))]
public static class PatcherApplyDeclaration {
    [Inject(At.Head, nameof(PatcherApplyTarget.Compute))]
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 42;
        ch.Cancel();
    }
}

public static class PatcherImperativeInjectionMethod {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public static class ConcurrentTargetA {
    public static int Value() {
        return 1;
    }
}

public static class ConcurrentTargetB {
    public static int Value() {
        return 2;
    }
}

public static class ConcurrentTargetC {
    public static int Value() {
        return 3;
    }
}

public static class ConcurrentTargetD {
    public static int Value() {
        return 4;
    }
}

public static class PatcherGcTarget {
    public static int Compute() {
        return 1;
    }
}

public static class PatcherGcInjectionMethod {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public static class PatcherReturnTarget {
    public static int Compute() {
        return 1;
    }
}

public static class PatcherReturnInjectionMethod {
    public static void OnReturn(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class PatcherTests {
    [Fact]
    public void Apply_AppliesDetour_ChangesReturnValue_DisposeReverts() {
        Assert.Equal(1, PatcherApplyTarget.Compute());

        IPatchHandle handle = Patcher.Apply(typeof(PatcherApplyDeclaration).Assembly);
        try {
            Assert.True(handle.IsApplied);
            Assert.NotEmpty(handle.Detours);
            Assert.Equal(42, PatcherApplyTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.False(handle.IsApplied);
        Assert.Equal(1, PatcherApplyTarget.Compute());
    }

    [Fact]
    public void Apply_Twice_SameAssembly_ReturnsSameHandle_NoDoubleApply() {
        IPatchHandle first = Patcher.Apply(typeof(PatcherApplyDeclaration).Assembly);
        try {
            IPatchHandle second = Patcher.Apply(typeof(PatcherApplyDeclaration).Assembly);
            Assert.Same(first, second);
            Assert.Equal(first.Detours.Count, second.Detours.Count);
        } finally {
            first.Dispose();
        }
    }

    [Fact]
    public void Apply_WhenScanThrows_RevertsAlreadyAppliedDetours() {
        Assembly rollbackAssembly = typeof(RollbackGoodTarget).Assembly;

        Assert.Equal(7, RollbackGoodTarget.Compute());

        Assert.Throws<ConcordEmitException>(() => Patcher.Apply(rollbackAssembly));

        Assert.Equal(7, RollbackGoodTarget.Compute());
    }

    [Fact]
    public void Patch_SingleExplicit_ChangesReturnValue_DisposeReverts() {
        MethodBase target = typeof(PatcherImperativeTarget).GetMethod(nameof(PatcherImperativeTarget.Compute))!;
        MethodBase injectionMethod = typeof(PatcherImperativeInjectionMethod).GetMethod(nameof(PatcherImperativeInjectionMethod.OnCompute))!;

        Assert.Equal(1, PatcherImperativeTarget.Compute());

        IPatchHandle handle = Patcher.Patch(target, injectionMethod, At.Head);
        try {
            Assert.Equal(99, PatcherImperativeTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(1, PatcherImperativeTarget.Compute());
    }

    [Fact]
    public void PatchInjection_ReturnPosition_RootsHandle_ChangesReturnValue_DisposeReverts() {
        MethodBase target = typeof(PatcherReturnTarget).GetMethod(nameof(PatcherReturnTarget.Compute))!;
        MethodBase injectionMethod = typeof(PatcherReturnInjectionMethod).GetMethod(nameof(PatcherReturnInjectionMethod.OnReturn))!;
        Injection injection = new Injection((MethodInfo)injectionMethod, new InjectAt.Return(0), injectionMethod.DeclaringType!.FullName!, 0);

        Assert.Equal(1, PatcherReturnTarget.Compute());

        IPatchHandle handle = Patcher.PatchInjection(target, injection);
        try {
            Assert.True(handle.IsApplied);
            Assert.Equal(99, PatcherReturnTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(1, PatcherReturnTarget.Compute());
    }

    [Fact]
    public void Patch_DiscardedHandle_SurvivesGarbageCollection() {
        Assert.Equal(1, PatcherGcTarget.Compute());

        ApplyAndDiscard();

        for (int i = 0; i < 3; i++) {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(99, PatcherGcTarget.Compute());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyAndDiscard() {
        MethodBase target = typeof(PatcherGcTarget).GetMethod(nameof(PatcherGcTarget.Compute))!;
        MethodBase injectionMethod = typeof(PatcherGcInjectionMethod).GetMethod(nameof(PatcherGcInjectionMethod.OnCompute))!;
        Patcher.Patch(target, injectionMethod, At.Head);
    }

    [Fact]
    public void Patch_InjectionMethodWithNoDeclaringType_ThrowsConcordDeclarationException() {
        MethodBase target = typeof(PatcherImperativeTarget).GetMethod(nameof(PatcherImperativeTarget.Compute))!;
        System.Reflection.Emit.DynamicMethod dynamic = new System.Reflection.Emit.DynamicMethod("orphan", typeof(void), Type.EmptyTypes);

        Assert.Throws<ConcordDeclarationException>(() => Patcher.Patch(target, dynamic, At.Head));
    }

    [Fact]
    public void ConcurrentPatch_DifferentTargets_AllApply() {
        MethodBase[] targets = [
            typeof(ConcurrentTargetA).GetMethod(nameof(ConcurrentTargetA.Value))!,
            typeof(ConcurrentTargetB).GetMethod(nameof(ConcurrentTargetB.Value))!,
            typeof(ConcurrentTargetC).GetMethod(nameof(ConcurrentTargetC.Value))!,
            typeof(ConcurrentTargetD).GetMethod(nameof(ConcurrentTargetD.Value))!
        ];
        MethodBase injection = typeof(PatcherImperativeInjectionMethod).GetMethod(nameof(PatcherImperativeInjectionMethod.OnCompute))!;

        IPatchHandle[] handles = new IPatchHandle[targets.Length];
        System.Threading.Tasks.Parallel.For(0, targets.Length, i => handles[i] = Patcher.Patch(targets[i], injection, At.Head));
        try {
            Assert.All(handles, h => Assert.True(h.IsApplied));
        } finally {
            foreach (IPatchHandle h in handles) {
                h.Dispose();
            }
        }
    }
}
