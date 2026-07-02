using System.Reflection;
using BenchmarkDotNet.Attributes;
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

public static class HarmonyPrefixes {
    public static void Empty() { } // NOSONAR empty prefix is the benchmark baseline
}
