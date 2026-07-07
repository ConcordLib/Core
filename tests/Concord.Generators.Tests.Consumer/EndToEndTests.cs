using System.Reflection;
using Xunit;

namespace Concord.Generators.Tests.Consumer;

public sealed class EndToEndTests {
    [Fact]
    public void GeneratorEmittedRegistry_IsOnTheAssembly() {
        PatchRegistryAttribute? attribute =
            typeof(SecretCounterPatch).Assembly.GetCustomAttribute<PatchRegistryAttribute>();

        Assert.NotNull(attribute);
        Assert.True(typeof(IPatchDeclarationProvider).IsAssignableFrom(attribute!.RegistryType));
    }

    [Fact]
    public void RegistryAndShadows_ComposeEndToEnd() {
        SecretCounterPatch.Observed = -1;

        using (Patcher.Apply(typeof(SecretCounterPatch).Assembly)) {
            SecretCounter counter = new SecretCounter();

            counter.Tick();
            counter.Tick();

            // Second Tick's head injection saw hits == 1 (after the first Tick) via the shadow
            // field, plus Bump(0) returning 1 via the shadow method stub: 1 + 1.
            Assert.Equal(2, SecretCounterPatch.Observed);
            Assert.Equal(2, counter.CurrentHits());
        }
    }
}
