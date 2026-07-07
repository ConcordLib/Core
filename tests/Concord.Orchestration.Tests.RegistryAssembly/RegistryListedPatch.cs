namespace Concord.Orchestration.Tests.RegistryAssembly;

/// <summary>Declaration listed in the hand-written registry; must apply on the fast path.</summary>
[Patch]
public abstract class RegistryListedPatch : RegistryTarget {
    /// <summary>Set when the injection runs.</summary>
    public static bool Fired;

    [Inject(At.Head, nameof(RegistryTarget.Bump))]
    private void BumpHead() {
        Fired = true;
    }
}
