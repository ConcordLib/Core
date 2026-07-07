using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Generators.Tests;

public sealed class GeneratorTestHarnessTests {
    [Fact]
    public void Run_CompilesSourceReferencingConcordAttributes() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new NullGenerator(),
            """
            using Concord;

            namespace Mod;

            public class Target {
                public void M() { }
            }

            [Patch(typeof(Target))]
            internal abstract class TargetPatch {
            }
            """);

        Assert.Empty(diagnostics);
        GeneratorTestHarness.AssertCompiles(output);
    }

    private sealed class NullGenerator : IIncrementalGenerator {
        public void Initialize(IncrementalGeneratorInitializationContext context) {
        }
    }
}
