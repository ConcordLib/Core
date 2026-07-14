using System.Reflection;
using System.Reflection.Emit;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class PatchDeclarationScannerTests {
    [Fact]
    public void ScanType_InjectMethod_CallsPatchApplierWithBaseMethod() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(GoodDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal(typeof(GameBase).GetMethod(nameof(GameBase.Step)), call.Target);
        Assert.Equal(typeof(GoodDeclaration).GetMethod(nameof(GoodDeclaration.OnStep)), call.Injection.InjectionMethod);
        Assert.IsType<InjectAt.Head>(call.Injection.At);
        Assert.Equal(typeof(GoodDeclaration).FullName, call.Injection.Owner);
    }

    [Fact]
    public void ScanType_InjectTargetsPropertyName_ResolvesToGetter() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(PropertyTargetDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal(typeof(GameBase).GetProperty(nameof(GameBase.Score))!.GetGetMethod(), call.Target);
    }

    [Fact]
    public void ScanType_DeclaredField_CallsPropertyRegistry() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(GoodDeclaration), patches, props);

        PropCall call = Assert.Single(props.Calls);
        Assert.Equal(typeof(GameBase), call.BaseType);
        Assert.Equal("counter", call.Name);
        Assert.Equal(typeof(int), call.ValueType);
    }

    [Fact]
    public void ScanType_FieldOnlyAttributed_RegistersPropertyNoPatches() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(FieldOnlyDeclaration), patches, props);

        Assert.Empty(patches.Calls);
        PropCall call = Assert.Single(props.Calls);
        Assert.Equal(typeof(GameBase), call.BaseType);
        Assert.Equal("marker", call.Name);
    }

    [Fact]
    public void ScanType_InjectField_IsNotAttachedProperty() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(InjectFieldDeclaration), patches, props);

        Assert.Empty(patches.Calls);
        PropCall call = Assert.Single(props.Calls);
        Assert.Equal(typeof(GameBase), call.BaseType);
        Assert.Equal("attachedCounter", call.Name);
    }

    [Fact]
    public void ScanType_StaticField_IsNotAttachedProperty() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(StaticFieldDeclaration), patches, props);

        Assert.Empty(patches.Calls);
        PropCall call = Assert.Single(props.Calls);
        Assert.Equal(typeof(GameBase), call.BaseType);
        Assert.Equal("marker", call.Name);
    }

    [Fact]
    public void ScanType_Unattributed_Skipped() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(UnattributedDeclaration), patches, props);

        Assert.Empty(patches.Calls);
        Assert.Empty(props.Calls);
    }

    [Fact]
    public void ScanType_PatchWithExplicitSealedTarget_AppliesAgainstTarget() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(SealedTargetDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal(typeof(SealedGame).GetMethod(nameof(SealedGame.Value)), call.Target);
    }

    [Fact]
    public void ScanType_PatchTargetMismatchesBase_Throws() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        Assert.Throws<ConcordDeclarationException>(() => PatchDeclarationScanner.ScanType(typeof(MismatchDeclaration), patches, props));
    }

    [Fact]
    public void ScanType_BarePatchNoGameBase_Throws() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        Assert.Throws<ConcordDeclarationException>(() => PatchDeclarationScanner.ScanType(typeof(NoGameBaseDeclaration), patches, props));
    }

    [Fact]
    public void ScanAssembly_ScansAttributedDeclarations() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanAssembly(typeof(GoodDeclaration).Assembly, patches, props);

        Assert.Contains(patches.Calls, c => c.Injection.Owner == typeof(GoodDeclaration).FullName);
        Assert.Contains(props.Calls, c => c.BaseType == typeof(GameBase) && c.Name == "counter");
    }

    [Fact]
    public void ScanType_SecondInjectionFails_FirstIsNotApplied() {
        RecordingApplier applier = new RecordingApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        Assert.Throws<ConcordDeclarationException>(() => PatchDeclarationScanner.ScanType(typeof(HalfFailingDeclaration), applier, props));

        Assert.Empty(applier.AppliedHandles);
    }

    [Fact]
    public void ScanType_PatchDebug_MarksInjectionForIlDump() {
        Type declaration = CreateDebugDeclaration();
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(declaration, patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.True(call.Injection.Debug);
    }

    [Fact]
    public void ScanType_PatchBeforeType_StampsInjection() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(BeforeDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal([typeof(GoodDeclaration).FullName!], call.Injection.BeforeOwners);
    }

    [Fact]
    public void ScanType_OrderAttributes_StampDeduplicatedOwners() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(OrderedDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal([typeof(GoodDeclaration).FullName!, "Optional.Mod.Before"], call.Injection.BeforeOwners);
        Assert.Equal([typeof(FieldOnlyDeclaration).FullName!, "Optional.Mod.After"], call.Injection.AfterOwners);
    }

    [Fact]
    public void ScanType_WhitespaceOrderOwner_ThrowsBeforeApplying() {
        RecordingApplier patches = new RecordingApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        Assert.Throws<ConcordDeclarationException>(() =>
            PatchDeclarationScanner.ScanType(typeof(InvalidOrderDeclaration), patches, props));

        Assert.Empty(patches.AppliedHandles);
    }

    [Fact]
    public void ScanType_EmptyAfterOwner_ThrowsBeforeApplying() {
        RecordingApplier patches = new RecordingApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        Assert.Throws<ConcordDeclarationException>(() =>
            PatchDeclarationScanner.ScanType(typeof(InvalidAfterDeclaration), patches, props));

        Assert.Empty(patches.AppliedHandles);
    }

    private static Type CreateDebugDeclaration() {
        AssemblyName assemblyName = new AssemblyName("ConcordPatchDebugTest_" + Guid.NewGuid().ToString("N"));
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder type = module.DefineType(
            "DebugDeclaration",
            TypeAttributes.Public | TypeAttributes.Abstract,
            typeof(GameBase));

        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(PatchAttribute).GetConstructor(Type.EmptyTypes)!,
            []));
        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(PatchDebugAttribute).GetConstructor(Type.EmptyTypes)!,
            []));

        MethodBuilder method = type.DefineMethod(
            "OnStep",
            MethodAttributes.Public,
            typeof(void),
            [typeof(ControlHandle)]);
        method.GetILGenerator().Emit(OpCodes.Ret);

        ConstructorInfo injectConstructor = typeof(InjectAttribute).GetConstructor(
            [typeof(At), typeof(string), typeof(uint), typeof(Type[])])!;
        method.SetCustomAttribute(new CustomAttributeBuilder(
            injectConstructor,
            [At.Head, nameof(GameBase.Step), 0u, null]));

        return type.CreateType()!;
    }
}
