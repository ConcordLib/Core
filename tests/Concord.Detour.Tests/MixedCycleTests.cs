using System.Reflection;
using Concord.Emit;
using Xunit;

namespace Concord.Detour.Tests;

public sealed class MixedCycleTests {
    [Fact]
    public void ConcordBeforeForeignOwner_Throws_AndNamesBothOwners() {
        (long Seq, Injection Injection)[] live = [
            (0, ConcordSide("mod.concord", beforeOwners: ["mod.foreign"])),
            (1, ForeignSide("mod.foreign"))
        ];

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => InjectionOrderer.OrderForComposition(live));

        Assert.Equal("CONC052", error.Code);
        Assert.Contains("mod.concord", error.Message);
        Assert.Contains("mod.foreign", error.Message);
    }

    [Fact]
    public void ForeignAfterConcordOwner_Throws_AndNamesBothOwners() {
        (long Seq, Injection Injection)[] live = [
            (0, ForeignSide("mod.foreign", afterOwners: ["mod.concord"])),
            (1, ConcordSide("mod.concord"))
        ];

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => InjectionOrderer.OrderForComposition(live));

        Assert.Equal("CONC052", error.Code);
        Assert.Contains("mod.foreign", error.Message);
        Assert.Contains("mod.concord", error.Message);
    }

    [Fact]
    public void ConcordAfterForeignOwner_IsAlreadySatisfiedByLayering() {
        Injection concord = ConcordSide("mod.concord", afterOwners: ["mod.foreign"]);
        Injection foreign = ForeignSide("mod.foreign");
        (long Seq, Injection Injection)[] live = [(0, concord), (1, foreign)];

        Injection[] runtimeOrder = Enumerable.Reverse(InjectionOrderer.OrderForComposition(live)).ToArray();

        Assert.Equal([foreign, concord], runtimeOrder);
    }

    private static Injection ConcordSide(
        string owner,
        IReadOnlyList<string>? beforeOwners = null,
        IReadOnlyList<string>? afterOwners = null) =>
        Create(owner, false, beforeOwners, afterOwners);

    private static Injection ForeignSide(
        string owner,
        IReadOnlyList<string>? beforeOwners = null,
        IReadOnlyList<string>? afterOwners = null) =>
        Create(owner, true, beforeOwners, afterOwners);

    private static Injection Create(
        string owner,
        bool foreignOrigin,
        IReadOnlyList<string>? beforeOwners,
        IReadOnlyList<string>? afterOwners) {
        MethodInfo method = typeof(MixedCycleTests).GetMethod(nameof(Inject), BindingFlags.NonPublic | BindingFlags.Static)!;
        return new Injection(method, new InjectAt.Head(), owner, 0) {
            ForeignOrigin = foreignOrigin,
            BeforeOwners = beforeOwners ?? [],
            AfterOwners = afterOwners ?? []
        };
    }

    private static void Inject() { }
}
