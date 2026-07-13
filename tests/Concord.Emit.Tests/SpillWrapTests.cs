using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class SpillOccupant {
    public float Age;

    public float AgeYears => Age;
}

public class SpillHost {
    public SpillOccupant occupant = new SpillOccupant();

    public bool ShouldEject() {
        return occupant.AgeYears >= 18f;
    }
}

public static class SpillComputedFoo {
    public static int Calls;

    public static int Bar(int x) {
        Calls++;
        return x + 1;
    }
}

public class SpillComputedTarget {
    public int Run(int x) {
        return SpillComputedFoo.Bar(x + 5) * 10;
    }
}

public class SpillInjectionMethods {
    public float ShimAge(Operation<float> age) {
        return age.Invoke() - 2f;
    }

    public int ReplaceComputedArg(Operation<int, int> op) {
        return op.Invoke(7);
    }

    public int SkipComputed(Operation<int, int> op) {
        return 0;
    }
}

public sealed class SpillWrapTests {
    private static ComposeResult ComposeWrap(System.Type targetType, string targetMethod, System.Type siteType, string siteMethod, string injectionName) {
        MethodBase target = targetType.GetMethod(targetMethod)!;
        MethodBase injection = typeof(SpillInjectionMethods).GetMethod(injectionName)!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(siteType, siteMethod, At.Around, 0), "test", 0);
        return WrapperComposer.Compose(target, [wrap]);
    }

    [Fact]
    public void GetterBehindReceiverChain_ShiftsComparison() {
        ComposeResult result = ComposeWrap(typeof(SpillHost), nameof(SpillHost.ShouldEject), typeof(SpillOccupant), "get_AgeYears", nameof(SpillInjectionMethods.ShimAge));
        System.Func<SpillHost, bool> run = result.Wrapper.CreateDelegate<System.Func<SpillHost, bool>>();

        SpillHost host = new SpillHost();
        host.occupant.Age = 19f;
        Assert.False(run(host));

        host.occupant.Age = 21f;
        Assert.True(run(host));
    }

    [Fact]
    public void ComputedArgument_ReplacedThroughInvoke() {
        ComposeResult result = ComposeWrap(typeof(SpillComputedTarget), nameof(SpillComputedTarget.Run), typeof(SpillComputedFoo), nameof(SpillComputedFoo.Bar), nameof(SpillInjectionMethods.ReplaceComputedArg));
        System.Func<SpillComputedTarget, int, int> run = result.Wrapper.CreateDelegate<System.Func<SpillComputedTarget, int, int>>();

        SpillComputedFoo.Calls = 0;
        Assert.Equal(80, run(new SpillComputedTarget(), 3));
        Assert.Equal(1, SpillComputedFoo.Calls);
    }

    [Fact]
    public void SkippedOriginal_LeavesStackBalanced() {
        ComposeResult result = ComposeWrap(typeof(SpillComputedTarget), nameof(SpillComputedTarget.Run), typeof(SpillComputedFoo), nameof(SpillComputedFoo.Bar), nameof(SpillInjectionMethods.SkipComputed));
        System.Func<SpillComputedTarget, int, int> run = result.Wrapper.CreateDelegate<System.Func<SpillComputedTarget, int, int>>();

        SpillComputedFoo.Calls = 0;
        Assert.Equal(0, run(new SpillComputedTarget(), 3));
        Assert.Equal(0, SpillComputedFoo.Calls);
    }
}
