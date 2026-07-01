using System.Reflection;
using Concord.Emit.Tests.ForeignTargets;
using Xunit;

namespace Concord.Emit.Tests;

public static class RealIlTargets {
    public static int SpineRuns;

    public static int Combine(int a, int b, int c, int d) {
        SpineRuns++;
        return a + b + c + d;
    }
}

public static class RealIlInjectionMethods {
    public static void HeadManyArgs(ControlHandle<int> ch, int a, int b, int c, int d) {
        int weighted = (a * 1000) + (b * 100) + (c * 10) + d;
        ch.ReturnValue = weighted;
        ch.Cancel();
    }

    public static void HeadReassignsHighArg(ControlHandle<int> ch, int a, int b, int c, int d) {
        d = (d * 2) + a;
        b += d;
        ch.ReturnValue = (b * 100) + (c * 10) + d;
        ch.Cancel();
    }
}

public class RealIlInstanceTarget {
    public int Value;

    public int Step(int delta) {
        return Value += delta;
    }
}

public sealed class RealIlInstanceInjectionMethod : RealIlInstanceTarget {
    public void Head(ControlHandle ch, int delta) {
        int self = Value;
        int scaled = delta * 7;
        Value = self + scaled;
    }
}

public sealed class RealIlWrapFoo {
    public static int BarCalls;

    public static int Bar(int x) {
        BarCalls++;
        return x + 1;
    }
}

public sealed class RealIlWrapTarget {
    public int Run(int a, int b, int c, int d) {
        return RealIlWrapFoo.Bar(a) * 10;
    }
}

public sealed class RealIlWrapInjectionMethod {
    public int WrapManyArgs(int a, int b, int c, int d, Operation<int> op) {
        int combined = (a * 1000) + (b * 100) + (c * 10) + d;
        return op.Invoke(combined);
    }
}

public class PrivateInstanceReturnTarget {
    public int marker;

    private void Work() {
        marker += 1;
    }

    public void CallWork() {
        Work();
    }
}

public abstract class PrivateInstanceReturnInjectionMethod : PrivateInstanceReturnTarget {
    public void OnWork(ControlHandle ch) {
        marker += 100;
    }
}

public sealed class RealMethodIlTests {
    [Fact]
    public void Compose_HeadInjectionMethodWithFourArgs_ReadsExplicitParameterOperands() {
        MethodBase target = typeof(RealIlTargets).GetMethod(nameof(RealIlTargets.Combine))!;
        MethodBase injectionMethod = typeof(RealIlInjectionMethods).GetMethod(nameof(RealIlInjectionMethods.HeadManyArgs))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        RealIlTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        object? value = result.Wrapper.Invoke(null, [2, 4, 6, 8]);

        Assert.Equal(2468, value);
        Assert.Equal(0, RealIlTargets.SpineRuns);
    }

    [Fact]
    public void Compose_HeadInjectionMethodReassignsHighArg_HandlesExplicitStargOperands() {
        MethodBase target = typeof(RealIlTargets).GetMethod(nameof(RealIlTargets.Combine))!;
        MethodBase injectionMethod = typeof(RealIlInjectionMethods).GetMethod(nameof(RealIlInjectionMethods.HeadReassignsHighArg))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        RealIlTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        object? value = result.Wrapper.Invoke(null, [2, 4, 6, 8]);

        Assert.Equal(2278, value);
        Assert.Equal(0, RealIlTargets.SpineRuns);
    }

