using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class AroundCtorTarget {
    public int Seed;

    public AroundCtorTarget(int seed) {
        Seed = seed;
        AroundCtorLog.Entries.Add("body:" + seed);
    }
}

public static class AroundCtorLog {
    public static List<string> Entries = new List<string>();

    public static void Clear() {
        Entries.Clear();
    }
}

public static class AroundCtorInjectionMethods {
    public static void WrapOnce(int seed, VoidOperation<int> original) {
        AroundCtorLog.Entries.Add("pre");
        original.Invoke(seed);
        AroundCtorLog.Entries.Add("post");
    }

    public static void WrapZeroInvoke(int seed, VoidOperation<int> original) {
        AroundCtorLog.Entries.Add("skip");
    }

    public static void WrapLoopInvoke(int seed, VoidOperation<int> original) {
        for (int i = 0; i < 2; i++) {
            original.Invoke(seed + i);
        }
    }

    public static void WrapTwice(int seed, VoidOperation<int> original) {
        original.Invoke(seed);
        if (AroundCtorGate.RunSecondInvoke) {
            original.Invoke(seed + 1);
        }
    }

    public static void WrapConditionallySkipped(int seed, VoidOperation<int> original) {
        if (AroundCtorGate.RunGuardedInvoke) {
            original.Invoke(seed);
        }
    }
}

public static class AroundCtorGate {
    public static bool RunSecondInvoke;
    public static bool RunGuardedInvoke;
}

public sealed class AroundCctorTarget {
    public static int Value = 1;
}

public static class AroundCctorInjectionMethods {
    public static void Wrap(Operation original) {
        original.Invoke();
    }
}

public sealed class AroundConstructorTests {
    [Fact]
    public void Around_Constructor_ExactlyOneInvoke_ConstructsOnceAndWrapsBody() {
        ConstructorInfo ctor = typeof(AroundCtorTarget).GetConstructor([typeof(int)])!;
        MethodBase injectionMethod = typeof(AroundCtorInjectionMethods).GetMethod(nameof(AroundCtorInjectionMethods.WrapOnce))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(ctor, [around]);
        IDetourHandle handle = DetourBackend.Current.Apply(ctor, result.Wrapper);

        AroundCtorLog.Clear();
        AroundCtorTarget instance = new AroundCtorTarget(5);

        Assert.Equal(5, instance.Seed);
        Assert.Equal(["pre", "body:5", "post"], AroundCtorLog.Entries);

        handle.Dispose();
    }

    [Fact]
    public void Compose_Constructor_ZeroInvokeSites_ThrowsCONC112() {
        ConstructorInfo ctor = typeof(AroundCtorTarget).GetConstructor([typeof(int)])!;
        MethodBase injectionMethod = typeof(AroundCtorInjectionMethods).GetMethod(nameof(AroundCtorInjectionMethods.WrapZeroInvoke))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(ctor, [around]));

        Assert.Equal("CONC112", ex.Code);
    }

    [Fact]
    public void Compose_Constructor_InvokeInLoop_ThrowsCONC113() {
        ConstructorInfo ctor = typeof(AroundCtorTarget).GetConstructor([typeof(int)])!;
        MethodBase injectionMethod = typeof(AroundCtorInjectionMethods).GetMethod(nameof(AroundCtorInjectionMethods.WrapLoopInvoke))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(ctor, [around]));

        Assert.Equal("CONC113", ex.Code);
    }

    [Fact]
    public void Around_Constructor_RuntimeSecondInvoke_GuardPreventsDoubleInitThenEpilogueThrows() {
        ConstructorInfo ctor = typeof(AroundCtorTarget).GetConstructor([typeof(int)])!;
        MethodBase injectionMethod = typeof(AroundCtorInjectionMethods).GetMethod(nameof(AroundCtorInjectionMethods.WrapTwice))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(ctor, [around]);
        IDetourHandle handle = DetourBackend.Current.Apply(ctor, result.Wrapper);

        AroundCtorLog.Clear();
        AroundCtorGate.RunSecondInvoke = true;

        try {
            Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(typeof(AroundCtorTarget), 7));

            Assert.Equal(["body:7"], AroundCtorLog.Entries);
        } finally {
            AroundCtorGate.RunSecondInvoke = false;
            handle.Dispose();
        }
    }

    [Fact]
    public void Around_Constructor_RuntimeSkippedInvoke_EpilogueThrowsBodyRanZeroTimes() {
        ConstructorInfo ctor = typeof(AroundCtorTarget).GetConstructor([typeof(int)])!;
        MethodBase injectionMethod = typeof(AroundCtorInjectionMethods).GetMethod(nameof(AroundCtorInjectionMethods.WrapConditionallySkipped))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(ctor, [around]);
        IDetourHandle handle = DetourBackend.Current.Apply(ctor, result.Wrapper);

        AroundCtorLog.Clear();
        AroundCtorGate.RunGuardedInvoke = false;

        try {
            Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(typeof(AroundCtorTarget), 9));

            Assert.Empty(AroundCtorLog.Entries);
        } finally {
            handle.Dispose();
        }
    }

    [Fact]
    public void Compose_StaticConstructor_ThrowsCONC114() {
        ConstructorInfo cctor = typeof(AroundCctorTarget).TypeInitializer!;
        MethodBase injectionMethod = typeof(AroundCctorInjectionMethods).GetMethod(nameof(AroundCctorInjectionMethods.Wrap))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(cctor, [around]));

        Assert.Equal("CONC114", ex.Code);
    }
}
