namespace Concord.Orchestration.Tests.RegistryAssembly;

/// <summary>Declaration NOT listed in the registry; applying it proves the fallback scan ran.</summary>
[Patch]
public abstract class RegistryUnlistedPatch : RegistryTarget {
    /// <summary>Set when the injection runs.</summary>
    public static bool Fired;

    [Inject(At.Head, nameof(RegistryTarget.Bump))]
    private void BumpHead() {
        Fired = true;
    }
}
