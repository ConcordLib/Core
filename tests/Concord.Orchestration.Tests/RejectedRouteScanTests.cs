using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

[Patch]
public abstract class RoutableDeclaration : OtherBase {
    [Inject(At.Head, nameof(Run))]
    public void OnRun(ControlHandle ch) { }
}

public sealed class CodeThrowingApplier : IPatchApplier {
    private readonly string code;

    public CodeThrowingApplier(string code) {
        this.code = code;
    }

    public List<MethodBase> Applied { get; } = [];

    public void ApplyPatch(MethodBase target, Injection injection) {
        if (target.DeclaringType == typeof(GameBase)) {
            throw new ConcordEmitException(code, "another patcher owns this method");
        }

        Applied.Add(target);
    }
}

public sealed class RejectedRouteScanTests {
    [Fact]
    public void ScanType_RouteRefused_ThrowsDeclarationException() {
        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => PatchDeclarationScanner.ScanType(
                typeof(GoodDeclaration),
                new CodeThrowingApplier(RoutingDetourBackend.RejectedRouteCode),
                new FakeAttachedPropertyRegistry()));

        Assert.Contains(RoutingDetourBackend.RejectedRouteCode, ex.Message);
    }

    [Theory]
    [InlineData(RoutingDetourBackend.RejectedRouteCode)]
    [InlineData("CONC061")]
    public void ScanDeclarations_TargetRefused_StillAppliesTheOtherDeclarations(string code) {
        CodeThrowingApplier applier = new CodeThrowingApplier(code);

        PatchDeclarationScanner.ScanDeclarations(
            [typeof(GoodDeclaration), typeof(RoutableDeclaration)],
            applier,
            new FakeAttachedPropertyRegistry());

        Assert.Single(applier.Applied);
        Assert.Equal(nameof(OtherBase.Run), applier.Applied[0].Name);
    }
}
