using System.Reflection;
using Concord;
using Xunit;

namespace Concord.Emit.Tests;

public static class AssemblyDumpTarget {
    public static int Helper() {
        return 3;
    }

    public static int Run() {
        return Helper() + 1;
    }
}

public sealed class IlDumpAssemblyTests {
    [Fact]
    public void ComposeDump_ListsEveryAssemblyTheBodyResolvesAgainst() {
        MethodBase target = typeof(AssemblyDumpTarget).GetMethod(nameof(AssemblyDumpTarget.Run))!;
        MethodBase injectionMethod = typeof(ReturnSiteInjectionMethods).GetMethod(nameof(ReturnSiteInjectionMethods.Double))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0);

        string dump = WrapperComposer.ComposeDump(target, [ret]);

        Assert.Contains("assemblies[", dump);
        Assert.Contains(typeof(AssemblyDumpTarget).Assembly.GetName().Name + " hash=", dump);
        Assert.Contains(" used ", dump);
    }
}
