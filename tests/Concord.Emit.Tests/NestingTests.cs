using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class NestingLog {
    public static List<string> Entries = new List<string>();

    public static void Clear() {
        Entries.Clear();
    }
}

public static class NestingCounters {
    public static int Around;
    public static int Spine;

    public static void BumpAround() {
        Around++;
    }

    public static void BumpSpine() {
        Spine++;
    }
}

public static class NestingTarget {
    public static int IntStep() {
        NestingLog.Entries.Add("spine");
        return 7;
    }

    public static int IntStepWithFinally() {
        try {
            NestingLog.Entries.Add("spine");
            return 7;
        } finally {
            NestingLog.Entries.Add("finally");
        }
    }

    public static void VoidStep() {
        NestingLog.Entries.Add("spine");
    }

    public static void AllocFreeStep() {
        NestingCounters.BumpSpine();
    }

    public static int MultiReturnInFinally(int which) {
        try {
            NestingLog.Entries.Add("spine");
            if (which == 0) {
                return 7;
            }

            if (which == 1) {
                return 11;
            }

            return 13;
        } finally {
            NestingLog.Entries.Add("finally");
        }
    }

    public static int NestedTryReturns(int which) {
        try {
            NestingLog.Entries.Add("outer");
            try {
                if (which == 0) {
                    return 3;
                }
            } finally {
                NestingLog.Entries.Add("inner-finally");
            }

            if (which == 1) {
                return 5;
            }

            return 9;
        } finally {
            NestingLog.Entries.Add("outer-finally");
        }
    }
}

public static class NestingInjectionMethods {
    public static void HeadP1(ControlHandle ch) {
        NestingLog.Entries.Add("p1-pre");
    }

    public static void ReturnP2(ControlHandle<int> ch) {
        NestingLog.Entries.Add("p2-post");
    }

    public static void AroundVoid(Operation original) {
        NestingLog.Entries.Add("pre");
        original.Invoke();
        NestingLog.Entries.Add("post");
    }

    public static void AroundAllocFree(Operation original) {
        NestingCounters.BumpAround();
        original.Invoke();
        NestingCounters.BumpAround();
    }

    public static int AroundInt(Operation<int> original) {
        NestingLog.Entries.Add("pre");
        int result = original.Invoke();
        NestingLog.Entries.Add("post");
        return result * 10;
    }

    public static int AroundIntFinally(Operation<int> original) {
        NestingLog.Entries.Add("pre");
        int result = original.Invoke();
        NestingLog.Entries.Add("post");
        return result * 10;
    }

    public static int AroundMultiReturnFinally(int which, Operation<int, int> original) {
        NestingLog.Entries.Add("pre");
        int result = original.Invoke(which);
        NestingLog.Entries.Add("post");
        return result * 10;
    }

    public static int AroundNestedTryReturns(int which, Operation<int, int> original) {
        NestingLog.Entries.Add("pre");
        int result = original.Invoke(which);
        NestingLog.Entries.Add("post");
        return result * 10;
    }
}

public sealed class NestingTests {
    [Fact]
    public void Compose_MultiInjection_OrderIsHeadSpineReturn() {
        MethodBase target = typeof(NestingTarget).GetMethod(nameof(NestingTarget.IntStep))!;
        MethodBase headInjectionMethod = typeof(NestingInjectionMethods).GetMethod(nameof(NestingInjectionMethods.HeadP1))!;
        MethodBase returnInjectionMethod = typeof(NestingInjectionMethods).GetMethod(nameof(NestingInjectionMethods.ReturnP2))!;

        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "test", 0);
        Injection ret = new Injection(returnInjectionMethod, new InjectAt.Tail(), "test", 1);

        NestingLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [head, ret]);
        result.Wrapper.Invoke(null, []);

        Assert.Equal(["p1-pre", "spine", "p2-post"], NestingLog.Entries);
    }

    [Fact]
    public void Compose_AroundSplice_OrderIsPreSpinePost() {
        MethodBase target = typeof(NestingTarget).GetMethod(nameof(NestingTarget.VoidStep))!;
        MethodBase aroundInjectionMethod = typeof(NestingInjectionMethods).GetMethod(nameof(NestingInjectionMethods.AroundVoid))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);

        NestingLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [around]);
        result.Wrapper.Invoke(null, []);

        Assert.Equal(["pre", "spine", "post"], NestingLog.Entries);
    }

    [Fact]
    public void Compose_AroundSpliceNonVoidTarget_KeepsSpineResultOnStack() {
        MethodBase target = typeof(NestingTarget).GetMethod(nameof(NestingTarget.IntStep))!;
        MethodBase aroundInjectionMethod = typeof(NestingInjectionMethods).GetMethod(nameof(NestingInjectionMethods.AroundInt))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);

        NestingLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [around]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(70, value);
        Assert.Equal(["pre", "spine", "post"], NestingLog.Entries);
    }

    [Fact]
    public void Compose_AroundSpliceNonVoidTargetWithFinally_KeepsSpineResultOnStack() {
        MethodBase target = typeof(NestingTarget).GetMethod(nameof(NestingTarget.IntStepWithFinally))!;
        MethodBase aroundInjectionMethod = typeof(NestingInjectionMethods).GetMethod(nameof(NestingInjectionMethods.AroundIntFinally))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);

        NestingLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [around]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Equal(70, value);
        Assert.Equal(["pre", "spine", "finally", "post"], NestingLog.Entries);
    }

    [Theory]
    [InlineData(0, 70)]
    [InlineData(1, 110)]
    [InlineData(2, 130)]
    public void Compose_AroundSpliceMultiReturnInFinally_RoutesEachReturnThroughSplice(int which, int expected) {
        MethodBase target = typeof(NestingTarget).GetMethod(nameof(NestingTarget.MultiReturnInFinally))!;
        MethodBase aroundInjectionMethod = typeof(NestingInjectionMethods).GetMethod(nameof(NestingInjectionMethods.AroundMultiReturnFinally))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);

        NestingLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [around]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
        Assert.Equal(["pre", "spine", "finally", "post"], NestingLog.Entries);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 50)]
    [InlineData(2, 90)]
    public void Compose_AroundSpliceNestedTryReturns_RoutesEachReturnThroughSplice(int which, int expected) {
        MethodBase target = typeof(NestingTarget).GetMethod(nameof(NestingTarget.NestedTryReturns))!;
        MethodBase aroundInjectionMethod = typeof(NestingInjectionMethods).GetMethod(nameof(NestingInjectionMethods.AroundNestedTryReturns))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [around]);
        object? value = result.Wrapper.Invoke(null, [which]);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Compose_MultipleAround_ThrowsCONC051() {
        MethodBase target = typeof(NestingTarget).GetMethod(nameof(NestingTarget.VoidStep))!;
        MethodBase aroundInjectionMethod = typeof(NestingInjectionMethods).GetMethod(nameof(NestingInjectionMethods.AroundVoid))!;

        Injection around1 = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);
        Injection around2 = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 1);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [around1, around2]));

        Assert.Equal("CONC051", ex.Code);
    }

    [Fact]
    public void Compose_AroundSplice_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(NestingTarget).GetMethod(nameof(NestingTarget.AllocFreeStep))!;
        MethodBase aroundInjectionMethod = typeof(NestingInjectionMethods).GetMethod(nameof(NestingInjectionMethods.AroundAllocFree))!;

        Injection around = new Injection(aroundInjectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [around]);
        Action invoke = result.Wrapper.CreateDelegate<Action>();
        invoke();

        long before = TestPolyfills.GetAllocatedBytes();
        invoke();
        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }
}
