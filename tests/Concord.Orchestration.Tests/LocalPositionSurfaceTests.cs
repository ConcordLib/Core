using System.Reflection;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class LocalPositionSurfaceTests {
    // Both declarations below target the same store with By: 0, and both change the value, so a
    // miscounted occurrence applies one of them twice and the total moves. Identity bodies would
    // read as coverage while proving nothing.
    [Fact]
    public void ApplyAssembly_TwoLocalStoreInjectionsOnOneSlot_EachRunsExactlyOnce() {
        Assert.Equal("6", LocalSurfaceTarget.Run(5));

        IPatchHandle handle = Patcher.Apply(typeof(LocalPositionSurfaceTests).Assembly);
        try {
            Assert.Equal("17", LocalSurfaceTarget.Run(5));
        } finally {
            handle.Dispose();
        }

        Assert.Equal("6", LocalSurfaceTarget.Run(5));
    }

    [Fact]
    public void ScanType_InjectAtLocalWithSlice_AttachesRangeToLocal() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(LocalSliceDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        InjectAt.Local at = Assert.IsType<InjectAt.Local>(call.Injection.At);
        Assert.Equal(typeof(int), at.LocalType);
        Assert.Equal(LocalAccess.Store, at.Access);
        SliceRange range = Assert.IsType<SliceRange>(at.Slice);
        Assert.Equal(typeof(LocalSurfaceFrom), range.FromType);
        Assert.Equal(nameof(LocalSurfaceFrom.Start), range.FromMember);
        Assert.Equal(2u, range.FromBy);
        Assert.Equal(typeof(LocalSurfaceTo), range.ToType);
        Assert.Equal(nameof(LocalSurfaceTo.End), range.ToMember);
        Assert.Equal(3u, range.ToBy);
    }

    [Fact]
    public void ScanType_InjectAtLocalWithoutSlice_LeavesTheRangeUnset() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(LocalNoSliceDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        InjectAt.Local at = Assert.IsType<InjectAt.Local>(call.Injection.At);
        Assert.Null(at.Slice);
    }

    [Fact]
    public void Builder_SliceAfterLocal_IsAccepted() {
        MethodInfo injectionMethod = typeof(LocalSurfaceMethods).GetMethod(nameof(LocalSurfaceMethods.OnStore))!;
        SliceRange range = new SliceRange(typeof(LocalSurfaceFrom), nameof(LocalSurfaceFrom.Start), 1, typeof(LocalSurfaceTo), nameof(LocalSurfaceTo.End), 1);

        PatchBuilder builder = Patcher.For(typeof(LocalSurfaceTarget), nameof(LocalSurfaceTarget.Run))
            .Local(typeof(int), LocalAccess.Store, injectionMethod)
            .Slice(range);

        Assert.NotNull(builder);
    }

    [Fact]
    public void Builder_SliceAfterHead_StillThrows() {
        MethodInfo injectionMethod = typeof(LocalSurfaceMethods).GetMethod(nameof(LocalSurfaceMethods.OnStore))!;
        SliceRange range = new SliceRange(typeof(LocalSurfaceFrom), nameof(LocalSurfaceFrom.Start), 1, typeof(LocalSurfaceTo), nameof(LocalSurfaceTo.End), 1);

        PatchBuilder builder = Patcher.For(typeof(LocalSurfaceTarget), nameof(LocalSurfaceTarget.Run)).Head(injectionMethod);

        Assert.Throws<ConcordDeclarationException>(() => builder.Slice(range));
    }

    [Fact]
    public void ScanType_LocalConstructorWithAnotherPosition_Throws() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => PatchDeclarationScanner.ScanType(typeof(LocalAtWrongPositionDeclaration), patches, props));

        Assert.Contains("Local injections require At.Local", ex.Message);
    }

    [Fact]
    public void ScanType_AtLocalWithoutTheLocalConstructor_Throws() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => PatchDeclarationScanner.ScanType(typeof(BareAtLocalDeclaration), patches, props));

        Assert.Contains("without its dedicated constructor form", ex.Message);
    }

    // PatchBuilder takes Ordinal, Index and Name as runtime values, so CONCORD036 cannot see them.
    // CONC145 is the only gate on this route.
    [Fact]
    public void Builder_LocalWithTwoSelectors_IsRejected() {
        MethodInfo injectionMethod = typeof(LocalSurfaceMethods).GetMethod(nameof(LocalSurfaceMethods.Bump))!;

        IPatchHandle? applied = null;

        // Disposed in a finally: LocalSurfaceTarget is a shared static, so a regression that lets the
        // apply through would leave it patched and fail the sibling tests as well.
        try {
            ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
                () => {
                    applied = Patcher.For(typeof(LocalSurfaceTarget), nameof(LocalSurfaceTarget.Run))
                        .Local(typeof(int), LocalAccess.Store, injectionMethod, ordinal: 1, index: 0)
                        .Apply();
                });

            Assert.Equal("CONC145", ex.Code);
            Assert.Contains("Ordinal and Index", ex.Message);
        } finally {
            applied?.Dispose();
        }
    }

    [Fact]
    public void Builder_LocalWithThreeSelectors_NamesEveryOne() {
        MethodInfo injectionMethod = typeof(LocalSurfaceMethods).GetMethod(nameof(LocalSurfaceMethods.Bump))!;

        IPatchHandle? applied = null;

        try {
            ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
                () => {
                    applied = Patcher.For(typeof(LocalSurfaceTarget), nameof(LocalSurfaceTarget.Run))
                        .Local(typeof(int), LocalAccess.Store, injectionMethod, ordinal: 1, index: 0, name: "total")
                        .Apply();
                });

            Assert.Equal("CONC145", ex.Code);
            Assert.Contains("Ordinal, Index and Name", ex.Message);
        } finally {
            applied?.Dispose();
        }
    }

    [Fact]
    public void Builder_LocalWithOneSelector_IsAccepted() {
        MethodInfo injectionMethod = typeof(LocalSurfaceMethods).GetMethod(nameof(LocalSurfaceMethods.Bump))!;

        IPatchHandle handle = Patcher.For(typeof(LocalSurfaceTarget), nameof(LocalSurfaceTarget.Run))
            .Local(typeof(int), LocalAccess.Store, injectionMethod, ordinal: 1)
            .Apply();
        try {
            Assert.Equal("106", LocalSurfaceTarget.Run(5));
        } finally {
            handle.Dispose();
        }

        Assert.Equal("6", LocalSurfaceTarget.Run(5));
    }

    [Fact]
    public void UseLog_RoutesPatchReportsToTheHost() {
        Action<string>? previous = Concord.Detour.PatchLog.Sink;
        List<string> reported = new List<string>();

        try {
            Action<string> sink = reported.Add;
            Patcher.UseLog(sink);
            Assert.Same(sink, Concord.Detour.PatchLog.Sink);

            Patcher.UseLog(null);
            Assert.Null(Concord.Detour.PatchLog.Sink);
        } finally {
            Concord.Detour.PatchLog.Sink = previous;
        }
    }

    [Fact]
    public void UseLocalNameResolver_ReachesTheSymbolLookup() {
        Func<Module, string?> resolver = _ => null;

        try {
            Patcher.UseLocalNameResolver(resolver);
            Assert.Same(resolver, LocalNames.PathResolver);
        } finally {
            Patcher.UseLocalNameResolver(null);
        }

        Assert.Null(LocalNames.PathResolver);
    }

    public static class LocalSurfaceFrom {
        public static void Start() { }
    }

    public static class LocalSurfaceTo {
        public static void End() { }
    }

    // An assembly-wide Patcher.Apply composes every [Patch] below for real, so the target has to
    // carry enough anchors for the fixture slice and exactly one int local for it to select. The
    // return type is string so a Debug-only return temp is not a second int candidate.
    public static class LocalSurfaceTarget {
        public static string Run(int seed) {
            LocalSurfaceFrom.Start();
            LocalSurfaceTo.End();
            LocalSurfaceFrom.Start();
            int total = seed + 1;
            LocalSurfaceTo.End();
            LocalSurfaceTo.End();
            return total.ToString();
        }
    }

    public static class LocalSurfaceMethods {
        public static void OnStore() { }

        public static int Bump(int total) {
            return total + 100;
        }
    }

    [Patch(typeof(LocalSurfaceTarget))]
    private static class LocalSliceDeclaration {
        [Inject(nameof(LocalSurfaceTarget.Run), typeof(int), LocalAccess.Store, At.Local)]
        [Slice(typeof(LocalSurfaceFrom), nameof(LocalSurfaceFrom.Start), 2, typeof(LocalSurfaceTo), nameof(LocalSurfaceTo.End), 3)]
        public static int OnStore(int total) {
            return total + 1;
        }
    }

    [Patch(typeof(LocalSurfaceTarget))]
    private static class LocalNoSliceDeclaration {
        [Inject(nameof(LocalSurfaceTarget.Run), typeof(int), LocalAccess.Store, At.Local)]
        public static int OnStore(int total) {
            return total + 10;
        }
    }

    [Patch(typeof(LocalSurfaceTarget))]
    private static class LocalAtWrongPositionDeclaration {
        [Inject(nameof(LocalSurfaceTarget.Run), typeof(int), LocalAccess.Store, At.Tail)]
        public static void OnStore(ControlHandle ch) { }
    }

    [Patch(typeof(LocalSurfaceTarget))]
    private static class BareAtLocalDeclaration {
        [Inject(At.Local, nameof(LocalSurfaceTarget.Run))]
        public static void OnStore(ControlHandle ch) { }
    }
}
