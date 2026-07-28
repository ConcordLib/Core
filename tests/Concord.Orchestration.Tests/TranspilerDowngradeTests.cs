using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using Concord.Orchestration.Tests.RollbackAssembly;
using Xunit;

namespace Concord.Orchestration.Tests;

public class DowngradeGame {
    public virtual int Step(int n) {
        return n + 1;
    }
}

[Patch(typeof(DowngradeGame))]
public abstract class ThrowingTranspilerDeclaration : DowngradeGame {
    [Inject(At.Transpiler, nameof(DowngradeGame.Step))]
    public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions) {
        throw new InvalidOperationException("author blew up");
    }
}

public class DowngradeGoodTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Step(int n) {
        return n + 1;
    }
}

[Patch(typeof(DowngradeGoodTarget))]
public abstract class DowngradeGoodDeclaration : DowngradeGoodTarget {
    [Inject(At.Tail, nameof(DowngradeGoodTarget.Step))]
    public void AddOneHundred(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 100;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class TranspilerDowngradeTests {
    [Fact]
    public void ScanType_TranspilerFailure_ThrowsDeclarationExceptionNotEmitException() {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => PatchDeclarationScanner.ScanType(typeof(ThrowingTranspilerDeclaration), applier, props));

        Assert.Contains("CONC117", ex.Message);
        Assert.Contains(nameof(ThrowingTranspilerDeclaration), ex.Message);
        Assert.Empty(applier.Handles);
    }

    [Fact]
    public void ScanDeclarations_TranspilerFailure_GoodDeclarationStillApplies() {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        try {
            PatchDeclarationScanner.ScanDeclarations(
                [typeof(DowngradeGoodDeclaration), typeof(ThrowingTranspilerDeclaration)],
                applier,
                props);

            IDetourHandle handle = Assert.Single(applier.Handles);
            Assert.Equal(typeof(DowngradeGoodTarget).GetMethod(nameof(DowngradeGoodTarget.Step)), handle.Original);
            Assert.Equal(106, new DowngradeGoodTarget().Step(5));
        } finally {
            foreach (IDetourHandle handle in applier.Handles) {
                handle.Dispose();
            }
        }
    }

    [Fact]
    public void ScanType_NonTranspilerEmitFailure_EscapesAsConcordEmitException() {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => PatchDeclarationScanner.ScanType(typeof(RollbackBadDeclaration), applier, props));

        Assert.Equal("CONC012", ex.Code);
        Assert.Empty(applier.Handles);
    }
}
