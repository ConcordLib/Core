using System.Reflection;
using Concord;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class StackedTarget {
    public static int Value() {
        return 0;
    }
}

public static class StackedFirstInjection {
    public static void Add10(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 10;
    }
}

public static class StackedSecondInjection {
    public static void Add100(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 100;
    }
}

public sealed class StackedPatchTests {
    [Fact]
    public void TwoInjectionsOnOneTarget_BothRun() {
        MethodBase target = typeof(StackedTarget).GetMethod(nameof(StackedTarget.Value))!;
        MethodBase first = typeof(StackedFirstInjection).GetMethod(nameof(StackedFirstInjection.Add10))!;
        MethodBase second = typeof(StackedSecondInjection).GetMethod(nameof(StackedSecondInjection.Add100))!;

        Assert.Equal(0, StackedTarget.Value());

        IPatchHandle h1 = Patcher.Patch(target, first, At.Tail);
        IPatchHandle h2 = Patcher.Patch(target, second, At.Tail);
        try {
            Assert.Equal(110, StackedTarget.Value());
        } finally {
            h2.Dispose();
            h1.Dispose();
        }

        Assert.Equal(0, StackedTarget.Value());
    }

    [Fact]
    public void OutOfOrderDispose_RestoresOriginal_AndSurvivingPatchRuns() {
        MethodBase target = typeof(StackedTarget).GetMethod(nameof(StackedTarget.Value))!;
        MethodBase first = typeof(StackedFirstInjection).GetMethod(nameof(StackedFirstInjection.Add10))!;
        MethodBase second = typeof(StackedSecondInjection).GetMethod(nameof(StackedSecondInjection.Add100))!;

        IPatchHandle h1 = Patcher.Patch(target, first, At.Tail);
        IPatchHandle h2 = Patcher.Patch(target, second, At.Tail);

        h1.Dispose();
        Assert.Equal(100, StackedTarget.Value());

        h2.Dispose();
        Assert.Equal(0, StackedTarget.Value());
    }
}
