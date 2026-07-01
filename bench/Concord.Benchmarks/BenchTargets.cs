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
}
