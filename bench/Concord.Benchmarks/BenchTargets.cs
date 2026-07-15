using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Concord.Benchmarks;

public static class BenchTargets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ValueReturn(int a, int b) {
        return a + b;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string RefReturn(string s) {
        return s;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Instance(int x) {
        return x + 1;
    }

    public sealed class InstanceTarget {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute(int x) {
            return x + 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ControlHandleReturn(int x) {
        return x + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Critical Code Smell", "S1186", Justification = "Empty benchmark target body is intentional; the patch under benchmark is what runs.")]
    public static void ControlHandleCancel() { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int AroundSplice(int x, int y) {
        return x + y;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int InvokeWrap(int x) {
        return x * 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SuppressMessage("Major Code Smell", "S108", Justification = "Empty finally block is the benchmark shape under test.")]
    public static int TryFinallyTarget(int x) {
        try {
            return x + 1;
        } finally {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int StackedInjections(int x) {
        return x + 1;
    }
}
