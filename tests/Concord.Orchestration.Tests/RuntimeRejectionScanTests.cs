using System.Collections.Generic;
using System.Reflection;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class RejectingApplier : IPatchApplier {
    public void ApplyPatch(MethodBase target, Injection injection) {
        throw new ConcordEmitException("CONC144", "runtime rejected the body");
    }
}

public sealed class RuntimeRejectionScanTests {
    [Fact]
    public void ScanType_RuntimeRejectsBody_ThrowsDeclarationException() {
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => PatchDeclarationScanner.ScanType(typeof(GoodDeclaration), new RejectingApplier(), props));

        Assert.Contains("CONC144", ex.Message);
    }

    [Fact]
    public void ScanDeclarations_RuntimeRejectsBody_ContinuesToNextDeclaration() {
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();
        List<string> log = [];

        PatchDeclarationScanner.ScanDeclarations([typeof(GoodDeclaration)], new RejectingApplier(), props, log.Add);

        Assert.Single(log);
        Assert.Contains("CONC144", log[0]);
        Assert.Contains(typeof(GoodDeclaration).FullName!, log[0]);
    }
}