    [Fact]
    public void Compose_InstanceHeadWithControlHandleAndThis_AppliesRemappedArgs() {
        MethodBase target = typeof(RealIlInstanceTarget).GetMethod(nameof(RealIlInstanceTarget.Step))!;
        MethodBase injectionMethod = typeof(RealIlInstanceInjectionMethod).GetMethod(nameof(RealIlInstanceInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        RealIlInstanceTarget instance = new RealIlInstanceTarget();
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        Func<RealIlInstanceTarget, int, int> step = result.Wrapper.CreateDelegate<Func<RealIlInstanceTarget, int, int>>();
        int value = step(instance, 3);

        Assert.Equal(24, value);
        Assert.Equal(24, instance.Value);
    }

    [Fact]
    public void Compose_NoInjectionForeignStaticSpineWithFiveArgs_CanonicalizesCrossModuleArgOperands() {
        MethodBase target = typeof(ForeignSpineTargets).GetMethod(nameof(ForeignSpineTargets.CombineFive))!;

        ForeignSpineTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, []);
        object? value = result.Wrapper.Invoke(null, [2, 4, 6, 8, 9]);

        Assert.Equal(24689, value);
        Assert.Equal(1, ForeignSpineTargets.SpineRuns);
    }

    [Fact]
    public void Compose_NoInjectionForeignSpineReassignsHighArg_CanonicalizesCrossModuleStargOperands() {
        MethodBase target = typeof(ForeignSpineTargets).GetMethod(nameof(ForeignSpineTargets.ReassignHighArg))!;

        ForeignSpineTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, []);
        object? value = result.Wrapper.Invoke(null, [2, 4, 6, 8, 9]);

        Assert.Equal(24700, value);
        Assert.Equal(1, ForeignSpineTargets.SpineRuns);
    }

    [Fact]
    public void Compose_NoInjectionForeignInstanceSpineWithFiveArgs_CanonicalizesCrossModuleArgOperands() {
        MethodBase target = typeof(ForeignInstanceSpineTarget).GetMethod(nameof(ForeignInstanceSpineTarget.CombineFive))!;

        ForeignInstanceSpineTarget instance = new ForeignInstanceSpineTarget { Value = 1 };
        ComposeResult result = WrapperComposer.Compose(target, []);
        Func<ForeignInstanceSpineTarget, int, int, int, int, int, int> run =
            result.Wrapper.CreateDelegate<Func<ForeignInstanceSpineTarget, int, int, int, int, int, int>>();
        int value = run(instance, 2, 4, 6, 8, 9);

        Assert.Equal(24690, value);
    }

    [Fact]
    public void Compose_WrapInjectionMethodWithFourArgs_SubstitutesArgAndRunsOriginal() {
        MethodBase target = typeof(RealIlWrapTarget).GetMethod(nameof(RealIlWrapTarget.Run))!;
        MethodBase injectionMethod = typeof(RealIlWrapInjectionMethod).GetMethod(nameof(RealIlWrapInjectionMethod.WrapManyArgs))!;
        Injection injection = new Injection(
            injectionMethod,
            new InjectAt.Invoke(typeof(RealIlWrapFoo), nameof(RealIlWrapFoo.Bar), At.Around, 0),
            "test",
            0);

        RealIlWrapFoo.BarCalls = 0;
        ComposeResult result = WrapperComposer.Compose(target, [injection]);

        RealIlWrapTarget instance = new RealIlWrapTarget();
        Func<RealIlWrapTarget, int, int, int, int, int> run =
            result.Wrapper.CreateDelegate<Func<RealIlWrapTarget, int, int, int, int, int>>();
        int value = run(instance, 1, 2, 3, 4);

        Assert.Equal(12350, value);
        Assert.Equal(1, RealIlWrapFoo.BarCalls);
    }

    [Fact]
    public void PrivateInstanceReturn_InjectFires() {
        MethodBase target = typeof(PrivateInstanceReturnTarget)
            .GetMethod("Work", BindingFlags.NonPublic | BindingFlags.Instance)!;
        MethodBase injectionMethod = typeof(PrivateInstanceReturnInjectionMethod)
            .GetMethod(nameof(PrivateInstanceReturnInjectionMethod.OnWork))!;
        Injection inj = new Injection(injectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [inj]);

        PrivateInstanceReturnTarget instance = new PrivateInstanceReturnTarget();
        result.Wrapper.Invoke(null, [instance]);

        Assert.Equal(101, instance.marker);
    }
}
