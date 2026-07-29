using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;

namespace Concord.Emit.Tests;

public static class QueryRules {
    public static int Twice(int value) => value * 2;
    public static int Thrice(int value) => value * 3;
}

public class QueryHost {
    public int Run(int seed) {
        int a = QueryRules.Twice(seed);
        int b = QueryRules.Thrice(a);
        int c = QueryRules.Twice(b);
        return c;
    }
}

public sealed class QueryBox {
    public QueryBox(int value) {
        Value = value;
    }

    public QueryBox(int value, int bonus) {
        Value = value + bonus;
    }

    public int Value { get; }

    public static QueryBox Make(int value) => new QueryBox(value);
}

public class NewObjQueryHost {
    public int Build(int seed) {
        QueryBox first = new QueryBox(seed);
        QueryBox second = new QueryBox(seed, 1);
        QueryBox third = QueryBox.Make(seed);
        return first.Value + second.Value + third.Value;
    }
}

public sealed class CallSiteQueryTests {
    private static List<Instruction> SpineOf(MethodBase target) {
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        return [.. source.Definition.Body.Instructions];
    }

    [Fact]
    public void Match_FindsEveryOccurrence() {
        MethodBase target = typeof(QueryHost).GetMethod(nameof(QueryHost.Run))!;
        List<Instruction> spine = SpineOf(target);

        List<Instruction> matches = CallSiteQuery.Match(
            spine, typeof(QueryRules), nameof(QueryRules.Twice), null, false, false);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void Select_ByOrdinal_PicksOne() {
        MethodBase target = typeof(QueryHost).GetMethod(nameof(QueryHost.Run))!;
        List<Instruction> spine = SpineOf(target);
        List<Instruction> matches = CallSiteQuery.Match(
            spine, typeof(QueryRules), nameof(QueryRules.Twice), null, false, false);

        List<Instruction> selected = CallSiteQuery.Select(matches, 2, target, "QueryRules.Twice", false);

        Assert.Single(selected);
        Assert.Same(matches[1], selected[0]);
    }

    [Fact]
    public void Select_OrdinalPastEnd_ThrowsConc033() {
        MethodBase target = typeof(QueryHost).GetMethod(nameof(QueryHost.Run))!;
        List<Instruction> spine = SpineOf(target);
        List<Instruction> matches = CallSiteQuery.Match(
            spine, typeof(QueryRules), nameof(QueryRules.Twice), null, false, false);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => CallSiteQuery.Select(matches, 9, target, "QueryRules.Twice", false));

        Assert.Contains("CONC033", ex.ToString());
    }

    [Fact]
    public void Match_NewObj_FindsEveryConstructionSite() {
        MethodBase target = typeof(NewObjQueryHost).GetMethod(nameof(NewObjQueryHost.Build))!;
        List<Instruction> spine = SpineOf(target);

        List<Instruction> matches = CallSiteQuery.Match(
            spine, typeof(QueryBox), ".ctor", null, false, true);

        Assert.Equal(2, matches.Count);
        Assert.All(matches, instruction => Assert.Equal(OpCodes.Newobj, instruction.OpCode));
    }

    [Fact]
    public void Match_NewObj_IgnoresOrdinaryCallsOnTheSameType() {
        MethodBase target = typeof(NewObjQueryHost).GetMethod(nameof(NewObjQueryHost.Build))!;
        List<Instruction> spine = SpineOf(target);

        List<Instruction> calls = CallSiteQuery.Match(
            spine, typeof(QueryBox), nameof(QueryBox.Make), null, false, false);
        List<Instruction> constructions = CallSiteQuery.Match(
            spine, typeof(QueryBox), ".ctor", null, false, true);

        Assert.Single(calls);
        Assert.DoesNotContain(calls[0], constructions);

        List<Instruction> ctorsAsCalls = CallSiteQuery.Match(
            spine, typeof(QueryBox), ".ctor", null, false, false);

        Assert.Empty(ctorsAsCalls);
    }

    [Fact]
    public void Match_NewObj_FiltersByConstructorParameterTypes() {
        MethodBase target = typeof(NewObjQueryHost).GetMethod(nameof(NewObjQueryHost.Build))!;
        List<Instruction> spine = SpineOf(target);

        List<Instruction> matches = CallSiteQuery.Match(
            spine, typeof(QueryBox), ".ctor", [typeof(int), typeof(int)], false, true);

        Assert.Single(matches);
        Assert.Equal(2, ((Mono.Cecil.MethodReference)matches[0].Operand).Parameters.Count);
    }
}
