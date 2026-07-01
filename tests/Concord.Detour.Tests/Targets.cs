using System.Runtime.CompilerServices;

namespace Concord.Detour.Tests;

public static class Targets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Original() {
        return 1;
    }

    public static int Replacement() {
        return 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int OriginalB() {
        return 10;
    }

    public static int ReplacementB() {
        return 20;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int OriginalC() {
        return 100;
    }

    public static int ReplacementC() {
        return 200;
    }
}
