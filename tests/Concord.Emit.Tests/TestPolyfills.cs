using System.Reflection;

namespace Concord.Emit.Tests;

internal static class TestPolyfills {
#if !NET
    static TestPolyfills() {
        AppDomain.MonitoringIsEnabled = true;
    }
#endif

    public static long GetAllocatedBytes() {
#if NET
        return GC.GetAllocatedBytesForCurrentThread();
#else
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
