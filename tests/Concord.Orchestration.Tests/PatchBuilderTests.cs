using System.Reflection;
using System.Runtime.CompilerServices;
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
    public static void OnStep(ControlHandle ch) {
        BuilderLog.Entries.Add("pre");
        BuilderAroundTarget2.Step();
        BuilderLog.Entries.Add("post");
    }
}

public static class BuilderInvokeInjectionMethod2 {
    public static void BeforeStep(ControlHandle ch) {
        BuilderLog.Entries.Add("injected");
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
}
