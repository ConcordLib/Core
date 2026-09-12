using System.Reflection;
using MonoMod.Core.Platforms;

namespace Concord.Detour;

/// <summary>
///     Compiles a composed wrapper when it is composed, rather than leaving it to the runtime to compile
///     on first call.
/// </summary>
public static class WrapperPrecompile {
    private static Func<IDisposable?>? guard;

    /// <summary>
    ///     Whether a composed wrapper is compiled at compose time. Off by default: forcing the compile
    ///     makes applying a patch slower.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>
    ///     Registers a scope opened around every compile, so a host can suspend whatever makes compiling
    ///     unsafe for it.
    /// </summary>
    /// <param name="scope">Opens the scope, or null to clear.</param>
    public static void UseGuard(Func<IDisposable?>? scope) {
        guard = scope;
    }

    // runs on recompose too: a host cant bracket a wrapper it never knew was rebuilt.
    internal static void Compile(MethodBase wrapper) {
        if (!Enabled) {
            return;
        }

        IDisposable? scope = guard?.Invoke();
        try {
            PlatformTriple triple = PlatformTriple.Current;
            triple.Runtime.Compile(triple.GetIdentifiable(wrapper));
        } finally {
            scope?.Dispose();
        }
    }
}
