using System.Reflection;
using Concord.Emit;
using Xunit;

namespace Concord.Detour.Tests;

public sealed class InjectionOrdererTests {
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
        Assert.Equal("Patch ordering cycle: A -> B -> A.", error.Message);
    }

    [Fact]
    public void OrderForComposition_SelfReference_IsCycle() {
        (long Seq, Injection Injection)[] live = [
            (0, Create("self", 0, afterOwners: ["self"]))
        ];

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(() => InjectionOrderer.OrderForComposition(live));

        Assert.Equal("CONC052", error.Code);
        Assert.Equal("Patch ordering cycle: self -> self.", error.Message);
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

    private static Injection Create(
        string owner,
        int priority,
        IReadOnlyList<string>? beforeOwners = null,
        IReadOnlyList<string>? afterOwners = null) {
        MethodInfo method = typeof(InjectionOrdererTests).GetMethod(nameof(Inject), BindingFlags.NonPublic | BindingFlags.Static)!;
        return new Injection(method, new InjectAt.Head(), owner, priority) {
            BeforeOwners = beforeOwners ?? [],
            AfterOwners = afterOwners ?? []
        };
    }

    private static void Inject() { }
}
