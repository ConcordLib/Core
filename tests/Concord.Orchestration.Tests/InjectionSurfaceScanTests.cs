using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class InjectionSurfaceScanTests {
    [Fact]
    public void ScanType_InjectNewMethod_RecordsNewObjInjection() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(InjectNewDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal(typeof(InjectionSurfaceTarget).GetMethod(nameof(InjectionSurfaceTarget.Run)), call.Target);
        InjectAt.NewObj at = Assert.IsType<InjectAt.NewObj>(call.Injection.At);
        Assert.Equal(typeof(InjectionSurfaceConstructed), at.ConstructedType);
        Assert.Equal(At.Tail, at.Shift);
    }

    [Fact]
    public void ScanType_InjectWithSlice_AttachesRangeToInvoke() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(InvokeSliceDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal(typeof(InjectionSurfaceTarget).GetMethod(nameof(InjectionSurfaceTarget.Run)), call.Target);
        InjectAt.Invoke at = Assert.IsType<InjectAt.Invoke>(call.Injection.At);
        SliceRange range = Assert.IsType<SliceRange>(at.Slice);
        Assert.Equal(typeof(InjectionSurfaceFrom), range.FromType);
        Assert.Equal(nameof(InjectionSurfaceFrom.Start), range.FromMember);
        Assert.Equal(2u, range.FromBy);
        Assert.Equal(typeof(InjectionSurfaceTo), range.ToType);
        Assert.Equal(nameof(InjectionSurfaceTo.End), range.ToMember);
        Assert.Equal(3u, range.ToBy);
    }

    [Fact]
    public void ScanType_WholeMethodInjectionWithSlice_ScansWithoutError() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(CreateWholeMethodSliceDeclaration(), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal(typeof(InjectionSurfaceTarget).GetMethod(nameof(InjectionSurfaceTarget.Run)), call.Target);
        Assert.IsType<InjectAt.Head>(call.Injection.At);
    }

    [Fact]
    public void ScanType_InjectNewWithSlice_AttachesRangeToNewObj() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(InjectNewSliceDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal(typeof(InjectionSurfaceTarget).GetMethod(nameof(InjectionSurfaceTarget.Run)), call.Target);
        InjectAt.NewObj at = Assert.IsType<InjectAt.NewObj>(call.Injection.At);
        SliceRange range = Assert.IsType<SliceRange>(at.Slice);
        Assert.Equal(typeof(InjectionSurfaceFrom), range.FromType);
        Assert.Equal(nameof(InjectionSurfaceFrom.Start), range.FromMember);
        Assert.Equal(2u, range.FromBy);
        Assert.Equal(typeof(InjectionSurfaceTo), range.ToType);
        Assert.Equal(nameof(InjectionSurfaceTo.End), range.ToMember);
        Assert.Equal(3u, range.ToBy);
    }

    // The other tests here stop at the scanner and assert on a FakePatchApplier, and the emit-side
    // suites build their Injections by hand. This is the only case that carries an [InjectNew] +
    // [Slice] + [Capture] declaration the whole way a mod author does: attributes, scan, compose,
    // live detour, call, revert.
    [Fact]
    public void ApplyAssembly_InjectNewWithSliceAndCapture_RunsAgainstTheLiveTarget() {
        SurfaceRunRecorder.Reset();
        Assert.Equal(11, SurfaceRunTarget.Run(5));
        Assert.Equal(0, SurfaceRunRecorder.Hits);

        IPatchHandle handle = Patcher.Apply(typeof(SurfaceRunDeclaration).Assembly);
        try {
            SurfaceRunRecorder.Reset();
            Assert.Equal(11, SurfaceRunTarget.Run(5));
            Assert.Equal(1, SurfaceRunRecorder.Hits);
            Assert.Equal("inside", SurfaceRunRecorder.SeenTag);
        } finally {
            handle.Dispose();
        }

        SurfaceRunRecorder.Reset();
        Assert.Equal(11, SurfaceRunTarget.Run(5));
        Assert.Equal(0, SurfaceRunRecorder.Hits);
    }

    [Fact]
    public void ScanType_InjectAndInjectNewOnSameMethod_Throws() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        Assert.Throws<ConcordDeclarationException>(() =>
            PatchDeclarationScanner.ScanType(typeof(BothInjectionAttributesDeclaration), patches, props));
    }

    public sealed class InjectionSurfaceConstructed { }

    public static class InjectionSurfaceHelper {
        public static void Call() { }
    }

    public static class InjectionSurfaceFrom {
        public static void Start() { }
    }

    public static class InjectionSurfaceTo {
        public static void End() { }
    }

    public static class InjectionSurfaceTarget {
        public static void Run() {
            InjectionSurfaceFrom.Start();
            InjectionSurfaceFrom.Start();
            InjectionSurfaceHelper.Call();
            InjectionSurfaceConstructed constructed = new InjectionSurfaceConstructed();
            InjectionSurfaceHelper.Call();
            InjectionSurfaceTo.End();
            InjectionSurfaceTo.End();
            InjectionSurfaceTo.End();
            GC.KeepAlive(constructed);
        }
    }

    public sealed class SurfaceOrder {
        public SurfaceOrder(int id, string tag) {
            Id = id;
            Tag = tag;
        }

        public int Id { get; }

        public string Tag { get; }
    }

    public static class SurfaceRunTarget {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(int seed) {
            SurfaceOrder outside = new SurfaceOrder(seed, "outside");
            InjectionSurfaceFrom.Start();
            SurfaceOrder inside = new SurfaceOrder(seed + 1, "inside");
            InjectionSurfaceTo.End();
            return outside.Id + inside.Id;
        }
    }

    public static class SurfaceRunRecorder {
        public static int Hits;

        public static string SeenTag = string.Empty;

        public static void Reset() {
            Hits = 0;
            SeenTag = string.Empty;
        }
    }

    // Every [Patch] type here is live for the whole assembly, private and nested included:
    // ScanAssembly walks assembly.GetTypes(), so the five other tests that call Patcher.Apply on
    // this assembly apply this declaration too. Keep any declaration added here composable, or it
    // throws out of their Patcher.Apply rather than out of the test that owns it.
    [Patch(typeof(SurfaceRunTarget))]
    private static class SurfaceRunDeclaration {
        [InjectNew(nameof(SurfaceRunTarget.Run), typeof(SurfaceOrder), At.Tail, 1)]
        [Slice(typeof(InjectionSurfaceFrom), nameof(InjectionSurfaceFrom.Start), 1, typeof(InjectionSurfaceTo), nameof(InjectionSurfaceTo.End), 1)]
        public static void OnConstruct([Capture(2)] string tag) {
            SurfaceRunRecorder.Hits++;
            SurfaceRunRecorder.SeenTag = tag;
        }
    }

    [Patch(typeof(InjectionSurfaceTarget))]
    private static class InjectNewDeclaration {
        [InjectNew(nameof(InjectionSurfaceTarget.Run), typeof(InjectionSurfaceConstructed), At.Tail)]
        public static void OnNew(ControlHandle ch) { }
    }

    [Patch(typeof(InjectionSurfaceTarget))]
    private static class InvokeSliceDeclaration {
        [Inject(nameof(InjectionSurfaceTarget.Run), typeof(InjectionSurfaceHelper), nameof(InjectionSurfaceHelper.Call), At.Head)]
        [Slice(typeof(InjectionSurfaceFrom), nameof(InjectionSurfaceFrom.Start), 2, typeof(InjectionSurfaceTo), nameof(InjectionSurfaceTo.End), 3)]
        public static void OnCall(ControlHandle ch) { }
    }

    [Patch(typeof(InjectionSurfaceTarget))]
    private static class InjectNewSliceDeclaration {
        [InjectNew(nameof(InjectionSurfaceTarget.Run), typeof(InjectionSurfaceConstructed), At.Tail)]
        [Slice(typeof(InjectionSurfaceFrom), nameof(InjectionSurfaceFrom.Start), 2, typeof(InjectionSurfaceTo), nameof(InjectionSurfaceTo.End), 3)]
        public static void OnNew(ControlHandle ch) { }
    }

    private static Type CreateWholeMethodSliceDeclaration() {
        AssemblyName assemblyName = new AssemblyName("WholeMethodSliceDeclaration_" + Guid.NewGuid().ToString("N"));
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder type = module.DefineType(
            "WholeMethodSliceDeclaration",
            TypeAttributes.Public | TypeAttributes.Abstract);

        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(PatchAttribute).GetConstructor([typeof(Type)])!,
            [typeof(InjectionSurfaceTarget)]));

        MethodBuilder method = type.DefineMethod(
            "OnRun",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(ControlHandle)]);
        method.GetILGenerator().Emit(OpCodes.Ret);

        ConstructorInfo injectConstructor = typeof(InjectAttribute).GetConstructor(
            [typeof(At), typeof(string), typeof(uint), typeof(Type[])])!;
        method.SetCustomAttribute(new CustomAttributeBuilder(
            injectConstructor,
            [At.Head, nameof(InjectionSurfaceTarget.Run), 0u, null]));

        ConstructorInfo sliceConstructor = typeof(SliceAttribute).GetConstructors().Single();
        method.SetCustomAttribute(new CustomAttributeBuilder(
            sliceConstructor,
            [typeof(InjectionSurfaceFrom), nameof(InjectionSurfaceFrom.Start), 1u, null, null, 1u]));

        return type.CreateType()!;
    }

    [Patch(typeof(InjectionSurfaceTarget))]
    private static class BothInjectionAttributesDeclaration {
        [Inject(At.Head, nameof(InjectionSurfaceTarget.Run))]
        [InjectNew(nameof(InjectionSurfaceTarget.Run), typeof(InjectionSurfaceConstructed), At.Head)]
        public static void OnRun(ControlHandle ch) { }
    }
}
