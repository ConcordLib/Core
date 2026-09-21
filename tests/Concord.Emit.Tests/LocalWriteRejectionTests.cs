using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class LocalWriteHost {
    // Wrapper arg 0 is seed and arg 1 is bonus, so a starg on the [Local] parameter's index would
    // land on bonus. Both locals are address-taken by ToString(), so Release keeps them in slots.
    public static string Compute(int seed, int bonus) {
        int first = seed * 2;
        int second = seed * 3;
        return first + ":" + second + ":" + bonus;
    }
}

public static class LocalWriteLedger {
    public static void Record(int amount) {
        Recorded = amount;
    }

    public static int Recorded { get; set; }
}

public class LocalWriteCaptureHost {
    public string Total(int listed) {
        int doubled = listed * 2;
        LocalWriteLedger.Record(doubled + 1);
        return doubled.ToString();
    }
}

public static class LocalWriteMethods {
    public static int Reassign(int original, [Local(Ordinal = 2)] int second) {
        second = 999;
        LocalRecorder.SeenInt = second;
        return original;
    }

    public static int Increment(int original, [Local(Ordinal = 2)] int second) {
        second++;
        LocalRecorder.SeenInt = second;
        return original;
    }

    public static int Parse(int original, [Local(Ordinal = 1)] int first) {
        _ = int.TryParse("777", out first);
        return original;
    }

    public static int Describe(int original, [Local(Ordinal = 1)] int first) {
        LocalRecorder.SeenInt = first.ToString().Length;
        return original;
    }

    public static void CaptureAndLocal([Capture(1), Local] int doubled) {
        LocalRecorder.SeenInt = doubled;
    }
}

public sealed class LocalWriteRejectionTests {
    [Theory]
    [InlineData(nameof(LocalWriteMethods.Reassign))]
    [InlineData(nameof(LocalWriteMethods.Increment))]
    public void AssigningALocalParameter_Throws(string name) {
        MethodBase target = typeof(LocalWriteHost).GetMethod(nameof(LocalWriteHost.Compute))!;
        MethodBase injection = typeof(LocalWriteMethods).GetMethod(name)!;

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [
                new Injection(injection, new InjectAt.Local(typeof(int), LocalAccess.Store, Ordinal: 1, By: 1), "test", 0),
            ]));

        Assert.Equal("CONC164", error.Code);
        Assert.Contains("parameter 'second'", error.Message);
    }

    // The bound slot has to be one the target already assigned, and the injection point a store to a
    // different slot. Bind the slot being stored to and the target overwrites the corruption on the
    // way out, so the test passes with or without the copy. Here the fix is the only thing standing
    // between 'first' and 777: without it this reads "777:15:40".
    [Fact]
    public void AnOutArgumentOnALocalParameter_LeavesTheTargetSlotAlone() {
        MethodBase target = typeof(LocalWriteHost).GetMethod(nameof(LocalWriteHost.Compute))!;
        MethodBase injection = typeof(LocalWriteMethods).GetMethod(nameof(LocalWriteMethods.Parse))!;

        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(injection, new InjectAt.Local(typeof(int), LocalAccess.Store, Ordinal: 2, By: 1), "test", 0),
        ]);

        Func<int, int, string> run = result.Wrapper.CreateDelegate<Func<int, int, string>>();

        Assert.Equal("10:15:40", run(5, 40));
    }

    // An instance call on a struct local takes its address too. Rejecting every ldarga would make
    // [Local] unusable on int, float, DateTime and every user struct, so the copy has to carry the
    // slot's real value: 'first' is 10 by the store of 'second', so ToString() is two characters.
    [Fact]
    public void AnInstanceCallOnAStructLocalParameter_StillReads() {
        MethodBase target = typeof(LocalWriteHost).GetMethod(nameof(LocalWriteHost.Compute))!;
        MethodBase injection = typeof(LocalWriteMethods).GetMethod(nameof(LocalWriteMethods.Describe))!;

        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(injection, new InjectAt.Local(typeof(int), LocalAccess.Store, Ordinal: 2, By: 1), "test", 0),
        ]);

        Func<int, int, string> run = result.Wrapper.CreateDelegate<Func<int, int, string>>();
        LocalRecorder.SeenInt = -1;

        Assert.Equal("10:15:40", run(5, 40));
        Assert.Equal(2, LocalRecorder.SeenInt);
    }

    [Fact]
    public void CaptureAndLocalOnOneParameter_Throws() {
        MethodBase target = typeof(LocalWriteCaptureHost).GetMethod(nameof(LocalWriteCaptureHost.Total))!;
        MethodBase injection = typeof(LocalWriteMethods).GetMethod(nameof(LocalWriteMethods.CaptureAndLocal))!;

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [
                new Injection(injection, new InjectAt.Invoke(typeof(LocalWriteLedger), nameof(LocalWriteLedger.Record), At.Head, 1), "test", 0),
            ]));

        Assert.Equal("CONC165", error.Code);
        Assert.Contains("parameter 'doubled'", error.Message);
    }
}
