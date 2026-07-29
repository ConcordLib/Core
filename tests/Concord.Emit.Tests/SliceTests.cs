using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class SliceLog {
    public static void Begin() { }

    public static void End() { }
}

public static class SliceCart {
    public static int Total(int seed) {
        return seed + 1;
    }

    public static int Prep(int seed) {
        return seed * 2;
    }
}

public sealed class SliceTicket {
    public SliceTicket(int seed) {
        Value = seed;
    }

    public int Value { get; }
}

public class SliceHost {
    public int Checkout(int seed) {
        int outside = SliceCart.Total(seed);
        SliceLog.Begin();
        int inside = SliceCart.Total(outside);
        SliceLog.End();
        int after = SliceCart.Total(inside);
        return after;
    }

    public int Issue(int seed) {
        int outside = new SliceTicket(seed).Value;
        SliceLog.Begin();
        int inside = new SliceTicket(outside + 1).Value;
        SliceLog.End();
        return inside;
    }

    public int Audited(int seed) {
        int prepared = SliceCart.Prep(seed);
        SliceLog.Begin();
        int inside = SliceCart.Total(prepared);
        SliceLog.End();
        return inside;
    }
}

public static class SliceRecorder {
    public static int Hits;

    public static int LastSeed;
}

public class SliceMethods {
    public void Before([Capture(1)] int seed) {
        SliceRecorder.Hits++;
        SliceRecorder.LastSeed = seed;
    }

    [Slice(typeof(SliceLog), nameof(SliceLog.Begin), 1, typeof(SliceLog), nameof(SliceLog.End))]
    public void SlicedAtHead() {
        SliceRecorder.Hits++;
    }
}

public sealed class SliceTests {
    [Fact]
    public void Slice_CountsOnlyInsideTheRange() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(typeof(SliceLog), nameof(SliceLog.Begin), 1, typeof(SliceLog), nameof(SliceLog.End), 1);

        ComposeResult result = WrapperComposer.Compose(target, [Sliced(range, 1)]);
        Func<SliceHost, int, int> run = result.Wrapper.CreateDelegate<Func<SliceHost, int, int>>();

        SliceRecorder.Hits = 0;
        SliceRecorder.LastSeed = 0;
        run(new SliceHost(), 5);

