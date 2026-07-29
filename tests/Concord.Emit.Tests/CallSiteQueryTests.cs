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

        List<Instruction> selected = CallSiteQuery.Select(matches, 2, target, "QueryRules.Twice");

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
            () => CallSiteQuery.Select(matches, 9, target, "QueryRules.Twice"));

        Assert.Contains("CONC033", ex.ToString());
    }
}
