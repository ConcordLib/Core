using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Emit.Tests;

public class CtorTarget {
    public int Seed;

    public CtorTarget(int seed) {
        Seed = seed;
    }
}

public static class CtorSpikeLog {
    public static int HeadFiredCount;

    public static void Reset() {
        HeadFiredCount = 0;
    }
}

public static class CtorSpikeInjectionMethods {
    public static void HeadInjectionMethod(ControlHandle ch) {
        CtorSpikeLog.HeadFiredCount++;
    }
}

public sealed class ConstructorTargetSpikeTests {
    [Fact]
    public void ConstructorTargetSpike_HeadFiresAndOriginalRuns() {
        ConstructorInfo ctor = typeof(CtorTarget).GetConstructor([typeof(int)])!;
        MethodBase injectionMethod = typeof(CtorSpikeInjectionMethods).GetMethod(nameof(CtorSpikeInjectionMethods.HeadInjectionMethod))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "spike", 0);

        ComposeResult result = WrapperComposer.Compose(ctor, [head]);
        IDetourHandle handle = DetourBackend.Current.Apply(ctor, result.Wrapper);

        CtorSpikeLog.Reset();
        CtorTarget instance = new CtorTarget(42);

        Assert.Equal(1, CtorSpikeLog.HeadFiredCount);
        Assert.Equal(42, instance.Seed);

        handle.Dispose();

        CtorSpikeLog.Reset();
        CtorTarget after = new CtorTarget(99);

        Assert.Equal(0, CtorSpikeLog.HeadFiredCount);
        Assert.Equal(99, after.Seed);
    }
}
