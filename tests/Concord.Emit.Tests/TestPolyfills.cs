using System.Reflection;

namespace Concord.Emit.Tests;

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
