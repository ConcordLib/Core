using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class LocalPositionAnchorFrom {
    public static void Start() { }
}

public static class LocalPositionAnchorTo {
    public static void End() { }
}

// A by-value formatter, for the fixtures that need a real ldloc rather than the ldloca that
// local.ToString() emits.
public static class LocalPositionFormat {
    public static string Of(float value) {
        return value.ToString();
    }

    public static string Of(int value) {
        return value.ToString();
    }
}

public class LocalPositionHost {
    // The return type is string so a Debug-only return temp is never a float, and each local is
    // address-taken by ToString() so Release cannot schedule it onto the stack.
    public string Points(float seed) {
        float points = seed + 1f;
        points = points * 2f;
        return points.ToString();
    }
}

public class LocalPositionReaderHost {
    // Three reads of one slot, so a Load By can pick a middle one and the read after it proves the
    // slot itself was never written.
    public string Thrice(float seed) {
        float points = seed + 1f;
        return LocalPositionFormat.Of(points) + "|" + LocalPositionFormat.Of(points) + "|" + LocalPositionFormat.Of(points);
    }
}

// Two returns inside a try, so Roslyn merges them onto one exit slot read once at the bottom. That
// slot is the only float local in both Debug and Release, and it is what CONC159 refuses.
public class LocalPositionResultHost {
    public float Split(float seed) {
        try {
            if (seed > 3f) {
                return seed + 1f;
            }

            return seed * 2f;
        } finally {
            LocalPositionAnchorTo.End();
        }
    }

    // One return, so the exit slot is read once and normalization has nothing to clone into.
    public float Once(float seed) {
        try {
            return seed + 1f;
        } finally {
            LocalPositionAnchorTo.End();
        }
    }
}

public struct LocalPositionBox {
    public int Value;
}

// 'box = default' writes the slot through its address with initobj and never stores to it, so the
// one store the body does have is not the whole story.
public class LocalPositionIndirectHost {
    public string Reset(LocalPositionBox seed) {
        LocalPositionBox box = seed;
        if (box.Value > 3) {
            box = default;
        }

        return box.Value.ToString();
    }
}

public class LocalPositionSiblingHost {
    public string Compute(float seed) {
        int bonus = 7;
        float points = seed + 1f;
        points = points * 2f;
        return points.ToString() + "|" + bonus.ToString();
    }
}

public class LocalPositionSlicedHost {
    public string Work(float seed) {
        float points = seed + 1f;
        LocalPositionAnchorFrom.Start();
        float doubled = points * 2f;
        LocalPositionAnchorTo.End();
        return points.ToString() + "|" + doubled.ToString();
    }
}

public static class LocalPositionMethods {
    public static float Adjust(float value) {
        return value + 100f;
    }

    public static float AddThousand(float value) {
        return value + 1000f;
    }

    public static float Double(float value) {
        return value * 2f;
    }

    public static float Blend(float value, [Local] int bonus) {
        return value + bonus;
    }

    public static float BlendReversed([Local] int bonus, float value) {
        return value + bonus;
    }

    public static LocalPositionBox Bump(LocalPositionBox box) {
        box.Value += 1;
        return box;
    }

    public static void Bound([Local] int total) {
        _ = total;
    }
}

public sealed class LocalPositionTests {
    [Fact]
    public void LocalPosition_RoundTripsThroughTheAttribute() {
        InjectAttribute declared = new InjectAttribute(
            "Work", typeof(int), LocalAccess.Store, At.Local, by: 2);

        InjectAt resolved = declared.ResolvedAt;

        InjectAt.Local local = Assert.IsType<InjectAt.Local>(resolved);
        Assert.Equal(typeof(int), local.LocalType);
        Assert.Equal(LocalAccess.Store, local.Access);
        Assert.Equal(2u, local.By);
    }

    [Fact]
    public void LocalPosition_CarriesTheSelectorsThroughTheAttribute() {
        InjectAttribute declared = new InjectAttribute(
            "Work", typeof(string), LocalAccess.Load, At.Local, by: 1, ordinal: 3, index: 7);

        InjectAt.Local local = Assert.IsType<InjectAt.Local>(declared.ResolvedAt);

        Assert.Equal(LocalAccess.Load, local.Access);
        Assert.Equal(3u, local.Ordinal);
        Assert.Equal(7, local.Index);
        Assert.Null(local.Slice);
    }

