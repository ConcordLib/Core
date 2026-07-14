using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Emit.Tests;

public static class AroundComposeLog {
    public static List<string> Entries = new List<string>();

    public static void Clear() {
        Entries.Clear();
    }
}

public static class AroundComposeTarget {
    public static int Step() {
        AroundComposeLog.Entries.Add("body");
        return 7;
    }
}

public static class AroundComposeInjectionMethods {
    public static void Head(ControlHandle ch) {
        AroundComposeLog.Entries.Add("head");
    }

    public static void CancelHead(ControlHandle<int> ch) {
        AroundComposeLog.Entries.Add("head");
        ch.ReturnValue = 99;
        ch.Cancel();
    }

    public static int Around(Operation<int> original) {
        AroundComposeLog.Entries.Add("pre");
        int result = original.Invoke();
        AroundComposeLog.Entries.Add("post");
        return result;
    }
}

public sealed class AroundComposeCtorTarget {
    public int Seed;

    public AroundComposeCtorTarget(int seed) {
        Seed = seed;
        AroundComposeLog.Entries.Add("ctor-body:" + seed);
    }
}

public static class AroundComposeCtorInjectionMethods {
    public static void CancelHead(int seed, ControlHandle ch) {
        AroundComposeLog.Entries.Add("head");
        ch.Cancel();
    }

    public static void Around(int seed, VoidOperation<int> original) {
        AroundComposeLog.Entries.Add("pre");
        original.Invoke(seed);
        AroundComposeLog.Entries.Add("post");
    }
}

public sealed class AroundComposeTests {
    [Fact]
    public void Compose_HeadAndAround_HeadRunsBeforeAroundPreCode() {
        MethodBase target = typeof(AroundComposeTarget).GetMethod(nameof(AroundComposeTarget.Step))!;
        MethodBase headInjectionMethod = typeof(AroundComposeInjectionMethods).GetMethod(nameof(AroundComposeInjectionMethods.Head))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeInjectionMethods).GetMethod(nameof(AroundComposeInjectionMethods.Around))!;

        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "test", 0);
        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 1);

        AroundComposeLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [head, around]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(7, value);
        Assert.Equal(["head", "pre", "body", "post"], AroundComposeLog.Entries);
    }

    [Fact]
    public void Compose_CancellingHeadWithAround_SkipsWholeAroundAndReturnsHeadValue() {
        MethodBase target = typeof(AroundComposeTarget).GetMethod(nameof(AroundComposeTarget.Step))!;
        MethodBase headInjectionMethod = typeof(AroundComposeInjectionMethods).GetMethod(nameof(AroundComposeInjectionMethods.CancelHead))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeInjectionMethods).GetMethod(nameof(AroundComposeInjectionMethods.Around))!;

        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "test", 0);
        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 1);

        AroundComposeLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [head, around]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(99, value);
        Assert.Equal(["head"], AroundComposeLog.Entries);
    }

    [Fact]
    public void Compose_HeadPriorityAboveAround_SameCompositionAsBelow() {
        MethodBase target = typeof(AroundComposeTarget).GetMethod(nameof(AroundComposeTarget.Step))!;
        MethodBase headInjectionMethod = typeof(AroundComposeInjectionMethods).GetMethod(nameof(AroundComposeInjectionMethods.Head))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeInjectionMethods).GetMethod(nameof(AroundComposeInjectionMethods.Around))!;

        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "test", 1);
        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);

        AroundComposeLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [around, head]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(7, value);
        Assert.Equal(["head", "pre", "body", "post"], AroundComposeLog.Entries);
    }

    [Fact]
    public void Compose_CtorHeadCancelWithAround_ReachesEpilogueWithBodyNotRunAndThrows() {
        ConstructorInfo ctor = typeof(AroundComposeCtorTarget).GetConstructor([typeof(int)])!;
        MethodBase headInjectionMethod = typeof(AroundComposeCtorInjectionMethods).GetMethod(nameof(AroundComposeCtorInjectionMethods.CancelHead))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeCtorInjectionMethods).GetMethod(nameof(AroundComposeCtorInjectionMethods.Around))!;

        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "test", 0);
        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 1);

        ComposeResult result = WrapperComposer.Compose(ctor, [head, around]);
        IDetourHandle handle = DetourBackend.Current.Apply(ctor, result.Wrapper);

        AroundComposeLog.Clear();

        try {
            Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(typeof(AroundComposeCtorTarget), 3));

            Assert.Equal(["head"], AroundComposeLog.Entries);
        } finally {
            handle.Dispose();
        }
    }
}
