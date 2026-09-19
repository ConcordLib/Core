using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class HandleHost {
    // ToString() takes the local's address, so Release cannot schedule it onto the stack and the
    // slot survives to be written. The string return type keeps the Debug return temp out of the
    // int count, so 'doubled' is the only int local either way.
    public string Work(int seed) {
        int doubled = seed * 2;
        HandleHelper.Touch();
        return doubled.ToString();
    }
}

public static class HandleHelper {
    public static void Touch() {
    }
}

public class HandleMethods {
    public void Double([Local] LocalHandle<int> doubled) {
        doubled.Value *= 10;
    }

    public void Both([Local] int snapshot, [Local] LocalHandle<int> live) {
        HandleRecorder.Snapshot = snapshot;
        live.Value = 77;
    }

    public void Escape([Local] LocalHandle<int> doubled) {
        HandleRecorder.Escaped = doubled;
    }

    public void ReadOnly([Local] LocalHandle<int> doubled) {
        HandleRecorder.Snapshot = doubled.Value;
    }

    public void Mismatch([Local(Index = 0)] LocalHandle<string> wrong) {
        wrong.Value = "no";
    }

    public void Branched([Local] LocalHandle<int> doubled) {
        doubled.Value = HandleRecorder.Snapshot > 0 ? 1 : 2;
    }

    public void Constructs() {
        LocalHandle<int> fresh = new LocalHandle<int>();
        fresh.Value = 5;
        HandleRecorder.Snapshot = fresh.Value;
    }
}

public static class HandleRecorder {
    public static int Snapshot;

    public static object? Escaped;
}

public class TwoHandleHost {
    public string Swap(int seed) {
        int first = seed;
        long second = seed * 2L;
        HandleHelper.Touch();
        return first.ToString() + second.ToString();
    }
}

public class TwoHandleMethods {
    public void Shift([Local] LocalHandle<int> first, [Local] LocalHandle<long> second) {
        first.Value = (int)second.Value + 1;
        second.Value = 9L;
    }
}

public class StoreHandleHost {
    public string Compute(int seed) {
        long other = seed;
        int scaled = seed * 2;
        return scaled.ToString() + other.ToString();
    }
}

public class StoreHandleMethods {
    public int Bump(int original, [Local] LocalHandle<long> other) {
        other.Value = 100L;
        return original + 1;
    }

    // A bare LocalHandle<T> is a supported shape, so the handle declared first must not be mistaken
    // for the value parameter.
    public int BumpHandleFirst(LocalHandle<long> other, int original) {
        other.Value = 100L;
        return original + 1;
    }
}

// Two int parameters, so picking the wrong arg index reads 'bonus' instead of the stored local and
// the difference shows up in the returned string. 'scaled' stays the only int local.
public class WideStoreHandleHost {
    public string Compute(int seed, int bonus) {
        long other = seed;
        int scaled = seed * 2;
        return scaled.ToString() + other.ToString() + bonus.ToString();
    }
}

public sealed class LocalHandleTests {
    [Fact]
    public void HandleWrite_ChangesWhatTheTargetReturns() {
        MethodBase target = typeof(HandleHost).GetMethod(nameof(HandleHost.Work))!;
        MethodBase injection = typeof(HandleMethods).GetMethod(nameof(HandleMethods.Double))!;
        Injection write = new Injection(
            injection, new InjectAt.Invoke(typeof(HandleHelper), nameof(HandleHelper.Touch), At.Tail), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [write]);
        Func<HandleHost, int, string> run = result.Wrapper.CreateDelegate<Func<HandleHost, int, string>>();

        Assert.Equal("100", run(new HandleHost(), 5));
    }