    [Fact]
    public void LocalPosition_DefaultsTheUnsetSelectors() {
        InjectAttribute declared = new InjectAttribute("Work", typeof(int), LocalAccess.Store, At.Local);

        InjectAt.Local local = Assert.IsType<InjectAt.Local>(declared.ResolvedAt);

        Assert.Equal(0u, local.By);
        Assert.Equal(0u, local.Ordinal);
        Assert.Equal(-1, local.Index);
    }

    [Fact]
    public void LocalPosition_KeepsTheSliceOnAWithExpression() {
        SliceRange range = new SliceRange(typeof(LocalPositionAnchorFrom), nameof(LocalPositionAnchorFrom.Start), 1, typeof(LocalPositionAnchorTo), nameof(LocalPositionAnchorTo.End), 1);
        InjectAt.Local local = new InjectAt.Local(typeof(float), LocalAccess.Store);

        InjectAt.Local sliced = local with { Slice = range };

        Assert.Equal(range, sliced.Slice);
    }

    // seed 5 gives store 1 the value 6 and store 2 the value 12. Adjust adds 100, so each By picks
    // out a different result and none of them can be reached by targeting the wrong store.
    [Theory]
    [InlineData(1u, "212")]
    [InlineData(2u, "112")]
    [InlineData(0u, "312")]
    public void Store_ReplacesTheValueAtTheSelectedWrite(uint by, string expected) {
        string result = RunPoints(new InjectAt.Local(typeof(float), LocalAccess.Store, by), nameof(LocalPositionMethods.Adjust));

        Assert.Equal(expected, result);
    }

