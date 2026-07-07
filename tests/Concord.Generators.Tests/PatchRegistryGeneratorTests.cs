using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Generators.Tests;

public sealed class PatchRegistryGeneratorTests {
    [Fact]
    public void TwoDeclarations_EmitsSortedRegistryAndAssemblyAttribute() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new PatchRegistryGenerator(),
            """
            using Concord;

            namespace Mod;

            public class Target {
                public void M() { }
            }

            [Patch(typeof(Target))]
            internal abstract class ZebraPatch {
            }

            [Patch(typeof(Target))]
            internal abstract class AlphaPatch {
            }
            """);

        Assert.Empty(diagnostics);
        string generated = GeneratorTestHarness.GeneratedSource(output, "ConcordPatchRegistry");
        Assert.Contains(
            "[assembly: global::Concord.PatchRegistry(typeof(global::Concord.Generated.PatchRegistry))]",
            generated);

        int alpha = generated.IndexOf("global::Mod.AlphaPatch", StringComparison.Ordinal);
        int zebra = generated.IndexOf("global::Mod.ZebraPatch", StringComparison.Ordinal);
        Assert.True(alpha >= 0, "AlphaPatch missing from registry");
        Assert.True(zebra > alpha, "registry is not sorted by fully-qualified name");

        GeneratorTestHarness.AssertCompiles(output);
    }

    [Fact]
    public void NoDeclarations_EmitsNothing() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new PatchRegistryGenerator(),
            "internal static class Nothing { }");

        Assert.Empty(diagnostics);
        foreach (SyntaxTree tree in output.SyntaxTrees) {
            Assert.DoesNotContain("ConcordPatchRegistry", tree.FilePath);
        }
    }

    [Fact]
    public void GenericDeclaration_IsSkipped() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new PatchRegistryGenerator(),
            """
            using Concord;

            namespace Mod;

            public class Target {
                public void M() { }
            }

            [Patch(typeof(Target))]
            internal abstract class PlainPatch {
            }

            [Patch(typeof(Target))]
            internal abstract class OpenPatch<T> {
            }
            """);

        Assert.Empty(diagnostics);
        string generated = GeneratorTestHarness.GeneratedSource(output, "ConcordPatchRegistry");
        Assert.Contains("global::Mod.PlainPatch", generated);
        Assert.DoesNotContain("OpenPatch", generated);
        GeneratorTestHarness.AssertCompiles(output);
    }

    [Fact]
    public void NestedDeclaration_UsesContainingTypeQualifiedName() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new PatchRegistryGenerator(),
            """
            using Concord;

            namespace Mod;

            public class Target {
                public void M() { }
            }

            internal static class Outer {
                [Patch(typeof(Target))]
                internal abstract class InnerPatch {
                }
            }
            """);

        Assert.Empty(diagnostics);
        string generated = GeneratorTestHarness.GeneratedSource(output, "ConcordPatchRegistry");
        Assert.Contains("typeof(global::Mod.Outer.InnerPatch)", generated);
        GeneratorTestHarness.AssertCompiles(output);
    }
}
