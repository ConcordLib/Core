using System.Reflection;
using Concord.Emit;
using Xunit;

namespace Concord.Detour.Tests;

public sealed class InjectionOrdererTests {
    public static TheoryData<string> AfterBodyPositions => new TheoryData<string> {
        "Return",
        "Tail",
        "Finally",
        "Catch",
        "InvokeTail",
        "NewObjTail"
    };

    public static TheoryData<string> ForeignFirstPositionsBesidesHead => new TheoryData<string> {
        "Around",
        "Local",
        "Constant",
        "Transpiler",
        "TranspilerFinal",
        "InvokeHead",
        "InvokeAround",
        "NewObjHead",
        "NewObjAround"
    };

    [Fact]
    public void OrderForComposition_Unconstrained_UsesPriorityThenApplySequence() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("early-low", 0)),
            (1, Create("late-low", 0)),
            (2, Create("high", 1))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["late-low", "early-low", "high"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Fact]
    public void OrderForComposition_BeforeConstraintOverridesPriority() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("before", 10, beforeOwners: ["after"])),
            (1, Create("after", 0))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["before", "after"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Fact]
    public void OrderForComposition_AfterConstraintOverridesPriority() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("after", 0, afterOwners: ["before"])),
            (1, Create("before", 10))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["before", "after"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Fact]
    public void OrderForComposition_MissingOwner_UsesFallbackOrder() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("first", 0, beforeOwners: ["missing"])),
            (1, Create("second", 0))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["second", "first"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Fact]
    public void OrderForComposition_Cycle_ReportsOwnerPath() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("A", 0, beforeOwners: ["B"])),
            (1, Create("B", 0, beforeOwners: ["A"]))
        ];

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => InjectionOrderer.OrderForComposition(live));

        Assert.Equal("CONC052", error.Code);
        Assert.Equal("CONC052: Patch ordering cycle: A -> B -> A.", error.Message);
    }

    [Fact]
    public void OrderForComposition_SelfReference_IsCycle() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("self", 0, afterOwners: ["self"]))
        ];

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => InjectionOrderer.OrderForComposition(live));

        Assert.Equal("CONC052", error.Code);
        Assert.Equal("CONC052: Patch ordering cycle: self -> self.", error.Message);
    }

    [Fact]
    public void OrderForComposition_DuplicateConstraint_OrdersBeforeEveryMatchingInjection() {
        Injection constrained = Create("A", 10, beforeOwners: ["B", "B"]);
        Injection firstMatch = Create("B", 0);
        Injection secondMatch = Create("B", 0);
        (long Seq, Injection Injection)[] live = [
            (0, constrained),
            (1, firstMatch),
            (2, secondMatch)
        ];

        Injection[] runtimeOrder = Enumerable.Reverse(InjectionOrderer.OrderForComposition(live)).ToArray();

        Assert.Same(constrained, runtimeOrder[0]);
        Assert.Equal([secondMatch, firstMatch], runtimeOrder.Skip(1));
    }

    [Fact]
    public void OrderForComposition_ForeignOrigin_AtHead_RunsBeforeEveryConcordInjection() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("concord-high", -100)),
            (1, Foreign("harmony-low", 100)),
            (2, Create("concord-low", 100)),
            (3, Foreign("harmony-high", -100))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["harmony-low", "harmony-high", "concord-high", "concord-low"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Theory]
    [MemberData(nameof(ForeignFirstPositionsBesidesHead))]
    public void OrderForComposition_ForeignOrigin_NotAfterTheBody_RunsBeforeEveryConcordInjection(string position) {
        InjectAt at = Position(position);
        (long Seq, Injection Injection)[] live = [
            (0, Create("concord-high", -100, at: at)),
            (1, Foreign("harmony-low", 100, at: at)),
            (2, Create("concord-low", 100, at: at)),
            (3, Foreign("harmony-high", -100, at: at))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["harmony-low", "harmony-high", "concord-high", "concord-low"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Theory]
    [MemberData(nameof(AfterBodyPositions))]
    public void OrderForComposition_ForeignOrigin_AfterTheBody_RunsAfterEveryConcordInjection(string position) {
        InjectAt at = Position(position);
        (long Seq, Injection Injection)[] live = [
            (0, Create("concord-high", -100, at: at)),
            (1, Foreign("harmony-low", 100, at: at)),
            (2, Create("concord-low", 100, at: at)),
            (3, Foreign("harmony-high", -100, at: at))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["concord-high", "concord-low", "harmony-low", "harmony-high"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Fact]
    public void OrderForComposition_ForeignOrigin_KeepsTheOrderItWasGivenIn() {
        (long Seq, Injection Injection)[] live = [
            (7, Foreign("first", 100)),
            (3, Foreign("second", -100))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["first", "second"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Theory]
    [MemberData(nameof(AfterBodyPositions))]
    public void OrderForComposition_ForeignOrigin_AfterTheBody_KeepsTheOrderItWasGivenIn(string position) {
        InjectAt at = Position(position);
        (long Seq, Injection Injection)[] live = [
            (7, Foreign("first", 100, at: at)),
            (3, Foreign("second", -100, at: at))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["first", "second"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Fact]
    public void OrderForComposition_HeadAndAfterBodyTogether_LayersEachPositionByItsOwnRule() {
        InjectAt head = Position("Head");
        InjectAt afterBody = Position("Return");
        (long Seq, Injection Injection)[] live = [
            (0, Create("concord-head", 0, at: head)),
            (1, Foreign("harmony-head", 0, at: head)),
            (2, Create("concord-return", 0, at: afterBody)),
            (3, Foreign("harmony-return", 0, at: afterBody))
        ];

        Injection[] runtimeOrder = Enumerable.Reverse(InjectionOrderer.OrderForComposition(live)).ToArray();

        Assert.Equal(
            ["harmony-head", "concord-head"],
            runtimeOrder.Where(static injection => injection.At is InjectAt.Head).Select(static injection => injection.Owner));
        Assert.Equal(
            ["concord-return", "harmony-return"],
            runtimeOrder.Where(static injection => injection.At is InjectAt.Return).Select(static injection => injection.Owner));
    }

    [Theory]
    [MemberData(nameof(AfterBodyPositions))]
    public void OrderForComposition_RunsAfterForeign_AfterTheBody_SortsAfterTheWholeForeignGroup(string position) {
        InjectAt at = Position(position);
        (long Seq, Injection Injection)[] live = [
            (0, Create("concord", 0, at: at)),
            (1, Foreign("harmony-first", 400, at: at)),
            (2, AfterForeign("terminal", -1000, at: at)),
            (3, Foreign("harmony-second", -400, at: at))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["concord", "harmony-first", "harmony-second", "terminal"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Fact]
    public void OrderForComposition_RunsAfterForeign_AtHead_SortsAfterTheWholeForeignGroup() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("concord", 0)),
            (1, Foreign("harmony-first", 400)),
            (2, AfterForeign("terminal", -1000)),
            (3, Foreign("harmony-second", -400))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["harmony-first", "harmony-second", "concord", "terminal"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Theory]
    [InlineData("Head")]
    [MemberData(nameof(AfterBodyPositions))]
    public void OrderForComposition_RunsAfterForeign_WithNoForeignInjection_StillSortsLast(string position) {
        InjectAt at = Position(position);
        (long Seq, Injection Injection)[] live = [
            (0, Create("concord-high", -100, at: at)),
            (1, AfterForeign("terminal", -1000, at: at)),
            (2, Create("concord-low", 100, at: at))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["concord-high", "concord-low", "terminal"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Fact]
    public void OrderForComposition_ForeignOwnerSharingAConcordOwnerName_AddsNoEdge() {
        Injection foreignShared = Foreign("shared", 0);
        Injection concordShared = Create("shared", 0);
        (long Seq, Injection Injection)[] live = [
            (0, Create("constrained", 0, beforeOwners: ["shared"])),
            (1, foreignShared),
            (2, concordShared)
        ];

        Injection[] runtimeOrder = Enumerable.Reverse(InjectionOrderer.OrderForComposition(live)).ToArray();

        Assert.Same(foreignShared, runtimeOrder[0]);
        Assert.Equal("constrained", runtimeOrder[1].Owner);
        Assert.Same(concordShared, runtimeOrder[2]);
    }

    [Theory]
    [MemberData(nameof(AfterBodyPositions))]
    public void OrderForComposition_AfterTheBody_ConcordAskingToRunAfterForeignOwner_IsACycle(string position) {
        InjectAt at = Position(position);
        (long Seq, Injection Injection)[] live = [
            (0, Create("concord", 0, afterOwners: ["harmony"], at: at)),
            (1, Foreign("harmony", 0, at: at))
        ];

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => InjectionOrderer.OrderForComposition(live));

        Assert.Equal("CONC052", error.Code);
        Assert.Equal(
            "CONC052: Patch ordering cycle: concord -> harmony -> concord. A foreign patch host always runs outside Concord.",
            error.Message);
    }

    [Theory]
    [MemberData(nameof(AfterBodyPositions))]
    public void OrderForComposition_AfterTheBody_ForeignAskingToRunBeforeConcordOwner_IsACycle(string position) {
        InjectAt at = Position(position);
        (long Seq, Injection Injection)[] live = [
            (0, Foreign("harmony", 0, beforeOwners: ["concord"], at: at)),
            (1, Create("concord", 0, at: at))
        ];

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => InjectionOrderer.OrderForComposition(live));

        Assert.Equal("CONC052", error.Code);
        Assert.Equal(
            "CONC052: Patch ordering cycle: harmony -> concord -> harmony. A foreign patch host always runs outside Concord.",
            error.Message);
    }

    [Theory]
    [MemberData(nameof(AfterBodyPositions))]
    public void OrderForComposition_AfterTheBody_CrossOriginConstraintTheLayeringAlreadyMeets_IsKept(string position) {
        InjectAt at = Position(position);
        (long Seq, Injection Injection)[] live = [
            (0, Foreign("harmony", 0, afterOwners: ["concord"], at: at)),
            (1, Create("concord", 0, beforeOwners: ["harmony"], at: at))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["concord", "harmony"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    [Fact]
    public void OrderForComposition_ConcordAskingToRunBeforeForeignOwner_IsACycle() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("concord", 0, beforeOwners: ["harmony"])),
            (1, Foreign("harmony", 0))
        ];

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => InjectionOrderer.OrderForComposition(live));

        Assert.Equal("CONC052", error.Code);
        Assert.Equal(
            "CONC052: Patch ordering cycle: concord -> harmony -> concord. A foreign patch host always runs outside Concord.",
            error.Message);
    }

    [Fact]
    public void OrderForComposition_ForeignAskingToRunAfterConcordOwner_IsACycle() {
        (long Seq, Injection Injection)[] live = [
            (0, Foreign("harmony", 0, afterOwners: ["concord"])),
            (1, Create("concord", 0))
        ];

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => InjectionOrderer.OrderForComposition(live));

        Assert.Equal("CONC052", error.Code);
        Assert.Equal(
            "CONC052: Patch ordering cycle: harmony -> concord -> harmony. A foreign patch host always runs outside Concord.",
            error.Message);
    }

    [Fact]
    public void OrderForComposition_CrossOriginConstraintTheLayeringAlreadyMeets_IsKept() {
        (long Seq, Injection Injection)[] live = [
            (0, Foreign("harmony", 0, beforeOwners: ["concord"])),
            (1, Create("concord", 0, afterOwners: ["harmony"]))
        ];

        Injection[] compositionOrder = InjectionOrderer.OrderForComposition(live);

        Assert.Equal(
            ["harmony", "concord"],
            Enumerable.Reverse(compositionOrder).Select(static injection => injection.Owner));
    }

    private static Injection Create(
        string owner,
        int priority,
        IReadOnlyList<string>? beforeOwners = null,
        IReadOnlyList<string>? afterOwners = null,
        InjectAt? at = null) {
        MethodInfo method = typeof(InjectionOrdererTests).GetMethod(nameof(Inject), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Injection stub method not found.");
        return new Injection(method, at ?? new InjectAt.Head(), owner, priority) {
            BeforeOwners = beforeOwners ?? [],
            AfterOwners = afterOwners ?? []
        };
    }

    private static Injection Foreign(
        string owner,
        int priority,
        IReadOnlyList<string>? beforeOwners = null,
        IReadOnlyList<string>? afterOwners = null,
        InjectAt? at = null) {
        Injection injection = Create(owner, priority, beforeOwners, afterOwners, at);
        injection.ForeignOrigin = true;
        return injection;
    }

    private static Injection AfterForeign(string owner, int priority, InjectAt? at = null) {
        Injection injection = Create(owner, priority, at: at);
        injection.RunsAfterForeign = true;
        return injection;
    }

    private static InjectAt Position(string name) {
        return name switch {
            "Head" => new InjectAt.Head(),
            "Return" => new InjectAt.Return(0),
            "Tail" => new InjectAt.Tail(),
            "Finally" => new InjectAt.Finally(),
            "Catch" => new InjectAt.Catch(),
            "Around" => new InjectAt.Around(),
            "Local" => new InjectAt.Local(typeof(int), LocalAccess.Store),
            "Constant" => new InjectAt.Constant(7),
            "Transpiler" => new InjectAt.Transpiler(),
            "TranspilerFinal" => new InjectAt.Transpiler(Final: true),
            "InvokeHead" => new InjectAt.Invoke(typeof(InjectionOrdererTests), nameof(Inject), At.Head),
            "InvokeTail" => new InjectAt.Invoke(typeof(InjectionOrdererTests), nameof(Inject), At.Tail),
            "InvokeAround" => new InjectAt.Invoke(typeof(InjectionOrdererTests), nameof(Inject), At.Around),
            "NewObjHead" => new InjectAt.NewObj(typeof(object), At.Head),
            "NewObjTail" => new InjectAt.NewObj(typeof(object), At.Tail),
            "NewObjAround" => new InjectAt.NewObj(typeof(object), At.Around),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown injection position.")
        };
    }

    private static void Inject() { }
}
