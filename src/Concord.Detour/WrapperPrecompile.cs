using System.Reflection;
using MonoMod.Core.Platforms;

namespace Concord.Detour;

internal static class WrapperPrecompile {
    private static Func<IDisposable?>? guard;

    internal static bool Enabled { get; set; }

    internal static void UseGuard(Func<IDisposable?>? scope) {
        guard = scope;
    }

    internal static IDisposable? Enter() {
        return Enabled ? guard?.Invoke() : null;
    }

    internal static void Compile(MethodBase wrapper) {
        if (!Enabled) {
            return;
        }

        PlatformTriple triple = PlatformTriple.Current;
        triple.Runtime.Compile(triple.GetIdentifiable(wrapper));
    }
}
