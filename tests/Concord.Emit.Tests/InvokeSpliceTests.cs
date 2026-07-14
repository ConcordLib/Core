using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class InvokeLog {
    public static List<string> Entries = new List<string>();

    public static void Clear() {
        Entries.Clear();
    }
}

public static class InvokeHelper {
    public static void Step() {
        InvokeLog.Entries.Add("step");
    }

    public static void Missing() { }
}

public class InvokeTarget {
    public void Run() {
        InvokeLog.Entries.Add("spine-before");
        InvokeHelper.Step();
        InvokeLog.Entries.Add("spine-after");
    }
}

public static class InvokeAllocHelper {
    public static void Tick() { }
}

public class InvokeAllocTarget {
    public void Run() {
        InvokeAllocHelper.Tick();
    }
}

public static class InvokeInjectionMethods {
    public static void BeforeStep(ControlHandle ch) {
        InvokeLog.Entries.Add("injected");
    }

    public static void BeforeTick(ControlHandle ch) { }
}

public static class OverloadedCallSiteHelper {
    public static void Do(int x) {
        InvokeLog.Entries.Add("do-int-" + x);
    }

    public static void Do(string s) {
        InvokeLog.Entries.Add("do-str-" + s);
    }
}

public class OverloadedCallSiteTarget {
    public void Run() {
        OverloadedCallSiteHelper.Do(42);
        OverloadedCallSiteHelper.Do("hi");
    }
}

public static class OverloadedCallSiteInjectionMethods {
    public static void BeforeDoInt(ControlHandle ch) {
        InvokeLog.Entries.Add("before-int");
    }
}

public sealed class InvokeSpliceTests {
    [Fact]
    public void Compose_InvokeInjection_SplicesBeforeCallSite() {
        MethodBase target = typeof(InvokeTarget).GetMethod(nameof(InvokeTarget.Run))!;
        MethodBase injectionMethod = typeof(InvokeInjectionMethods).GetMethod(nameof(InvokeInjectionMethods.BeforeStep))!;

        Injection injection = new Injection(injectionMethod, new InjectAt.Invoke(typeof(InvokeHelper), nameof(InvokeHelper.Step), At.Head, 0), "test", 0);

        InvokeLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [injection]);

        InvokeTarget instance = new InvokeTarget();
        result.Wrapper.Invoke(null, [instance]);

        Assert.Equal(["spine-before", "injected", "step", "spine-after"], InvokeLog.Entries);
    }

    [Fact]
    public void Compose_InvokeInjection_MissingSite_ThrowsCONC031() {
        MethodBase target = typeof(InvokeTarget).GetMethod(nameof(InvokeTarget.Run))!;
        MethodBase injectionMethod = typeof(InvokeInjectionMethods).GetMethod(nameof(InvokeInjectionMethods.BeforeStep))!;

        Injection injection = new Injection(injectionMethod, new InjectAt.Invoke(typeof(InvokeHelper), nameof(InvokeHelper.Missing), At.Head, 0), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [injection]));

        Assert.Equal("CONC031", ex.Code);
    }

    [Fact]
    public void Compose_InvokeInjection_ZeroAllocOnWarmInvocation() {
        MethodBase target = typeof(InvokeAllocTarget).GetMethod(nameof(InvokeAllocTarget.Run))!;
        MethodBase injectionMethod = typeof(InvokeInjectionMethods).GetMethod(nameof(InvokeInjectionMethods.BeforeTick))!;

        Injection injection = new Injection(
            injectionMethod,
            new InjectAt.Invoke(typeof(InvokeAllocHelper), nameof(InvokeAllocHelper.Tick), At.Head, 0),
            "test",
            0);

        ComposeResult result = WrapperComposer.Compose(target, [injection]);
        InvokeAllocTarget instance = new InvokeAllocTarget();
        Action<InvokeAllocTarget> invoke = result.Wrapper.CreateDelegate<Action<InvokeAllocTarget>>();
        invoke(instance);

        long before = TestPolyfills.GetAllocatedBytes();
        for (int i = 0; i < 10; i++) {
            invoke(instance);
        }

        long after = TestPolyfills.GetAllocatedBytes();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Compose_InvokeInjection_ParameterTypes_SelectsIntOverloadOnly() {
        MethodBase target = typeof(OverloadedCallSiteTarget).GetMethod(nameof(OverloadedCallSiteTarget.Run))!;
        MethodBase injectionMethod = typeof(OverloadedCallSiteInjectionMethods).GetMethod(nameof(OverloadedCallSiteInjectionMethods.BeforeDoInt))!;

        Injection injection = new Injection(
            injectionMethod,
            new InjectAt.Invoke(typeof(OverloadedCallSiteHelper), "Do", At.Head, 0, [typeof(int)]),
            "test",
            0);

        InvokeLog.Clear();
        ComposeResult result = WrapperComposer.Compose(target, [injection]);

        OverloadedCallSiteTarget instance = new OverloadedCallSiteTarget();
        result.Wrapper.Invoke(null, [instance]);

        Assert.Equal(["before-int", "do-int-42", "do-str-hi"], InvokeLog.Entries);
    }

    [Fact]
    public void Compose_InvokeInjection_ParameterTypes_NoMatchingOverload_ThrowsCONC031() {
        MethodBase target = typeof(OverloadedCallSiteTarget).GetMethod(nameof(OverloadedCallSiteTarget.Run))!;
        MethodBase injectionMethod = typeof(OverloadedCallSiteInjectionMethods).GetMethod(nameof(OverloadedCallSiteInjectionMethods.BeforeDoInt))!;

        Injection injection = new Injection(
            injectionMethod,
            new InjectAt.Invoke(typeof(OverloadedCallSiteHelper), "Do", At.Head, 0, [typeof(float)]),
            "test",
            0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [injection]));

        Assert.Equal("CONC031", ex.Code);
    }
}
