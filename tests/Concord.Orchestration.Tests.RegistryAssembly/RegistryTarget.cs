namespace Concord.Orchestration.Tests.RegistryAssembly;

/// <summary>Detour target for the registry fast-path tests.</summary>
public class RegistryTarget {
    /// <summary>Bumped by <see cref="Bump" />.</summary>
    public int Count;

    /// <summary>The patched method.</summary>
    public void Bump() {
        Count++;
    }
}
