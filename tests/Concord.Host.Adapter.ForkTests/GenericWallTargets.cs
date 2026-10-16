using System.Runtime.CompilerServices;

namespace Concord.Host.Adapter.ForkTests;

public struct Pt {
}

public static class Box<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Bar() {
        return 1;
    }
}

public static class Crate<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Bar() {
        return 1;
    }
}

public static class Open<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Bar() {
        return 1;
    }
}

public static class GenericWallHosts {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Identity<T>(int value) {
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Wrapper<T>(int value) {
        return value + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Plain(int value) {
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CallsPlain() {
        return -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CallsConstructed() {
        return -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CallsDefinition() {
        return -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CallsForeignConstructed() {
        return -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int OpenMethod<T>() {
        return 1;
    }
}
