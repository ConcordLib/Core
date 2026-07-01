using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

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
}
