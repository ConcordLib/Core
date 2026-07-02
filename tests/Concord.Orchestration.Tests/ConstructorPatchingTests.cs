using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

internal sealed class FluentCtorTarget {
    public int Value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public FluentCtorTarget(int seed) {
        Value = seed;
    }
}

internal static class FluentCtorLog {
    public static int HeadFired;

    public static void Reset() {
        HeadFired = 0;
    }
}

internal static class FluentCtorInjectionMethod {
    public static void OnConstruct(ControlHandle ch) {
        FluentCtorLog.HeadFired++;
    }
}

internal sealed class AttrCtorTarget {
    public int Value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public AttrCtorTarget() {
        Value = 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public AttrCtorTarget(int seed) {
        Value = seed;
    }
}

internal static class AttrCtorLog {
    public static int HeadFired;

    public static void Reset() {
        HeadFired = 0;
    }
}

[Patch(typeof(AttrCtorTarget))]
internal static class AttrCtorDeclaration {
    [Inject(At.Head)]
    public static void OnConstruct(ControlHandle ch) {
        AttrCtorLog.HeadFired++;
    }
}

internal sealed class OverloadedCtorTarget {
    public string Which;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public OverloadedCtorTarget() {
        Which = "default";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public OverloadedCtorTarget(int x) {
        Which = "int:" + x;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public OverloadedCtorTarget(string s) {
        Which = "string:" + s;
    }
}

internal static class OverloadedCtorLog {
    public static int IntCtorHits;
    public static int StringCtorHits;

    public static void Reset() {
        IntCtorHits = 0;
        StringCtorHits = 0;
    }
}

internal static class OverloadedCtorIntInjectionMethod {
    public static void OnIntCtor(ControlHandle ch) {
        OverloadedCtorLog.IntCtorHits++;
    }
}

internal static class StaticCtorOnlyType {
    static StaticCtorOnlyType() { }
}

internal sealed class WireCtorOverloadTarget {
    public string Which;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public WireCtorOverloadTarget() {
        Which = "default";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public WireCtorOverloadTarget(int x) {
        Which = "int:" + x;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public WireCtorOverloadTarget(string s) {
        Which = "string:" + s;
    }
}

[Patch(typeof(WireCtorOverloadTarget))]
internal static class OverloadedCtorAttrDeclaration {
    [Inject(At.Head, parameterTypes: [typeof(int)])]
    public static void OnIntConstruct(ControlHandle ch) {
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class ConstructorPatchingTests {
    [Fact]
    public void ForConstructor_Type_HeadFires_OriginalRuns_DisposeReverts() {
        FluentCtorLog.Reset();
        FluentCtorTarget before = new FluentCtorTarget(10);
        Assert.Equal(0, FluentCtorLog.HeadFired);
        Assert.Equal(10, before.Value);

        MethodInfo injectionMethod = typeof(FluentCtorInjectionMethod).GetMethod(nameof(FluentCtorInjectionMethod.OnConstruct))!;
        IPatchHandle handle = Patcher.ForConstructor(typeof(FluentCtorTarget), [typeof(int)])
            .Head(injectionMethod)
            .Apply();
        try {
            FluentCtorLog.Reset();
            FluentCtorTarget after = new FluentCtorTarget(42);
            Assert.Equal(1, FluentCtorLog.HeadFired);
            Assert.Equal(42, after.Value);
        } finally {
            handle.Dispose();
        }

        FluentCtorLog.Reset();
        FluentCtorTarget reverted = new FluentCtorTarget(7);
        Assert.Equal(0, FluentCtorLog.HeadFired);
        Assert.Equal(7, reverted.Value);
    }

    [Fact]
    public void ForConstructorGeneric_HeadFires_OriginalRuns_DisposeReverts() {
        FluentCtorLog.Reset();

        MethodInfo injectionMethod = typeof(FluentCtorInjectionMethod).GetMethod(nameof(FluentCtorInjectionMethod.OnConstruct))!;
        IPatchHandle handle = Patcher.ForConstructor<FluentCtorTarget>([typeof(int)])
            .Head(injectionMethod)
            .Apply();
        try {
            FluentCtorLog.Reset();
            FluentCtorTarget instance = new FluentCtorTarget(99);
            Assert.Equal(1, FluentCtorLog.HeadFired);
            Assert.Equal(99, instance.Value);
        } finally {
            handle.Dispose();
        }

        FluentCtorLog.Reset();
        FluentCtorTarget reverted = new FluentCtorTarget(5);
        Assert.Equal(0, FluentCtorLog.HeadFired);
        Assert.Equal(5, reverted.Value);
    }

    [Fact]
    public void ScanType_InjectTargetsConstructor_AppliesCtorAsPatchTarget() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(AttrCtorDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        ConstructorInfo? expectedCtor = typeof(AttrCtorTarget).GetConstructor(Type.EmptyTypes);
        Assert.Equal(expectedCtor, call.Target);
        Assert.IsType<InjectAt.Head>(call.Injection.At);
    }

    [Fact]
    public void InjectTargetsConstructor_Behavior_HeadFires_OriginalRuns() {
        AttrCtorLog.Reset();
        AttrCtorTarget before = new AttrCtorTarget();
        Assert.Equal(0, AttrCtorLog.HeadFired);
        Assert.Equal(0, before.Value);

        IPatchHandle handle = Patcher.Apply(typeof(AttrCtorDeclaration).Assembly);
        try {
            AttrCtorLog.Reset();
            AttrCtorTarget after = new AttrCtorTarget();
            Assert.Equal(1, AttrCtorLog.HeadFired);
            Assert.Equal(0, after.Value);
        } finally {
            handle.Dispose();
        }

        AttrCtorLog.Reset();
        AttrCtorTarget reverted = new AttrCtorTarget();
        Assert.Equal(0, AttrCtorLog.HeadFired);
        Assert.Equal(0, reverted.Value);
    }

    [Fact]
    public void ForConstructor_OverloadedCtors_IntOverload_SelectsOnlyIntCtor() {
        OverloadedCtorLog.Reset();

        MethodInfo injectionMethod = typeof(OverloadedCtorIntInjectionMethod).GetMethod(nameof(OverloadedCtorIntInjectionMethod.OnIntCtor))!;
        IPatchHandle handle = Patcher.ForConstructor(typeof(OverloadedCtorTarget), [typeof(int)])
            .Head(injectionMethod)
            .Apply();
        try {
            OverloadedCtorLog.Reset();
            OverloadedCtorTarget fromInt = new OverloadedCtorTarget(5);
            OverloadedCtorTarget fromString = new OverloadedCtorTarget("hello");
            Assert.Equal(1, OverloadedCtorLog.IntCtorHits);
            Assert.Equal("int:5", fromInt.Which);
            Assert.Equal("string:hello", fromString.Which);
        } finally {
            handle.Dispose();
        }
    }

    [Fact]
    public void ScanType_InjectTargetsConstructor_WithParameterTypes_ResolvesOverloadedCtor() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(OverloadedCtorAttrDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        ConstructorInfo? expectedCtor = typeof(WireCtorOverloadTarget).GetConstructor([typeof(int)]);
        Assert.Equal(expectedCtor, call.Target);
        Assert.IsType<InjectAt.Head>(call.Injection.At);
    }

    [Fact]
    public void ForConstructor_EmptyParamTypes_SelectsParameterlessCtor() {
        ConstructorInfo? parameterless = typeof(OverloadedCtorTarget).GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);
        Assert.NotNull(parameterless);

        PatchBuilder builder = Patcher.ForConstructor(typeof(OverloadedCtorTarget), []);
        Assert.NotNull(builder);
    }

    [Fact]
    public void ForConstructor_NoMatchingCtor_ThrowsConcordDeclarationException() {
        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => Patcher.ForConstructor(typeof(FluentCtorTarget), [typeof(double)]));
        Assert.Contains("instance constructor", ex.Message);
        Assert.Contains(typeof(FluentCtorTarget).FullName!, ex.Message);
    }

    [Fact]
    public void ForConstructor_StaticCtorOnlyType_ThrowsConcordDeclarationException() {
        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => Patcher.ForConstructor(typeof(StaticCtorOnlyType), []));
        Assert.Contains("instance constructor", ex.Message);
    }
}
