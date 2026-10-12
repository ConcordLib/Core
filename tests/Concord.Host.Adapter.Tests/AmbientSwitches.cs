using System.Runtime.CompilerServices;

namespace Concord.Host.Adapter.Tests;

internal static class AmbientSwitches {
    [ModuleInitializer]
    internal static void Neutralize() {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.EmptyMapVariable, null);
    }
}
