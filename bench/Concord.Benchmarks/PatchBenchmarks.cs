using System.Reflection;
using BenchmarkDotNet.Attributes;
using Concord.AttachedData;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class ValueReturnBench {
    private static readonly MethodInfo Target =
        typeof(BenchTargets).GetMethod(nameof(BenchTargets.ValueReturn))!;

    [GlobalSetup(Targets = [nameof(HarmonyPatched)])]
    public void SetupHarmony() {
        Harmony h = new Harmony("bench.harmony");
        h.Patch(Target, new HarmonyMethod(typeof(HarmonyPrefixes), nameof(HarmonyPrefixes.Empty)));
    }

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase headInjectionMethod = typeof(BenchHeads).GetMethod(nameof(BenchHeads.ValueReturnHead))!;
        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "bench", 0);
        ComposeResult result = WrapperComposer.Compose(Target, [head]);
        DetourBackend.Current.Apply(Target, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public static int Unpatched() {
        return BenchTargets.ValueReturn(2, 3);
    }

    [Benchmark]
    public static int HarmonyPatched() {
        return BenchTargets.ValueReturn(2, 3);
    }

    [Benchmark]
    public static int ConcordPatched() {
        return BenchTargets.ValueReturn(2, 3);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class RefReturnBench {
    private static readonly MethodInfo Target =
        typeof(BenchTargets).GetMethod(nameof(BenchTargets.RefReturn))!;

    [GlobalSetup(Targets = [nameof(HarmonyPatched)])]
    public void SetupHarmony() {
        Harmony h = new Harmony("bench.harmony.ref");
        h.Patch(Target, new HarmonyMethod(typeof(HarmonyPrefixes), nameof(HarmonyPrefixes.Empty)));
    }

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase headInjectionMethod = typeof(BenchHeads).GetMethod(nameof(BenchHeads.RefReturnHead))!;
        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "bench", 0);
        ComposeResult result = WrapperComposer.Compose(Target, [head]);
        DetourBackend.Current.Apply(Target, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public static string Unpatched() {
        return BenchTargets.RefReturn("hello");
    }

    [Benchmark]
    public static string HarmonyPatched() {
        return BenchTargets.RefReturn("hello");
    }

    [Benchmark]
    public static string ConcordPatched() {
        return BenchTargets.RefReturn("hello");
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class InstanceBench {
    private static readonly MethodInfo Target =
        typeof(BenchTargets).GetMethod(nameof(BenchTargets.Instance))!;

    [GlobalSetup(Targets = [nameof(HarmonyPatched)])]
    public void SetupHarmony() {
        Harmony h = new Harmony("bench.harmony.instance");
        h.Patch(Target, new HarmonyMethod(typeof(HarmonyPrefixes), nameof(HarmonyPrefixes.Empty)));
    }

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase headInjectionMethod = typeof(BenchHeads).GetMethod(nameof(BenchHeads.InstanceHead))!;
        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "bench", 0);
        ComposeResult result = WrapperComposer.Compose(Target, [head]);
        DetourBackend.Current.Apply(Target, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public int Unpatched() {
        return BenchTargets.Instance(42);
    }

    [Benchmark]
    public int HarmonyPatched() {
        return BenchTargets.Instance(42);
    }

    [Benchmark]
    public int ConcordPatched() {
        return BenchTargets.Instance(42);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class RealInstanceBench {
    private BenchTargets.InstanceTarget _target = new();
    private static readonly MethodInfo TargetMethod =
        typeof(BenchTargets.InstanceTarget).GetMethod(nameof(BenchTargets.InstanceTarget.Compute))!;

    [GlobalSetup(Targets = [nameof(HarmonyPatched)])]
    public void SetupHarmony() {
        Harmony h = new Harmony("bench.harmony.real.instance");
        h.Patch(TargetMethod, new HarmonyMethod(typeof(HarmonyPrefixes), nameof(HarmonyPrefixes.Empty)));
    }

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase headInjectionMethod = typeof(BenchHeads).GetMethod(nameof(BenchHeads.InstanceTargetHead))!;
        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "bench", 0);
        ComposeResult result = WrapperComposer.Compose(TargetMethod, [head]);
        DetourBackend.Current.Apply(TargetMethod, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public int Unpatched() {
        return _target.Compute(42);
    }

    [Benchmark]
    public int HarmonyPatched() {
        return _target.Compute(42);
    }

    [Benchmark]
    public int ConcordPatched() {
        return _target.Compute(42);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class ControlHandleReturnBench {
    private static readonly MethodInfo Target =
        typeof(BenchTargets).GetMethod(nameof(BenchTargets.ControlHandleReturn))!;

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase headInjectionMethod = typeof(BenchHeads).GetMethod(nameof(BenchHeads.ControlHandleReturnHead))!;
        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "bench", 0);
        ComposeResult result = WrapperComposer.Compose(Target, [head]);
        DetourBackend.Current.Apply(Target, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public int Unpatched() {
        return BenchTargets.ControlHandleReturn(42);
    }

    [Benchmark]
    public int ConcordPatched() {
        return BenchTargets.ControlHandleReturn(42);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class ControlHandleCancelBench {
    private static readonly MethodInfo Target =
        typeof(BenchTargets).GetMethod(nameof(BenchTargets.ControlHandleCancel))!;

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase headInjectionMethod = typeof(BenchHeads).GetMethod(nameof(BenchHeads.ControlHandleCancelHead))!;
        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "bench", 0);
        ComposeResult result = WrapperComposer.Compose(Target, [head]);
        DetourBackend.Current.Apply(Target, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public void Unpatched() {
        BenchTargets.ControlHandleCancel();
    }

    [Benchmark]
    public void ConcordPatched() {
        BenchTargets.ControlHandleCancel();
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class AroundSpliceBench {
    private static readonly MethodInfo Target =
        typeof(BenchTargets).GetMethod(nameof(BenchTargets.AroundSplice))!;

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase headInjectionMethod = typeof(BenchHeads).GetMethod(nameof(BenchHeads.AroundSpliceHead))!;
        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "bench", 0);
        ComposeResult result = WrapperComposer.Compose(Target, [head]);
        DetourBackend.Current.Apply(Target, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public int Unpatched() {
        return BenchTargets.AroundSplice(10, 20);
    }

    [Benchmark]
    public int ConcordPatched() {
        return BenchTargets.AroundSplice(10, 20);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class InvokeWrapBench {
    private static readonly MethodInfo Target =
        typeof(BenchTargets).GetMethod(nameof(BenchTargets.InvokeWrap))!;

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase invokeInjectionMethod = typeof(BenchHeads).GetMethod(nameof(BenchHeads.InvokeWrapHead))!;
        Injection invoke = new Injection(invokeInjectionMethod, new InjectAt.Head(), "bench", 0);
        ComposeResult result = WrapperComposer.Compose(Target, [invoke]);
        DetourBackend.Current.Apply(Target, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public int Unpatched() {
        return BenchTargets.InvokeWrap(42);
    }

    [Benchmark]
    public int ConcordPatched() {
        return BenchTargets.InvokeWrap(42);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class TryFinallyTargetBench {
    private static readonly MethodInfo Target =
        typeof(BenchTargets).GetMethod(nameof(BenchTargets.TryFinallyTarget))!;

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase headInjectionMethod = typeof(BenchHeads).GetMethod(nameof(BenchHeads.TryFinallyTargetHead))!;
        Injection head = new Injection(headInjectionMethod, new InjectAt.Head(), "bench", 0);
        ComposeResult result = WrapperComposer.Compose(Target, [head]);
        DetourBackend.Current.Apply(Target, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public int Unpatched() {
        return BenchTargets.TryFinallyTarget(42);
    }

    [Benchmark]
    public int ConcordPatched() {
        return BenchTargets.TryFinallyTarget(42);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class StackedBench {
    private static readonly MethodInfo Target =
        typeof(BenchTargets).GetMethod(nameof(BenchTargets.StackedInjections))!;

    [GlobalSetup(Targets = [nameof(ConcordPatched)])]
    public void SetupConcord() {
        MethodBase inj1Method = typeof(BenchHeads).GetMethod(nameof(BenchHeads.StackedInjectionsHead1))!;
        MethodBase inj2Method = typeof(BenchHeads).GetMethod(nameof(BenchHeads.StackedInjectionsHead2))!;
        MethodBase inj3Method = typeof(BenchHeads).GetMethod(nameof(BenchHeads.StackedInjectionsHead3))!;
        Injection inj1 = new Injection(inj1Method, new InjectAt.Head(), "bench", 0);
        Injection inj2 = new Injection(inj2Method, new InjectAt.Head(), "bench", 1);
        Injection inj3 = new Injection(inj3Method, new InjectAt.Head(), "bench", 2);
        ComposeResult result = WrapperComposer.Compose(Target, [inj1, inj2, inj3]);
        DetourBackend.Current.Apply(Target, result.Wrapper);
    }

    [Benchmark(Baseline = true)]
    public int Unpatched() {
        return BenchTargets.StackedInjections(42);
    }

    [Benchmark]
    public int ConcordPatched() {
        return BenchTargets.StackedInjections(42);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class AttachedFieldBench {
    private AttachedField<object, int> _field = null!;
    private object _target = new();

    [GlobalSetup]
    public void Setup() {
        _field = new AttachedField<object, int>();
    }

    [Benchmark(Baseline = true)]
    public int Get() {
        _field.Set(_target, 42);
        return _field.Get(_target);
    }

    [Benchmark]
    public int GetSet() {
        for (int i = 0; i < 10; i++) {
            _field.Set(_target, i);
        }
        return _field.Get(_target);
    }
}

public static class HarmonyPrefixes {
    public static void Empty() { } // NOSONAR empty prefix is the benchmark baseline
}