    // The case CONC039's one-parameter rule used to forbid: replace the matched local's value while
    // reading a sibling local in the same signature.
    [Fact]
    public void Store_ReadsASiblingLocalWhileReplacingTheValue() {
        MethodBase target = typeof(LocalPositionSiblingHost).GetMethod(nameof(LocalPositionSiblingHost.Compute))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Blend))!;
        Injection blend = new Injection(injection, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 2), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [blend]);
        Func<LocalPositionSiblingHost, float, string> run = result.Wrapper.CreateDelegate<Func<LocalPositionSiblingHost, float, string>>();

        Assert.Equal("19|7", run(new LocalPositionSiblingHost(), 5f));
    }

    [Fact]
    public void Store_FindsTheValueParameterWhenTheLocalComesFirst() {
        MethodBase target = typeof(LocalPositionSiblingHost).GetMethod(nameof(LocalPositionSiblingHost.Compute))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.BlendReversed))!;
        Injection blend = new Injection(injection, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 2), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [blend]);
        Func<LocalPositionSiblingHost, float, string> run = result.Wrapper.CreateDelegate<Func<LocalPositionSiblingHost, float, string>>();

        Assert.Equal("19|7", run(new LocalPositionSiblingHost(), 5f));
    }

    // A slice bounds both the match set and the By counting, so occurrence 1 inside the range is the
    // store to 'doubled' and the earlier store to 'points' is invisible.
    [Fact]
    public void Store_CountsOnlyInsideTheSlice() {
        MethodBase target = typeof(LocalPositionSlicedHost).GetMethod(nameof(LocalPositionSlicedHost.Work))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        InjectAt.Local site = new InjectAt.Local(typeof(float), LocalAccess.Store, By: 1, Ordinal: 2) {
            Slice = new SliceRange(typeof(LocalPositionAnchorFrom), nameof(LocalPositionAnchorFrom.Start), 1, typeof(LocalPositionAnchorTo), nameof(LocalPositionAnchorTo.End), 1),
        };

        ComposeResult result = WrapperComposer.Compose(target, [new Injection(injection, site, "test", 0)]);
        Func<LocalPositionSlicedHost, float, string> run = result.Wrapper.CreateDelegate<Func<LocalPositionSlicedHost, float, string>>();

        Assert.Equal("6|112", run(new LocalPositionSlicedHost(), 5f));
    }

    [Fact]
    public void Store_ThrowsConc155WhenTheSliceHoldsNoStore() {
        MethodBase target = typeof(LocalPositionSlicedHost).GetMethod(nameof(LocalPositionSlicedHost.Work))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        InjectAt.Local site = new InjectAt.Local(typeof(float), LocalAccess.Store, Ordinal: 1) {
            Slice = new SliceRange(typeof(LocalPositionAnchorFrom), nameof(LocalPositionAnchorFrom.Start), 1, typeof(LocalPositionAnchorTo), nameof(LocalPositionAnchorTo.End), 1),
        };

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [new Injection(injection, site, "test", 0)]));

        Assert.Contains("CONC155", ex.ToString());
        Assert.Contains("every store", ex.ToString());
        Assert.Contains("but 0 store(s) exist inside the declared range", ex.ToString());
    }

    [Fact]
    public void Store_ThrowsConc155WhenByOverrunsTheMatchCount() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => RunPoints(new InjectAt.Local(typeof(float), LocalAccess.Store, By: 5), nameof(LocalPositionMethods.Adjust)));

        Assert.Contains("CONC155", ex.ToString());
        Assert.Contains("store 5", ex.ToString());
        Assert.Contains("but 2 store(s) exist in the method body", ex.ToString());
    }

    // 'box' has one real store, so this composes without CONC154, and the initobj that wipes the
    // slot on the other path is invisible to it.
    [Fact]
    public void Store_ThrowsConc154WhenTheSlotIsWrittenThroughItsAddress() {
        MethodBase target = typeof(LocalPositionIndirectHost).GetMethod(nameof(LocalPositionIndirectHost.Reset))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Bump))!;
        Injection store = new Injection(injection, new InjectAt.Local(typeof(LocalPositionBox), LocalAccess.Store), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [store]));

        Assert.Contains("CONC154", ex.ToString());
        Assert.Contains("In-place mutation will not be seen", ex.ToString());
    }

    // An instance call on a struct local is an ldloca too, and nearly every one of them is
    // read-only. 'points.ToString()' must not cost the injection its store.
    [Fact]
    public void Store_AllowsAnAddressHandedToACall() {
        string result = RunPoints(new InjectAt.Local(typeof(float), LocalAccess.Store, By: 1), nameof(LocalPositionMethods.Adjust));

        Assert.Equal("212", result);
    }

    // Same, from the read side: CONC154 has nothing to say about Load, which never counts ldloca.
    [Fact]
    public void Load_AllowsAnAddressTakenSlot() {
        string result = RunPoints(new InjectAt.Local(typeof(float), LocalAccess.Load, By: 1), nameof(LocalPositionMethods.Adjust));

        Assert.Equal("212", result);
    }

    // The result slot's one load is what NormalizeReturnSites clones into every return path, so an
    // unrelated At.Return or At.Tail injection on the same target would move By under this one.
    [Fact]
    public void Load_ThrowsConc159OnTheResultCarryingSlot() {
        MethodBase target = typeof(LocalPositionResultHost).GetMethod(nameof(LocalPositionResultHost.Split))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        Injection read = new Injection(injection, new InjectAt.Local(typeof(float), LocalAccess.Load), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [read]));

        Assert.Contains("CONC159", ex.ToString());
        Assert.Contains("carries the method's result across 2 return paths", ex.ToString());
    }

    // One return path means NormalizeReturnSites has no branch site to clone into, so the count
    // cannot move and there is nothing to reject.
    [Fact]
    public void Load_OnASingleReturnResultSlotIsAllowed() {
        MethodBase target = typeof(LocalPositionResultHost).GetMethod(nameof(LocalPositionResultHost.Once))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        Injection read = new Injection(injection, new InjectAt.Local(typeof(float), LocalAccess.Load, By: 1), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<LocalPositionResultHost, float, float> run = result.Wrapper.CreateDelegate<Func<LocalPositionResultHost, float, float>>();

        Assert.Equal(105f, run(new LocalPositionResultHost(), 4f));
    }

    // Same slot, same target, store instead of load. CONC159 is about a load count that another
    // mod can move, and store counts are untouched by normalization, so this still composes.
    [Fact]
    public void Store_OnTheResultCarryingSlotIsStillAllowed() {
        MethodBase target = typeof(LocalPositionResultHost).GetMethod(nameof(LocalPositionResultHost.Split))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        Injection store = new Injection(injection, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 1), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [store]);
        Func<LocalPositionResultHost, float, float> run = result.Wrapper.CreateDelegate<Func<LocalPositionResultHost, float, float>>();

        Assert.Equal(105f, run(new LocalPositionResultHost(), 4f));
    }

    // The property At.Local exists for: a declarative injection must not shift another injection's
    // occupancy numbering. A store splice ends in 'stloc slot', which is itself a store of the slot
    // it just matched, so counting against the live spine makes every splice move the next By.
    // seed 5 gives store 1 the value 6. By:1 adds 1000, so store 2 recomputes 1006 * 2 = 2012, and
    // By:2 adds 100 for 2112. Counting against the live spine lands the By:2 splice inside the By:1
    // splice's own trailing stloc, which adds 100 before the doubling and yields 2212.
    [Fact]
    public void Store_CountsByAgainstThePreSpliceSpine() {
        MethodBase target = typeof(LocalPositionHost).GetMethod(nameof(LocalPositionHost.Points))!;
        MethodBase adjust = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        MethodBase thousand = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.AddThousand))!;

        Injection second = new Injection(adjust, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 2), "test", 0);
        Injection first = new Injection(thousand, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 1), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [second, first]);
        Func<LocalPositionHost, float, string> run = result.Wrapper.CreateDelegate<Func<LocalPositionHost, float, string>>();

        Assert.Equal("2112", run(new LocalPositionHost(), 5f));
    }

    // Same root cause from the other side: CopyValueInjection emits 'ldloc slot' for the value
    // parameter, so a store splice adds a load a later Load injection would otherwise count. The
    // load injection doubles rather than adds, so the two orderings cannot land on the same number.
    // Right: store 1 gives 1006, the body's real read doubles it to 2012, and 'points * 2f' gives
    // 4024. Counting live matches the spliced ldloc instead: 6 doubled is 12, plus 1000 is 1012,
    // and the real read doubles that to 2024.
    [Fact]
    public void Load_CountsByAgainstThePreSpliceSpineAfterAStoreSplice() {
        MethodBase target = typeof(LocalPositionHost).GetMethod(nameof(LocalPositionHost.Points))!;
        MethodBase twice = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Double))!;
        MethodBase thousand = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.AddThousand))!;

        Injection load = new Injection(twice, new InjectAt.Local(typeof(float), LocalAccess.Load, By: 1), "test", 0);
        Injection store = new Injection(thousand, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 1), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [load, store]);
        Func<LocalPositionHost, float, string> run = result.Wrapper.CreateDelegate<Func<LocalPositionHost, float, string>>();

        Assert.Equal("4024", run(new LocalPositionHost(), 5f));
    }

    // Task 8 owns At.Local Load. This only pins that the splice shape is right: the matched ldloc
    // spills to a temp, the body replaces the value on the stack, and the slot is not written.
    // 'points = points * 2f' is the body's one real ldloc; ToString() is an address-take, not a load.
    [Fact]
    public void Load_ReplacesTheValueOnTheStackWithoutWritingTheSlot() {
        string result = RunPoints(new InjectAt.Local(typeof(float), LocalAccess.Load, By: 1), nameof(LocalPositionMethods.Adjust));

        Assert.Equal("212", result);
    }

    // 'points * 2f' is the body's one real read. ToString() is an address-take, not a load.
    [Fact]
    public void Load_CountsOnlyRealReads() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => RunPoints(new InjectAt.Local(typeof(float), LocalAccess.Load, By: 2), nameof(LocalPositionMethods.Adjust)));

        Assert.Contains("CONC155", ex.ToString());
        Assert.Contains("but 1 load(s) exist in the method body", ex.ToString());
    }

    // seed 5 stores 6 once and reads it three times. By: 2 hands the second reader 106, and the
    // third reader still sees 6, which is what proves the splice never wrote the slot back.
    [Fact]
    public void Load_ReplacesTheValueAtTheSelectedReadOnly() {
        MethodBase target = typeof(LocalPositionReaderHost).GetMethod(nameof(LocalPositionReaderHost.Thrice))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        Injection read = new Injection(injection, new InjectAt.Local(typeof(float), LocalAccess.Load, By: 2), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<LocalPositionReaderHost, float, string> run = result.Wrapper.CreateDelegate<Func<LocalPositionReaderHost, float, string>>();

        Assert.Equal("6|106|6", run(new LocalPositionReaderHost(), 5f));
    }

    [Fact]
    public void LocalPosition_IsRejectedAlongsideAWholeMethodAround() {
        MethodBase target = typeof(LocalPositionHost).GetMethod(nameof(LocalPositionHost.Points))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        Injection store = new Injection(injection, new InjectAt.Local(typeof(float), LocalAccess.Store), "test", 0);
        Injection around = new Injection(injection, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [store, around]));

        Assert.Contains("CONC115", ex.ToString());
    }

    private static string RunPoints(InjectAt.Local site, string injectionName) {
        MethodBase target = typeof(LocalPositionHost).GetMethod(nameof(LocalPositionHost.Points))!;
        MethodBase injection = typeof(LocalPositionMethods).GetMethod(injectionName)!;

        ComposeResult result = WrapperComposer.Compose(target, [new Injection(injection, site, "test", 0)]);
        Func<LocalPositionHost, float, string> run = result.Wrapper.CreateDelegate<Func<LocalPositionHost, float, string>>();

        return run(new LocalPositionHost(), 5f);
    }
}
