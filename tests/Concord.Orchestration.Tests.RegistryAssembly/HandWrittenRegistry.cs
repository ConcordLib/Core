[assembly: Concord.PatchRegistry(typeof(Concord.Orchestration.Tests.RegistryAssembly.HandWrittenRegistry))]

namespace Concord.Orchestration.Tests.RegistryAssembly;

/// <summary>Hand-written stand-in for the generated patch registry.</summary>
public sealed class HandWrittenRegistry : IPatchDeclarationProvider {
    /// <inheritdoc />
    public IReadOnlyList<Type> Declarations { get; } = [typeof(RegistryListedPatch)];
}
