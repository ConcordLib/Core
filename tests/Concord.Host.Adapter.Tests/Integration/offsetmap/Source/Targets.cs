using System;
using System.Runtime.CompilerServices;

namespace Concord.Waylith.OffsetMap;

public static class Targets {
    public const string Marker = "offsetmap-probe-boom";

    public static int Seed = 5;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ControlLow() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ControlHigh() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExactAtThrow() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int TailOnly() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ZeroOnly() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int EmptyMap() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int GapAroundThrow() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Baseline() {
        int v = Seed;
        v += Seed;
        if (v > 0) {
            throw new InvalidOperationException(Marker);
        }

        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int OutOfRangeNear() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int OutOfRangeFar() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int OutOfRangePairA() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int OutOfRangePairB() {
        int v = Seed;
        v += Seed;
        v ^= Seed;
        v *= Seed;
        return v;
    }
}
