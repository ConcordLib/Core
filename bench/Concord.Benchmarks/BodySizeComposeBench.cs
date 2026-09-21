using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Concord.Emit;

namespace Concord.Benchmarks;

public static class Sink {
    [SuppressMessage("Major Code Smell", "S1104:Fields should not have public accessibility", Justification = "The injected bodies are copied into emitted IL that stores straight to this field.")]
    [SuppressMessage("Major Code Smell", "S2223:Non-constant static fields should not be visible", Justification = "The benchmark needs a writable sink the JIT cannot elide.")]
    public static int Value;
}

public static class SizedInjections {
    public static void Enter([Bound] int sectionId) {
        Sink.Value = sectionId;
    }

    public static void Exit() {
        Sink.Value++;
    }
}

[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 10)]
public class BodySizeComposeBench {
    private MethodBase target = null!;
    private Injection[] injections = null!;

    [Params("Size1", "Size25", "Size100", "Size400")]
    public string Target { get; set; } = "Size1";

    [GlobalSetup]
    public void Setup() {
        target = typeof(SizedTargets).GetMethod(Target)!;
        Injection enter = new Injection(typeof(SizedInjections).GetMethod(nameof(SizedInjections.Enter))!, new InjectAt.Head(), "bench", 0) {
            BoundArguments = new Dictionary<string, object?> { ["sectionId"] = 7 },
        };
        Injection exit = new Injection(typeof(SizedInjections).GetMethod(nameof(SizedInjections.Exit))!, new InjectAt.Finally(), "bench", 1);
        injections = [enter, exit];
    }

    [Benchmark]
    public MethodInfo ComposeHeadAndFinally() {
        return WrapperComposer.Compose(target, injections).Wrapper;
    }

    [Benchmark]
    public MethodInfo CloneOriginalBodyOnly() {
        return OriginalBody.Clone(target);
    }
}
