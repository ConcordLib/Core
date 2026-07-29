using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class BuilderMethodBaseTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderTypeTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public sealed class BuilderGenericTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderStringTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderGcTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderReturnTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderAroundTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Step() {
        BuilderLog.Entries.Add("spine");
    }
}

public static class BuilderTranspilerTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 5;
    }
}

public static class BuilderTranspilerFinalTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 5;
    }
}

public static class BuilderTranspilerBehaviorTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 5;
    }
}

public static class BuilderTranspilerChainTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 5;
    }
}

public static class BuilderTranspilers {
    public static IEnumerable<CodeInstruction> FiveToTen(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.opcode == OpCodes.Ldc_I4_5) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 10);
            } else {
                yield return instruction;
            }
        }
    }

    public static IEnumerable<CodeInstruction> TenToTwenty(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.Is(OpCodes.Ldc_I4, 10)) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 20);
            } else {
                yield return instruction;
            }
        }
    }
}

public static class BuilderLog {
    public static List<string> Entries = new List<string>();

    public static void Clear() {
        Entries.Clear();
    }
}

public static class BuilderInvokeHelper2 {
    public static void Step() {
        BuilderLog.Entries.Add("step");
    }
}

public class BuilderInvokeTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Run() {
        BuilderLog.Entries.Add("before");
        BuilderInvokeHelper2.Step();
        BuilderLog.Entries.Add("after");
    }
}

public static class BuilderSliceAnchors2 {
    public static void Begin() { }

    public static void End() { }
}

internal class BuilderSliceTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Run() {
        BuilderInvokeHelper2.Step();
        BuilderSliceAnchors2.Begin();
        BuilderInvokeHelper2.Step();
        BuilderSliceAnchors2.End();
        BuilderInvokeHelper2.Step();
    }
}

public sealed class BuilderNewObjValue2 {
    public BuilderNewObjValue2(int value) {
        Value = value;
    }

    public int Value { get; }
}

public class BuilderNewObjTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Run(int value) {
        BuilderNewObjValue2 created = new BuilderNewObjValue2(value);
        return created.Value;
    }
}

