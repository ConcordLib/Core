using System.Runtime.CompilerServices;

namespace Concord.Waylith.RimProbe;

public static class Solo {
    public const int Original = 1;

    public const int Injected = 11;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return Original;
    }
}

public static class HarmonyOnly {
    public const int Original = 2;

    public const int Injected = 21;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return Original;
    }
}

public static class ConcordFirst {
    public const int Original = 3;

    public const int Injected = 33;

    public const int HarmonyDelta = 100;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return Original;
    }
}

public static class HarmonyFirst {
    public const int Original = 4;

    public const int Injected = 44;

    public const int HarmonyDelta = 200;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return Original;
    }
}

public static class Injections {
    public const int GameInjected = 42;

    public static void SoloTail(ControlHandle<int> handle) {
        handle.ReturnValue = Solo.Injected;
    }

    public static void ConcordFirstTail(ControlHandle<int> handle) {
        handle.ReturnValue = ConcordFirst.Injected;
    }

    public static void HarmonyFirstTail(ControlHandle<int> handle) {
        handle.ReturnValue = HarmonyFirst.Injected;
    }

    public static void GameTail(ControlHandle<int> handle) {
        handle.ReturnValue = GameInjected;
    }
}

public static class HarmonyInjections {
    public static void HarmonyOnlyPostfix(ref int __result) {
        __result = HarmonyOnly.Injected;
    }

    public static void ConcordFirstPostfix(ref int __result) {
        __result = __result + ConcordFirst.HarmonyDelta;
    }

    public static void HarmonyFirstPostfix(ref int __result) {
        __result = __result + HarmonyFirst.HarmonyDelta;
    }
}