        Assert.Equal(1, SliceRecorder.Hits);
        Assert.Equal(6, SliceRecorder.LastSeed);
    }

    [Fact]
    public void NoSlice_CountsAcrossTheWholeBody() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;

        ComposeResult result = WrapperComposer.Compose(target, [Sliced(null, 1)]);
        Func<SliceHost, int, int> run = result.Wrapper.CreateDelegate<Func<SliceHost, int, int>>();

        SliceRecorder.Hits = 0;
        SliceRecorder.LastSeed = 0;
        run(new SliceHost(), 5);

        Assert.Equal(1, SliceRecorder.Hits);
        Assert.Equal(5, SliceRecorder.LastSeed);
    }

    [Fact]
    public void OpenEndedSlice_RunsToTheBodyTail() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(typeof(SliceLog), nameof(SliceLog.End), 1, null, null, 1);

        ComposeResult result = WrapperComposer.Compose(target, [Sliced(range, 1)]);
        Func<SliceHost, int, int> run = result.Wrapper.CreateDelegate<Func<SliceHost, int, int>>();

        SliceRecorder.Hits = 0;
        SliceRecorder.LastSeed = 0;
        run(new SliceHost(), 5);

        Assert.Equal(1, SliceRecorder.Hits);
        Assert.Equal(7, SliceRecorder.LastSeed);
    }

    [Fact]
    public void MissingFromAnchor_ThrowsConc131() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(typeof(SliceLog), nameof(SliceLog.Begin), 4, null, null, 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [Sliced(range, 1)]));

        Assert.Contains("CONC131", ex.ToString());
        Assert.Contains("opens at occurrence 4 of 'SliceLog.Begin'", ex.ToString());
    }

    [Fact]
    public void MissingToAnchor_ThrowsConc132() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(null, null, 1, typeof(SliceLog), nameof(SliceLog.End), 7);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [Sliced(range, 1)]));

        Assert.Contains("CONC132", ex.ToString());
        Assert.Contains("closes at occurrence 7 of 'SliceLog.End'", ex.ToString());
    }

    [Fact]
    public void FromMemberWithoutItsType_ThrowsConc131() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(null, nameof(SliceLog.Begin), 1, null, null, 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [Sliced(range, 1)]));

        Assert.Contains("CONC131", ex.ToString());
        Assert.Contains("opens at a half-specified anchor", ex.ToString());
        Assert.Contains("'Begin' was named without a declaring type, so fromType is missing.", ex.ToString());
        Assert.Contains("neither to open at the body head", ex.ToString());
    }

    [Fact]
    public void FromTypeWithoutItsMember_ThrowsConc131() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(typeof(SliceLog), null, 1, null, null, 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [Sliced(range, 1)]));

        Assert.Contains("CONC131", ex.ToString());
        Assert.Contains("opens at a half-specified anchor", ex.ToString());
        Assert.Contains("'SliceLog' was named without a member, so fromMember is missing.", ex.ToString());
    }

    [Fact]
    public void ToMemberWithoutItsType_ThrowsConc132() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(null, null, 1, null, nameof(SliceLog.End), 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [Sliced(range, 1)]));

        Assert.Contains("CONC132", ex.ToString());
        Assert.Contains("closes at a half-specified anchor", ex.ToString());
        Assert.Contains("'End' was named without a declaring type, so toType is missing.", ex.ToString());
        Assert.Contains("neither to close at the body tail", ex.ToString());
    }

    [Fact]
    public void ToTypeWithoutItsMember_ThrowsConc132() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(null, null, 1, typeof(SliceLog), null, 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [Sliced(range, 1)]));

        Assert.Contains("CONC132", ex.ToString());
        Assert.Contains("closes at a half-specified anchor", ex.ToString());
        Assert.Contains("'SliceLog' was named without a member, so toMember is missing.", ex.ToString());
    }

    [Fact]
    public void InvertedSlice_ThrowsConc133() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(typeof(SliceLog), nameof(SliceLog.End), 1, typeof(SliceLog), nameof(SliceLog.Begin), 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [Sliced(range, 1)]));

        Assert.Contains("CONC133", ex.ToString());
        Assert.Contains("empty or inverted", ex.ToString());
        Assert.Contains("just after 'SliceLog.End' occurrence 1", ex.ToString());
        Assert.Contains("just before 'SliceLog.Begin' occurrence 1", ex.ToString());
    }

    [Fact]
    public void SliceWithNoMatchInRange_SaysTheRangeNotTheBody() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Audited))!;
        MethodBase injection = typeof(SliceMethods).GetMethod(nameof(SliceMethods.Before))!;
        SliceRange range = new SliceRange(typeof(SliceLog), nameof(SliceLog.Begin), 1, typeof(SliceLog), nameof(SliceLog.End), 1);
        InjectAt.Invoke at = new InjectAt.Invoke(typeof(SliceCart), nameof(SliceCart.Prep), At.Head, 1) { Slice = range };

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [new Injection(injection, at, "test", 0)]));

        Assert.Contains("CONC031", ex.ToString());
        Assert.Contains("does not occur inside the declared range", ex.ToString());
        Assert.DoesNotContain("does not occur in the method body", ex.ToString());
    }

    [Fact]
    public void NoSliceWithNoMatch_StillSaysTheMethodBody() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        MethodBase injection = typeof(SliceMethods).GetMethod(nameof(SliceMethods.Before))!;
        InjectAt.Invoke at = new InjectAt.Invoke(typeof(SliceCart), nameof(SliceCart.Prep), At.Head, 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [new Injection(injection, at, "test", 0)]));

        Assert.Contains("CONC031", ex.ToString());
        Assert.Contains("does not occur in the method body", ex.ToString());
    }

    [Fact]
    public void ByOvershootsInsideTheRange_CountsTheRangeNotTheBody() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        SliceRange range = new SliceRange(typeof(SliceLog), nameof(SliceLog.Begin), 1, typeof(SliceLog), nameof(SliceLog.End), 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [Sliced(range, 2)]));

        Assert.Contains("CONC033", ex.ToString());
        Assert.Contains("but only 1 occurrence(s) exist inside the declared range", ex.ToString());
        Assert.DoesNotContain("in the method body", ex.ToString());
    }

    [Fact]
    public void NoSliceByOvershoot_StillCountsTheMethodBody() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [Sliced(null, 4)]));

        Assert.Contains("CONC033", ex.ToString());
        Assert.Contains("but only 3 occurrence(s) exist in the method body", ex.ToString());
    }

    [Fact]
    public void Slice_BoundsAConstructionSearchAndItsCapture() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Issue))!;
        MethodBase injection = typeof(SliceMethods).GetMethod(nameof(SliceMethods.Before))!;
        SliceRange range = new SliceRange(typeof(SliceLog), nameof(SliceLog.Begin), 1, typeof(SliceLog), nameof(SliceLog.End), 1);
        InjectAt.NewObj at = new InjectAt.NewObj(typeof(SliceTicket), At.Head, 1) { Slice = range };

        ComposeResult result = WrapperComposer.Compose(target, [new Injection(injection, at, "test", 0)]);
        Func<SliceHost, int, int> run = result.Wrapper.CreateDelegate<Func<SliceHost, int, int>>();

        SliceRecorder.Hits = 0;
        SliceRecorder.LastSeed = 0;
        run(new SliceHost(), 5);

        Assert.Equal(1, SliceRecorder.Hits);
        Assert.Equal(6, SliceRecorder.LastSeed);
    }

    [Fact]
    public void NoSlice_CountsConstructionsAcrossTheWholeBody() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Issue))!;
        MethodBase injection = typeof(SliceMethods).GetMethod(nameof(SliceMethods.Before))!;
        InjectAt.NewObj at = new InjectAt.NewObj(typeof(SliceTicket), At.Head, 1);

        ComposeResult result = WrapperComposer.Compose(target, [new Injection(injection, at, "test", 0)]);
        Func<SliceHost, int, int> run = result.Wrapper.CreateDelegate<Func<SliceHost, int, int>>();

        SliceRecorder.Hits = 0;
        SliceRecorder.LastSeed = 0;
        run(new SliceHost(), 5);

        Assert.Equal(1, SliceRecorder.Hits);
        Assert.Equal(5, SliceRecorder.LastSeed);
    }

    [Fact]
    public void SliceOnAWholeMethodPosition_ThrowsConc134() {
        MethodBase target = typeof(SliceHost).GetMethod(nameof(SliceHost.Checkout))!;
        MethodBase injection = typeof(SliceMethods).GetMethod(nameof(SliceMethods.SlicedAtHead))!;
        Injection head = new Injection(injection, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [head]));

        Assert.Contains("CONC134", ex.ToString());
        Assert.Contains("carries [Slice] at position 'At.Head'", ex.ToString());
        Assert.Contains("applies to invoke and construction positions only", ex.ToString());
    }

    private static Injection Sliced(SliceRange? range, uint by) {
        MethodBase injection = typeof(SliceMethods).GetMethod(nameof(SliceMethods.Before))!;
        InjectAt.Invoke at = new InjectAt.Invoke(typeof(SliceCart), nameof(SliceCart.Total), At.Head, by) { Slice = range };
        return new Injection(injection, at, "test", 0);
    }
}