public static class BuilderHeadInjectionMethod2 {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public static class BuilderGcInjectionMethod2 {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public static class BuilderReturnInjectionMethod2 {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 77;
    }
}

public static class BuilderAroundInjectionMethod2 {
    public static void OnStep(Operation original) {
        BuilderLog.Entries.Add("pre");
        original.Invoke();
        BuilderLog.Entries.Add("post");
    }
}

public static class BuilderInvokeInjectionMethod2 {
    public static void BeforeStep(ControlHandle ch) {
        BuilderLog.Entries.Add("injected");
    }
}

public static class BuilderNewObjInjectionMethod2 {
    public static int Bump(int original) {
        return original + 1;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class PatchBuilderTests {
    [Fact]
    public void For_MethodBase_HeadPatch_ChangesReturnValue_DisposeReverts() {
        Assert.Equal(1, BuilderMethodBaseTarget.Compute());

        MethodBase target = typeof(BuilderMethodBaseTarget).GetMethod(nameof(BuilderMethodBaseTarget.Compute))!;
        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        IPatchHandle handle = Patcher.For(target).Head(injectionMethod).Apply();
        try {
            Assert.Equal(99, BuilderMethodBaseTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(1, BuilderMethodBaseTarget.Compute());
    }

    [Fact]
    public void For_TypeAndName_HeadPatch_ChangesReturnValue_DisposeReverts() {
        Assert.Equal(1, BuilderTypeTarget.Compute());

        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        IPatchHandle handle = Patcher.For(typeof(BuilderTypeTarget), nameof(BuilderTypeTarget.Compute))
            .Head(injectionMethod)
            .Apply();
        try {
            Assert.Equal(99, BuilderTypeTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(1, BuilderTypeTarget.Compute());
    }

    [Fact]
    public void For_Generic_HeadPatch_ChangesReturnValue_DisposeReverts() {
        Assert.Equal(1, BuilderGenericTarget.Compute());

        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        IPatchHandle handle = Patcher.For<BuilderGenericTarget>(nameof(BuilderGenericTarget.Compute))
            .Head(injectionMethod)
            .Apply();
        try {
            Assert.Equal(99, BuilderGenericTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(1, BuilderGenericTarget.Compute());
    }

    [Fact]
    public void For_StringTypeName_HeadPatch_ChangesReturnValue_DisposeReverts() {
        Assert.Equal(1, BuilderStringTarget.Compute());

        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;
        string typeName = typeof(BuilderStringTarget).FullName!;

        IPatchHandle handle = Patcher.For(typeName, nameof(BuilderStringTarget.Compute))
            .Head(injectionMethod)
            .Apply();
        try {
            Assert.Equal(99, BuilderStringTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(1, BuilderStringTarget.Compute());
    }

    [Fact]
    public void Apply_DiscardedHandle_SurvivesGarbageCollection() {
        Assert.Equal(1, BuilderGcTarget2.Compute());

        ApplyAndDiscardViaBuilder();

        for (int i = 0; i < 3; i++) {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(99, BuilderGcTarget2.Compute());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyAndDiscardViaBuilder() {
        MethodBase target = typeof(BuilderGcTarget2).GetMethod(nameof(BuilderGcTarget2.Compute))!;
        MethodInfo injectionMethod = typeof(BuilderGcInjectionMethod2).GetMethod(nameof(BuilderGcInjectionMethod2.OnCompute))!;
        Patcher.For(target).Head(injectionMethod).Apply();
    }

    [Fact]
    public void Head_MethodInfo_And_TypeName_AreEquivalent() {
        Assert.Equal(1, BuilderTypeTarget.Compute());

        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        IPatchHandle h1 = Patcher.For(typeof(BuilderTypeTarget), nameof(BuilderTypeTarget.Compute))
            .Head(injectionMethod)
            .Apply();
        try {
            Assert.Equal(99, BuilderTypeTarget.Compute());
        } finally {
            h1.Dispose();
        }

        Assert.Equal(1, BuilderTypeTarget.Compute());

        IPatchHandle h2 = Patcher.For(typeof(BuilderTypeTarget), nameof(BuilderTypeTarget.Compute))
            .Head(typeof(BuilderHeadInjectionMethod2), nameof(BuilderHeadInjectionMethod2.OnCompute))
            .Apply();
        try {
            Assert.Equal(99, BuilderTypeTarget.Compute());
        } finally {
            h2.Dispose();
        }

        Assert.Equal(1, BuilderTypeTarget.Compute());
    }

    [Fact]
    public void Inject_AtHead_IsEquivalentTo_Head() {
        Assert.Equal(1, BuilderTypeTarget.Compute());

        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        IPatchHandle handle = Patcher.For(typeof(BuilderTypeTarget), nameof(BuilderTypeTarget.Compute))
            .Inject(At.Head, injectionMethod)
            .Apply();
        try {
            Assert.Equal(99, BuilderTypeTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(1, BuilderTypeTarget.Compute());
    }

    [Fact]
    public void Return_InjectionMethod_FiresAtReturnSite() {
        Assert.Equal(1, BuilderReturnTarget2.Compute());

        MethodInfo injectionMethod = typeof(BuilderReturnInjectionMethod2).GetMethod(nameof(BuilderReturnInjectionMethod2.OnCompute))!;

        IPatchHandle handle = Patcher.For(typeof(BuilderReturnTarget2), nameof(BuilderReturnTarget2.Compute))
            .Return(injectionMethod)
            .Apply();
        try {
            Assert.Equal(77, BuilderReturnTarget2.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(1, BuilderReturnTarget2.Compute());
    }

    [Fact]
    public void Around_InjectionMethod_WrapsSpine() {
        BuilderLog.Clear();

        MethodInfo injectionMethod = typeof(BuilderAroundInjectionMethod2).GetMethod(nameof(BuilderAroundInjectionMethod2.OnStep))!;

        IPatchHandle handle = Patcher.For(typeof(BuilderAroundTarget2), nameof(BuilderAroundTarget2.Step))
            .Around(injectionMethod)
            .Apply();
        try {
            BuilderAroundTarget2.Step();
            Assert.Equal(["pre", "spine", "post"], BuilderLog.Entries);
        } finally {
            handle.Dispose();
        }

        BuilderLog.Clear();
        BuilderAroundTarget2.Step();
        Assert.Equal(["spine"], BuilderLog.Entries);
    }

    [Fact]
    public void Invoke_Builder_SplicesBeforeCallSite() {
        BuilderLog.Clear();

        MethodBase targetMethod = typeof(BuilderInvokeTarget2).GetMethod(nameof(BuilderInvokeTarget2.Run))!;
        MethodInfo injectionMethod = typeof(BuilderInvokeInjectionMethod2).GetMethod(nameof(BuilderInvokeInjectionMethod2.BeforeStep))!;

        IPatchHandle handle = Patcher.For(targetMethod)
            .Invoke(typeof(BuilderInvokeHelper2), nameof(BuilderInvokeHelper2.Step), injectionMethod, At.Head)
            .Apply();
        try {
            BuilderInvokeTarget2 instance = new BuilderInvokeTarget2();
            instance.Run();
            Assert.Equal(["before", "injected", "step", "after"], BuilderLog.Entries);
        } finally {
            handle.Dispose();
        }

        BuilderLog.Clear();
        BuilderInvokeTarget2 instance2 = new BuilderInvokeTarget2();
        instance2.Run();
        Assert.Equal(["before", "step", "after"], BuilderLog.Entries);
    }

    [Fact]
    public void NewObj_Builder_RewritesConstructorArgument() {
        Assert.Equal(3, new BuilderNewObjTarget2().Run(3));

        MethodBase targetMethod = typeof(BuilderNewObjTarget2).GetMethod(nameof(BuilderNewObjTarget2.Run))!;
        MethodInfo injectionMethod = typeof(BuilderNewObjInjectionMethod2).GetMethod(nameof(BuilderNewObjInjectionMethod2.Bump))!;

        IPatchHandle handle = Patcher.For(targetMethod)
            .NewObj(typeof(BuilderNewObjValue2), [typeof(int)], injectionMethod, At.Argument)
            .Slice(new SliceRange(null, null, 1, null, null, 1))
            .Apply();
        try {
            Assert.Equal(4, new BuilderNewObjTarget2().Run(3));
        } finally {
            handle.Dispose();
        }

        Assert.Equal(3, new BuilderNewObjTarget2().Run(3));
    }

    [Fact]
    public void Slice_Builder_BoundsMostRecentInvokeInjection() {
        BuilderLog.Clear();

        MethodBase targetMethod = typeof(BuilderSliceTarget2).GetMethod(nameof(BuilderSliceTarget2.Run))!;
        MethodInfo injectionMethod = typeof(BuilderInvokeInjectionMethod2).GetMethod(nameof(BuilderInvokeInjectionMethod2.BeforeStep))!;
        SliceRange range = new SliceRange(
            typeof(BuilderSliceAnchors2),
            nameof(BuilderSliceAnchors2.Begin),
            1,
            typeof(BuilderSliceAnchors2),
            nameof(BuilderSliceAnchors2.End),
            1);

        IPatchHandle handle = Patcher.For(targetMethod)
            .Invoke(typeof(BuilderInvokeHelper2), nameof(BuilderInvokeHelper2.Step), injectionMethod, At.Head)
            .Slice(range)
            .Apply();
        try {
            new BuilderSliceTarget2().Run();
            Assert.Equal(["step", "injected", "step", "step"], BuilderLog.Entries);
        } finally {
            handle.Dispose();
        }
    }

    [Fact]
    public void Transpiler_MethodInfo_RegistersSuccessfully() {
        MethodBase target = typeof(BuilderTranspilerTarget).GetMethod(nameof(BuilderTranspilerTarget.Compute))!;
        MethodInfo transpiler = typeof(BuilderTranspilers).GetMethod(nameof(BuilderTranspilers.FiveToTen))!;

        using IPatchHandle handle = Patcher.For(target).Transpiler(transpiler).Apply();
        Assert.NotNull(handle);
    }

    [Fact]
    public void Transpiler_TypeAndName_RegistersSuccessfully() {
        MethodBase target = typeof(BuilderTranspilerTarget).GetMethod(nameof(BuilderTranspilerTarget.Compute))!;

        using IPatchHandle handle = Patcher.For(target)
            .Transpiler(typeof(BuilderTranspilers), nameof(BuilderTranspilers.FiveToTen))
            .Apply();
        Assert.NotNull(handle);
    }

    [Fact]
    public void Transpiler_Final_MethodInfo_RegistersSuccessfully() {
        MethodBase target = typeof(BuilderTranspilerFinalTarget).GetMethod(nameof(BuilderTranspilerFinalTarget.Compute))!;
        MethodInfo transpiler = typeof(BuilderTranspilers).GetMethod(nameof(BuilderTranspilers.FiveToTen))!;

        using IPatchHandle handle = Patcher.For(target).Transpiler(transpiler, final: true).Apply();
        Assert.NotNull(handle);
    }

    [Fact]
    public void Transpiler_Final_TypeAndName_RegistersSuccessfully() {
        MethodBase target = typeof(BuilderTranspilerFinalTarget).GetMethod(nameof(BuilderTranspilerFinalTarget.Compute))!;

        using IPatchHandle handle = Patcher.For(target)
            .Transpiler(typeof(BuilderTranspilers), nameof(BuilderTranspilers.FiveToTen), final: true)
            .Apply();
        Assert.NotNull(handle);
    }

    [Fact]
    public void Transpiler_ChangesObservableBehavior_DisposeReverts() {
        Assert.Equal(5, BuilderTranspilerBehaviorTarget.Compute());

        MethodBase target = typeof(BuilderTranspilerBehaviorTarget).GetMethod(nameof(BuilderTranspilerBehaviorTarget.Compute))!;
        MethodInfo transpiler = typeof(BuilderTranspilers).GetMethod(nameof(BuilderTranspilers.FiveToTen))!;

        IPatchHandle handle = Patcher.For(target).Transpiler(transpiler).Apply();
        try {
            Assert.Equal(10, BuilderTranspilerBehaviorTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(5, BuilderTranspilerBehaviorTarget.Compute());
    }

    [Fact]
    public void Transpiler_Chained_ThroughPatcherFor_AppliesInRegistrationOrder() {
        Assert.Equal(5, BuilderTranspilerChainTarget.Compute());

        MethodBase target = typeof(BuilderTranspilerChainTarget).GetMethod(nameof(BuilderTranspilerChainTarget.Compute))!;

        IPatchHandle handle = Patcher.For(target)
            .Transpiler(typeof(BuilderTranspilers), nameof(BuilderTranspilers.FiveToTen))
            .Transpiler(typeof(BuilderTranspilers), nameof(BuilderTranspilers.TenToTwenty))
            .Apply();
        try {
            Assert.Equal(20, BuilderTranspilerChainTarget.Compute());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(5, BuilderTranspilerChainTarget.Compute());
    }
}
