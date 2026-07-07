using System.Reflection;
using Concord.Orchestration.Tests.RegistryAssembly;
using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class PatchRegistryTests {
    [Fact]
    public void TryGetRegistryDeclarations_AssemblyWithRegistry_ReturnsListedDeclarations() {
        bool found = PatchDeclarationScanner.TryGetRegistryDeclarations(
            typeof(RegistryTarget).Assembly, out IReadOnlyList<Type> declarations);

        Assert.True(found);
        Assert.Equal([typeof(RegistryListedPatch)], declarations);
    }

    [Fact]
    public void TryGetRegistryDeclarations_AssemblyWithoutRegistry_ReturnsFalse() {
        bool found = PatchDeclarationScanner.TryGetRegistryDeclarations(
            typeof(PatchRegistryTests).Assembly, out IReadOnlyList<Type> declarations);

        Assert.False(found);
        Assert.Empty(declarations);
    }

    [Fact]
    public void Apply_AssemblyWithRegistry_AppliesListedDeclarationsOnly() {
        RegistryListedPatch.Fired = false;
        RegistryUnlistedPatch.Fired = false;

        using (Patcher.Apply(typeof(RegistryTarget).Assembly)) {
            RegistryTarget target = new RegistryTarget();
            target.Bump();

            Assert.True(RegistryListedPatch.Fired);
            Assert.False(RegistryUnlistedPatch.Fired);
            Assert.Equal(1, target.Count);
        }
    }
}
