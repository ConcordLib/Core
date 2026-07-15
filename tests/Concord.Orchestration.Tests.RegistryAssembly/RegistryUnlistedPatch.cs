using System.Diagnostics.CodeAnalysis;

namespace Concord.Orchestration.Tests.RegistryAssembly;

/// <summary>Declaration NOT listed in the registry; applying it proves the fallback scan ran.</summary>
[Patch]
public abstract class RegistryUnlistedPatch : RegistryTarget {
    /// <summary>Set when the injection runs.</summary>
    [SuppressMessage("Minor Code Smell", "S1104", Justification = "Public mutable field is written by the injected patch and read cross-assembly to verify the injection fired; encapsulating it would break the test.")]
    [SuppressMessage("Critical Code Smell", "S2223", Justification = "Field must stay mutable and non-const; the injected patch assigns it at runtime.")]
    public static bool Fired;

    [Inject(At.Head, nameof(RegistryTarget.Bump))]
    [SuppressMessage("Critical Code Smell", "S2696", Justification = "This injected instance method deliberately writes the static flag so the test can observe it cross-assembly.")]
    private void BumpHead() {
        Fired = true;
    }
}
