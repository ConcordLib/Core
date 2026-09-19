using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class AroundLocalHost {
    // The return type is string so a Debug build's return temp is not a second int candidate, and
    // ToString() takes the local's address so a Release build cannot stack-schedule it away.
    public string Work(int seed) {
        int doubled = seed * 2;
        return doubled.ToString();
    }
}

public class TwoSiteLocalHost {
    public string Work(int seed, bool high) {
        int doubled = seed * 2;
        return doubled.ToString() + high;
    }
}

public class AroundLocalMethods {
    public string Wrap(int seed, Operation<int, string> original) {
        return original.Invoke(seed);
    }

    public void ReadDuringReturn([Local] int doubled) {
        LocalRecorder.SeenInt = doubled;
    }
}

public class TwoSiteLocalMethods {
    public string WrapBranched(int seed, bool high, Operation<int, bool, string> original) {
        return high ? original.Invoke(seed * 10, high) : original.Invoke(seed, high);
    }

    public void ReadDuringReturn([Local] int doubled) {
        LocalRecorder.SeenInt = doubled;
    }
}

public class AroundLocalOnWrapperMethods {
    public string Wrap(int seed, [Local] int doubled, Operation<int, string> original) {
        LocalRecorder.SeenInt = doubled;
        return original.Invoke(seed);
    }
}

public sealed class LocalAroundTests {
    [Fact]
    public void ReturnSibling_ReadsTheRunningSpineCopysClone() {
        MethodBase target = typeof(AroundLocalHost).GetMethod(nameof(AroundLocalHost.Work))!;
        MethodBase around = typeof(AroundLocalMethods).GetMethod(nameof(AroundLocalMethods.Wrap))!;
        MethodBase read = typeof(AroundLocalMethods).GetMethod(nameof(AroundLocalMethods.ReadDuringReturn))!;

        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(around, new InjectAt.Around(), "wrapper", 0),
            new Injection(read, new InjectAt.Return(), "reader", 0),
        ]);

        Func<AroundLocalHost, int, string> run = result.Wrapper.CreateDelegate<Func<AroundLocalHost, int, string>>();

        LocalRecorder.SeenInt = 0;
        string returned = run(new AroundLocalHost(), 5);

        Assert.Equal("10", returned);
        Assert.Equal(10, LocalRecorder.SeenInt);
    }

    [Fact]
    public void EachInvokeSite_GetsItsOwnClone() {
        MethodBase target = typeof(TwoSiteLocalHost).GetMethod(nameof(TwoSiteLocalHost.Work))!;
        MethodBase around = typeof(TwoSiteLocalMethods).GetMethod(nameof(TwoSiteLocalMethods.WrapBranched))!;
        MethodBase read = typeof(TwoSiteLocalMethods).GetMethod(nameof(TwoSiteLocalMethods.ReadDuringReturn))!;

        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(around, new InjectAt.Around(), "wrapper", 0),
            new Injection(read, new InjectAt.Return(), "reader", 0),
        ]);

        Func<TwoSiteLocalHost, int, bool, string> run = result.Wrapper.CreateDelegate<Func<TwoSiteLocalHost, int, bool, string>>();
        TwoSiteLocalHost host = new TwoSiteLocalHost();

        LocalRecorder.SeenInt = 0;
        Assert.Equal("100True", run(host, 5, true));
        Assert.Equal(100, LocalRecorder.SeenInt);

        LocalRecorder.SeenInt = 0;
        Assert.Equal("10False", run(host, 5, false));
        Assert.Equal(10, LocalRecorder.SeenInt);
    }

    [Fact]
    public void LocalOnTheAroundMethodItself_IsRejected() {
        MethodBase target = typeof(AroundLocalHost).GetMethod(nameof(AroundLocalHost.Work))!;
        MethodBase around = typeof(AroundLocalOnWrapperMethods).GetMethod(nameof(AroundLocalOnWrapperMethods.Wrap))!;

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [new Injection(around, new InjectAt.Around(), "wrapper", 0)]));

        Assert.Equal("CONC160", error.Code);
        Assert.Contains("At.Return", error.Message, StringComparison.Ordinal);
    }
}
