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

public static class AroundComposeReturnTarget {
    public static int Pick(int which) {
        if (which == 0) {
            return 10;
        }

        if (which == 1) {
            return 20;
        }

        return 30;
    }
}

public static class AroundComposeReturnInjectionMethods {
    public static int PassThrough(int which, Operation<int, int> original) {
        return original.Invoke(which);
    }

    public static void Double(ControlHandle<int> ch) {
        int current = ch.ReturnValue;
        ch.ReturnValue = current * 2;
    }
}

public static class AroundComposeChangedArgTarget {
    public static int Double(int x) {
        return x * 2;
    }
}

public static class AroundComposeChangedArgInjectionMethods {
    public static int WrapChangedArg(int x, Operation<int, int> original) {
        return original.Invoke(x - 2);
    }

    public static void ObserveArg(ControlHandle<int> ch, int x) {
        ch.ReturnValue = x;
    }
}

public static class AroundComposeTailTarget {
    public static int Pick(int which) {
        if (which == 0) {
            return 10;
        }

        if (which == 1) {
            return 20;
        }

        return 30;
    }
}

public static class AroundComposeTailInjectionMethods {
    public static int Times10(int which, Operation<int, int> original) {
        int result = original.Invoke(which);
        return result * 10;
    }

    public static void Double(ControlHandle<int> ch) {
        ch.ReturnValue *= 2;
    }
}

public static class AroundComposeMultiInvokeTailTarget {
    public static int Triple(int x) {
        return x * 3;
    }
}

public static class AroundComposeMultiInvokeTailInjectionMethods {
    public static int InvokeTwiceAndSum(int x, Operation<int, int> original) {
        return original.Invoke(x) + original.Invoke(x + 1);
    }

    public static void Double(ControlHandle<int> ch) {
        ch.ReturnValue *= 2;
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

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 40)]
    [InlineData(2, 60)]
    public void Compose_ReturnWithAround_DoublesEveryBodyReturnThroughSpliceValue(int which, int expected) {
        MethodBase target = typeof(AroundComposeReturnTarget).GetMethod(nameof(AroundComposeReturnTarget.Pick))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeReturnInjectionMethods).GetMethod(nameof(AroundComposeReturnInjectionMethods.PassThrough))!;
        MethodBase returnInjectionMethod = typeof(AroundComposeReturnInjectionMethods).GetMethod(nameof(AroundComposeReturnInjectionMethods.Double))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);
        Injection ret = new Injection(returnInjectionMethod, new InjectAt.Return(0), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [around, ret]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 40)]
    [InlineData(2, 60)]
    public void Compose_ReturnPriorityAboveAround_SameResultAsBelow(int which, int expected) {
        MethodBase target = typeof(AroundComposeReturnTarget).GetMethod(nameof(AroundComposeReturnTarget.Pick))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeReturnInjectionMethods).GetMethod(nameof(AroundComposeReturnInjectionMethods.PassThrough))!;
        MethodBase returnInjectionMethod = typeof(AroundComposeReturnInjectionMethods).GetMethod(nameof(AroundComposeReturnInjectionMethods.Double))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 1);
        Injection ret = new Injection(returnInjectionMethod, new InjectAt.Return(0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret, around]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Compose_ReturnWithAroundChangedArg_ObservesInvokeArgNotAmbientArg() {
        MethodBase target = typeof(AroundComposeChangedArgTarget).GetMethod(nameof(AroundComposeChangedArgTarget.Double))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeChangedArgInjectionMethods).GetMethod(nameof(AroundComposeChangedArgInjectionMethods.WrapChangedArg))!;
        MethodBase returnInjectionMethod = typeof(AroundComposeChangedArgInjectionMethods).GetMethod(nameof(AroundComposeChangedArgInjectionMethods.ObserveArg))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);
        Injection ret = new Injection(returnInjectionMethod, new InjectAt.Return(0), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [around, ret]);
        object? value = result.Wrapper.Invoke(null, [5]);

        Assert.Equal(3, value);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 200)]
    [InlineData(2, 600)]
    public void Compose_TailWithAround_FiresBeforePostCodeOnLastBodyReturnOnly(int which, int expected) {
        MethodBase target = typeof(AroundComposeTailTarget).GetMethod(nameof(AroundComposeTailTarget.Pick))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeTailInjectionMethods).GetMethod(nameof(AroundComposeTailInjectionMethods.Times10))!;
        MethodBase tailInjectionMethod = typeof(AroundComposeTailInjectionMethods).GetMethod(nameof(AroundComposeTailInjectionMethods.Double))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);
        Injection tail = new Injection(tailInjectionMethod, new InjectAt.Tail(), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [around, tail]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 200)]
    [InlineData(2, 600)]
    public void Compose_TailPriorityAboveAround_SameResultAsBelow(int which, int expected) {
        MethodBase target = typeof(AroundComposeTailTarget).GetMethod(nameof(AroundComposeTailTarget.Pick))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeTailInjectionMethods).GetMethod(nameof(AroundComposeTailInjectionMethods.Times10))!;
        MethodBase tailInjectionMethod = typeof(AroundComposeTailInjectionMethods).GetMethod(nameof(AroundComposeTailInjectionMethods.Double))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 1);
        Injection tail = new Injection(tailInjectionMethod, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [tail, around]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Compose_TailWithAroundMultipleInvoke_FiresPerBodyRun() {
        MethodBase target = typeof(AroundComposeMultiInvokeTailTarget).GetMethod(nameof(AroundComposeMultiInvokeTailTarget.Triple))!;
        MethodBase aroundInjectionMethod = typeof(AroundComposeMultiInvokeTailInjectionMethods).GetMethod(nameof(AroundComposeMultiInvokeTailInjectionMethods.InvokeTwiceAndSum))!;
        MethodBase tailInjectionMethod = typeof(AroundComposeMultiInvokeTailInjectionMethods).GetMethod(nameof(AroundComposeMultiInvokeTailInjectionMethods.Double))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);
        Injection tail = new Injection(tailInjectionMethod, new InjectAt.Tail(), "test", 1);

        ComposeResult result = WrapperComposer.Compose(target, [around, tail]);
        object? value = result.Wrapper.Invoke(null, [5]);

        Assert.Equal(66, value);
    }
}
