using System.Reflection;
using System.Reflection.Emit;
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
