using System;
using System.Runtime.CompilerServices;

namespace Concord.Waylith.OffsetMap;

public static class Adapted {
    public const string OwnMarker = "offsetmap-adapted-own";

    public const string HeadMarker = "offsetmap-adapted-head";

    public const string TailMarker = "offsetmap-adapted-tail";

    public static bool ArmHead;

    public static bool ArmTail;

    public static int HeadSeen;

    public static int TailSeen;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Detonate(int seed) {
        int v = seed;
        v += seed;
        v ^= seed;
        if (seed < 0) {
            throw new InvalidOperationException(OwnMarker);
        }

        return v + 1;
    }
}

[Patch(typeof(Adapted))]
public abstract class HeadPatch {
    [Inject(At.Head, nameof(Adapted.Detonate))]
    public static void BeforeDetonate(int seed) {
        Adapted.HeadSeen = Adapted.HeadSeen + 1;
        if (Adapted.ArmHead) {
            throw new NotSupportedException(Adapted.HeadMarker);
        }
    }
}

[Patch(typeof(Adapted))]
public abstract class TailPatch {
    [Inject(At.Tail, nameof(Adapted.Detonate))]
    public static void AfterDetonate(ControlHandle<int> ch) {
        Adapted.TailSeen = Adapted.TailSeen + 1;
        if (Adapted.ArmTail) {
            throw new NotImplementedException(Adapted.TailMarker);
        }
    }
}