    [Fact]
    public void HandleRead_SeesTheLiveValue() {
        MethodBase target = typeof(HandleHost).GetMethod(nameof(HandleHost.Work))!;
        MethodBase injection = typeof(HandleMethods).GetMethod(nameof(HandleMethods.ReadOnly))!;
        Injection read = new Injection(
            injection, new InjectAt.Invoke(typeof(HandleHelper), nameof(HandleHelper.Touch), At.Head), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<HandleHost, int, string> run = result.Wrapper.CreateDelegate<Func<HandleHost, int, string>>();

        HandleRecorder.Snapshot = 0;
        Assert.Equal("10", run(new HandleHost(), 5));
        Assert.Equal(10, HandleRecorder.Snapshot);
    }

    [Fact]
    public void SnapshotAndHandle_OnTheSameLocal_KeepTheCallTimeValue() {
        MethodBase target = typeof(HandleHost).GetMethod(nameof(HandleHost.Work))!;
        MethodBase injection = typeof(HandleMethods).GetMethod(nameof(HandleMethods.Both))!;
        Injection write = new Injection(
            injection, new InjectAt.Invoke(typeof(HandleHelper), nameof(HandleHelper.Touch), At.Tail), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [write]);
        Func<HandleHost, int, string> run = result.Wrapper.CreateDelegate<Func<HandleHost, int, string>>();

        HandleRecorder.Snapshot = 0;
        Assert.Equal("77", run(new HandleHost(), 5));
        Assert.Equal(10, HandleRecorder.Snapshot);
    }

    [Fact]
    public void TwoHandles_PairEachLoadWithItsOwnCall() {
        MethodBase target = typeof(TwoHandleHost).GetMethod(nameof(TwoHandleHost.Swap))!;
        MethodBase injection = typeof(TwoHandleMethods).GetMethod(nameof(TwoHandleMethods.Shift))!;
        Injection write = new Injection(
            injection, new InjectAt.Invoke(typeof(HandleHelper), nameof(HandleHelper.Touch), At.Tail), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [write]);
        Func<TwoHandleHost, int, string> run = result.Wrapper.CreateDelegate<Func<TwoHandleHost, int, string>>();

        Assert.Equal("119", run(new TwoHandleHost(), 5));
    }

    [Fact]
    public void AtLocal_WritesAnotherSlotWhileReplacingTheStoredValue() {
        MethodBase target = typeof(StoreHandleHost).GetMethod(nameof(StoreHandleHost.Compute))!;
        MethodBase injection = typeof(StoreHandleMethods).GetMethod(nameof(StoreHandleMethods.Bump))!;
        Injection write = new Injection(
            injection, new InjectAt.Local(typeof(int), LocalAccess.Store), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [write]);
        Func<StoreHandleHost, int, string> run = result.Wrapper.CreateDelegate<Func<StoreHandleHost, int, string>>();

        Assert.Equal("11100", run(new StoreHandleHost(), 5));
    }

    [Fact]
    public void BareHandleBeforeTheValueParameter_StillReplacesTheStoredValue() {
        MethodBase target = typeof(WideStoreHandleHost).GetMethod(nameof(WideStoreHandleHost.Compute))!;
        MethodBase injection = typeof(StoreHandleMethods).GetMethod(nameof(StoreHandleMethods.BumpHandleFirst))!;
        Injection write = new Injection(
            injection, new InjectAt.Local(typeof(int), LocalAccess.Store), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [write]);
        Func<WideStoreHandleHost, int, int, string> run =
            result.Wrapper.CreateDelegate<Func<WideStoreHandleHost, int, int, string>>();

        Assert.Equal("1110040", run(new WideStoreHandleHost(), 5, 40));
    }

    // The narrow target has no second argument to read by mistake, so the same miscount emits IL
    // that fails verification at JIT rather than returning a wrong number.
    [Fact]
    public void BareHandleBeforeTheValueParameter_EmitsVerifiableIl() {
        MethodBase target = typeof(StoreHandleHost).GetMethod(nameof(StoreHandleHost.Compute))!;
        MethodBase injection = typeof(StoreHandleMethods).GetMethod(nameof(StoreHandleMethods.BumpHandleFirst))!;
        Injection write = new Injection(
            injection, new InjectAt.Local(typeof(int), LocalAccess.Store), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [write]);
        Func<StoreHandleHost, int, string> run = result.Wrapper.CreateDelegate<Func<StoreHandleHost, int, string>>();

        Assert.Equal("11100", run(new StoreHandleHost(), 5));
    }

    [Fact]
    public void WriteIllegalPosition_IsRejected() {
        MethodBase target = typeof(HandleHost).GetMethod(nameof(HandleHost.Work))!;
        MethodBase injection = typeof(HandleMethods).GetMethod(nameof(HandleMethods.Double))!;
        Injection write = new Injection(injection, new InjectAt.Tail(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [write]));

        Assert.Equal("CONC156", error.Code);
        Assert.Contains("At.Tail", error.Message);
    }

    [Fact]
    public void HandleOnAWholeMethodAround_IsRejected() {
        MethodBase target = typeof(HandleHost).GetMethod(nameof(HandleHost.Work))!;
        MethodBase injection = typeof(HandleMethods).GetMethod(nameof(HandleMethods.Double))!;
        Injection write = new Injection(injection, new InjectAt.Around(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [write]));

        Assert.Equal("CONC160", error.Code);
        Assert.Contains("LocalHandle<T>", error.Message);
    }

    [Fact]
    public void IndexSelectorOfTheWrongType_IsRejected() {
        MethodBase target = typeof(HandleHost).GetMethod(nameof(HandleHost.Work))!;
        MethodBase injection = typeof(HandleMethods).GetMethod(nameof(HandleMethods.Mismatch))!;
        Injection write = new Injection(
            injection, new InjectAt.Invoke(typeof(HandleHelper), nameof(HandleHelper.Touch), At.Tail), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [write]));

        Assert.Equal("CONC150", error.Code);
        Assert.Contains("slot 0", error.Message);
        Assert.Contains("parameter 'wrong'", error.Message);
    }

    [Fact]
    public void EscapingTheHandle_IsRejected() {
        MethodBase target = typeof(HandleHost).GetMethod(nameof(HandleHost.Work))!;
        MethodBase injection = typeof(HandleMethods).GetMethod(nameof(HandleMethods.Escape))!;
        Injection write = new Injection(
            injection, new InjectAt.Invoke(typeof(HandleHelper), nameof(HandleHelper.Touch), At.Tail), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [write]));

        Assert.Equal("CONC161", error.Code);
    }

    [Fact]
    public void WritingTheHandleAcrossAConditional_NamesTheBranchAsTheCause() {
        MethodBase target = typeof(HandleHost).GetMethod(nameof(HandleHost.Work))!;
        MethodBase injection = typeof(HandleMethods).GetMethod(nameof(HandleMethods.Branched))!;
        Injection write = new Injection(
            injection, new InjectAt.Invoke(typeof(HandleHelper), nameof(HandleHelper.Touch), At.Tail), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [write]));

        Assert.Equal("CONC161", error.Code);
        Assert.Contains("conditional", error.Message);
        Assert.Contains("if/else", error.Message);
    }

    [Fact]
    public void ConstructingAHandle_IsRejected() {
        MethodBase target = typeof(HandleHost).GetMethod(nameof(HandleHost.Work))!;
        MethodBase injection = typeof(HandleMethods).GetMethod(nameof(HandleMethods.Constructs))!;
        Injection write = new Injection(
            injection, new InjectAt.Invoke(typeof(HandleHelper), nameof(HandleHelper.Touch), At.Tail), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [write]));

        Assert.Equal("CONC161", error.Code);
        Assert.Contains("constructs a LocalHandle", error.Message);
    }
}
