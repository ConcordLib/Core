using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using MonoMod.Core;

namespace Concord.Detour;

/// <summary>
///     Single entry point to MonoMod.Core, so the JIT version check is reconciled with the running JIT
///     before the first detour is created.
/// </summary>
internal static class MonoModHost {
    private const string Core100RuntimeName = "MonoMod.Core.Platforms.Runtimes.Core100Runtime";
    private const int VtableIndexGetVersionIdentifier = 2;

    private static int reconciled;

    internal static IDetourFactory Factory {
        get {
            if (Interlocked.Exchange(ref reconciled, 1) == 0) {
                ReconcileJitVersion();
            }

            return DetourFactory.Current;
        }
    }

#if NET10_0_OR_GREATER
    // MonoMod asserts the JIT reports one hardcoded guid per runtime major, and a forked CoreCLR build
    // can bump that guid. Everything MonoMod pokes is still stock-shaped, so take the JIT at its word
    // rather than losing every detour to the assert.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Vulnerability", "S6640", Justification = "ICorJitCompiler is a C++ vtable; reading its version guid needs an unmanaged function pointer and has no managed equivalent.")]
    private static unsafe void ReconcileJitVersion() {
        if (Environment.Version.Major != 10) {
            return;
        }

        nint jit = GetJitObject();
        if (jit == 0) {
            return;
        }

        Guid reported;
        ((delegate* unmanaged[Thiscall]<nint, Guid*, void>)(*(nint**)jit)[VtableIndexGetVersionIdentifier])(
            jit,
            &reported);

        FieldInfo? field = typeof(DetourFactory).Assembly.GetType(Core100RuntimeName)
            ?.GetField("JitVersionGuid", BindingFlags.NonPublic | BindingFlags.Static);
        if (field is null) {
            return;
        }

        Guid* expected = (Guid*)StaticFieldAddress(field);
        if (*expected == reported) {
            return;
        }

        Console.Error.WriteLine(
            $"[Concord] JIT reports version {reported}, MonoMod expected {*expected}. Trusting the JIT. "
            + "If this runtime also changed the ICorJitInfo vtable, detouring will crash.");
        *expected = reported;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Vulnerability", "S6640", Justification = "getJit is a native export returning a C++ object pointer; calling it needs an unmanaged function pointer.")]
    private static unsafe nint GetJitObject() {
        try {
            if (!NativeLibrary.TryLoad("clrjit", typeof(object).Assembly, null, out nint library)
                || !NativeLibrary.TryGetExport(library, "getJit", out nint getJit)) {
                return 0;
            }

            return ((delegate* unmanaged[Stdcall]<nint>)getJit)();
        } catch (DllNotFoundException) {
            return 0;
        }
    }

    // The field is initonly, so reflection refuses to set it. Its address does not move once the type
    // is initialized, and ldsflda initializes it.
    private static nint StaticFieldAddress(FieldInfo field) {
        DynamicMethod method = new DynamicMethod(
            "JitVersionGuidAddress",
            typeof(nint),
            Type.EmptyTypes,
            typeof(MonoModHost).Module,
            skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldsflda, field);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ret);
        return ((Func<nint>)method.CreateDelegate(typeof(Func<nint>)))();
    }
#else
    private static void ReconcileJitVersion() {
    }
#endif
}
