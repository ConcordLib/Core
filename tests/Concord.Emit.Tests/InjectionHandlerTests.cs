using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class HandlerTargets {
    public static int SpineRuns;

    public static int Throwing() {
        SpineRuns++;
        throw new InvalidOperationException("boom");
    }

    public static int Safe() {
        SpineRuns++;
        return 5;
    }
}

public static class HandlerInjectionMethods {
    public static int TryRan;
    public static int FinallyRan;

    public static int AroundCatch(ControlHandle<int> ch) {
        try {
            return HandlerTargets.Throwing();
        }
        catch (InvalidOperationException) {
            return -1;
        }
    }

    public static void HeadTryFinally() {
        try {
            TryRan++;
        }
        finally {
            FinallyRan++;
        }
    }
}

public static class ThrowingWrapFoo {
    public static int Bar(int x) {
        throw new InvalidOperationException("boom");
    }
}

public class ThrowingWrapTarget {
    public int Run(int x) {
        return ThrowingWrapFoo.Bar(x) * 10;
    }
}

public class WrapCatchInjectionMethods {
    public int GuardedInvoke(int x, Operation<int> op) {
        try {
            return op.Invoke(x);
        }
        catch (InvalidOperationException) {
            return -1;
        }
    }
}

public sealed class InjectionHandlerTests {
    [Fact]
    public void Compose_AroundTryCatch_CatchesSpineException() {
        MethodBase target = typeof(HandlerTargets).GetMethod(nameof(HandlerTargets.Throwing))!;
        MethodBase injectionMethod = typeof(HandlerInjectionMethods).GetMethod(nameof(HandlerInjectionMethods.AroundCatch))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        HandlerTargets.SpineRuns = 0;
        ComposeResult result = WrapperComposer.Compose(target, [around]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(-1, value);
        Assert.Equal(1, HandlerTargets.SpineRuns);
    }

    [Fact]
    public void Compose_HeadTryFinally_RunsFinally() {
        MethodBase target = typeof(HandlerTargets).GetMethod(nameof(HandlerTargets.Safe))!;
        MethodBase injectionMethod = typeof(HandlerInjectionMethods).GetMethod(nameof(HandlerInjectionMethods.HeadTryFinally))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        HandlerTargets.SpineRuns = 0;
        HandlerInjectionMethods.TryRan = 0;
        HandlerInjectionMethods.FinallyRan = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(5, value);
        Assert.Equal(1, HandlerInjectionMethods.TryRan);
        Assert.Equal(1, HandlerInjectionMethods.FinallyRan);
        Assert.Equal(1, HandlerTargets.SpineRuns);
    }

    [Fact]
    public void Compose_WrapTryCatch_CatchesOriginalCallException() {
        MethodBase target = typeof(ThrowingWrapTarget).GetMethod(nameof(ThrowingWrapTarget.Run))!;
        MethodBase injectionMethod = typeof(WrapCatchInjectionMethods).GetMethod(nameof(WrapCatchInjectionMethods.GuardedInvoke))!;
        Injection injection = new Injection(injectionMethod, new InjectAt.Invoke(typeof(ThrowingWrapFoo), nameof(ThrowingWrapFoo.Bar), At.Around, 0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [injection]);

        ThrowingWrapTarget instance = new ThrowingWrapTarget();
        Func<ThrowingWrapTarget, int, int> run = result.Wrapper.CreateDelegate<Func<ThrowingWrapTarget, int, int>>();
        int value = run(instance, 3);

        Assert.Equal(-10, value);
    }
}
