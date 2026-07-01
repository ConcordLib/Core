using BenchmarkDotNet.Running;
using Concord.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(BenchTargets).Assembly).Run(args);
