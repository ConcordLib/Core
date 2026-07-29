using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

// Mono 6.12 stack-overflows the whole process on Type.IsValueType for a function-pointer type:
// RuntimeType.IsValueTypeImpl calls IsSubclassOf, which recurses forever. Reproduced standalone
// with zero Concord and zero MonoMod code - a net472 exe that reads MethodBody.LocalVariables of a
// method holding a `delegate*<int, int>` local and reads LocalType.IsValueType dies the same way.
// CoreCLR reports that same local as a nameless, non-value, non-pointer Type and is fine.
//
// The crash is a process abort, not a catchable exception, so a test that merely touches such a
// type takes the other 500-odd tests in the assembly down with it. That is why this skips rather
// than try/catches. Real .NET Framework is unaffected: the Windows CI leg runs the same net472
// assembly through `dotnet test` and keeps covering these tests.
internal sealed class SkipOnMonoFactAttribute : FactAttribute {
    public SkipOnMonoFactAttribute() {
        if (Type.GetType("Mono.Runtime") is not null) {
            Skip = "mono cannot reflect over a function-pointer local: Type.IsValueType recurses until the process stack overflows.";
        }
    }
}

internal static class TestPolyfills {
#if !NET
    private static readonly bool IsMono = Type.GetType("Mono.Runtime") is not null;

    static TestPolyfills() {
        if (!IsMono) {
            AppDomain.MonitoringIsEnabled = true;
        }
    }
#endif

    public static long GetAllocatedBytes() {
#if NET
        return GC.GetAllocatedBytesForCurrentThread();
#else
        if (IsMono) {
            return 0L;
        }

        return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
#endif
    }

#if !NET
    public static TDelegate CreateDelegate<TDelegate>(this MethodInfo method)
        where TDelegate : Delegate {
        return (TDelegate)method.CreateDelegate(typeof(TDelegate));
    }
#endif
}
