using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Generators.Tests;

public sealed class ShadowMemberGeneratorTests {
    // NOTE: test sources concatenate two namespaces into one file, so they MUST use
    // block-scoped namespaces (a file allows at most one file-scoped namespace).
    private const string Target = """
        namespace Game {
            public class OverlayDrawer {
                private int cachedValue;
                private static bool globalDirty;

                public void Draw() {
                    cachedValue++;
                    globalDirty = false;
                }
            }
        }
        """;

    [Fact]
    public void ShadowField_EmitsInjectFieldDeclaration() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            Target + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("cachedValue")]
                internal abstract partial class OverlayDrawerPatch : Game.OverlayDrawer {
                }
            }
            """);

        Assert.Empty(diagnostics);
        string generated = GeneratorTestHarness.GeneratedSource(output, "Shadows");
        Assert.Contains("[global::Concord.InjectField(\"cachedValue\")]", generated);
        Assert.Contains("private int cachedValue;", generated);
        GeneratorTestHarness.AssertCompiles(output);
    }

    [Fact]
    public void ShadowStaticField_EmitsStaticDeclaration() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            Target + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("globalDirty")]
                internal abstract partial class OverlayDrawerPatch : Game.OverlayDrawer {
                }
            }
            """);

        Assert.Empty(diagnostics);
        string generated = GeneratorTestHarness.GeneratedSource(output, "Shadows");
        Assert.Contains("private static bool globalDirty;", generated);
        GeneratorTestHarness.AssertCompiles(output);
    }

    [Fact]
    public void UnknownMember_ReportsConc100() {
        (Compilation _, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            Target + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("nope")]
                internal abstract partial class OverlayDrawerPatch : Game.OverlayDrawer {
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "CONC100");
    }

    [Fact]
    public void NonPartialClass_ReportsConc102() {
        (Compilation _, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            Target + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("cachedValue")]
                internal abstract class OverlayDrawerPatch : Game.OverlayDrawer {
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "CONC102");
    }

    [Fact]
    public void StringNamedUnresolvableTarget_ReportsConc103Warning() {
        (Compilation _, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            """
            namespace Mod {
                using Concord;

                [Patch("Game.Internal.NotReferenced")]
                [Shadow("cachedValue")]
                internal abstract partial class HiddenPatch {
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "CONC103" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ExplicitTypeofTarget_ResolvesWithoutBaseType() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            Target + """

            namespace Mod {
                using Concord;

                [Patch(typeof(Game.OverlayDrawer))]
                [Shadow("cachedValue")]
                internal abstract partial class DetachedPatch {
                }
            }
            """);

        Assert.Empty(diagnostics);
        string generated = GeneratorTestHarness.GeneratedSource(output, "Shadows");
        Assert.Contains("private int cachedValue;", generated);
        GeneratorTestHarness.AssertCompiles(output);
    }
}
