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

    private const string RichTarget = """
        namespace Game {
            public class Pawn {
                private int mood;
                private int Mood { get => mood; set => mood = value; }
                private int Recalculate(int add) { return mood + add; }
                private int Recalculate(int add, bool clamp) { return clamp ? add : mood; }
                private string ReadOnlyName { get; } = "x";
                private static void Reset() { }
                private int this[int i] { get => mood + i; }

                public void Tick() {
                    Mood = Recalculate(1);
                    _ = ReadOnlyName;
                    Reset();
                    _ = this[0];
                }
            }
        }
        """;

    [Fact]
    public void ShadowProperty_EmitsAbstractInjectPropertyStub() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            RichTarget + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("Mood")]
                internal abstract partial class PawnPatch : Game.Pawn {
                }
            }
            """);

        Assert.Empty(diagnostics);
        string generated = GeneratorTestHarness.GeneratedSource(output, "Shadows");
        Assert.Contains("[global::Concord.InjectProperty(\"Mood\")]", generated);
        Assert.Contains("protected abstract int Mood { get; set; }", generated);
        GeneratorTestHarness.AssertCompiles(output);
    }

    [Fact]
    public void ShadowGetOnlyProperty_EmitsGetOnlyStub() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            RichTarget + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("ReadOnlyName")]
                internal abstract partial class PawnPatch : Game.Pawn {
                }
            }
            """);

        Assert.Empty(diagnostics);
        string generated = GeneratorTestHarness.GeneratedSource(output, "Shadows");
        Assert.Contains("protected abstract string ReadOnlyName { get; }", generated);
        GeneratorTestHarness.AssertCompiles(output);
    }

    [Fact]
    public void ShadowUniqueMethod_ByParameterTypes_EmitsInjectMethodStub() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            RichTarget + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("Recalculate", typeof(int))]
                internal abstract partial class PawnPatch : Game.Pawn {
                }
            }
            """);

        Assert.Empty(diagnostics);
        string generated = GeneratorTestHarness.GeneratedSource(output, "Shadows");
        Assert.Contains("[global::Concord.InjectMethod(\"Recalculate\")]", generated);
        Assert.Contains("protected abstract int Recalculate(int add);", generated);
        GeneratorTestHarness.AssertCompiles(output);
    }

    [Fact]
    public void ShadowAmbiguousMethod_ReportsConc101() {
        (Compilation _, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            RichTarget + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("Recalculate")]
                internal abstract partial class PawnPatch : Game.Pawn {
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "CONC101");
    }

    [Fact]
    public void ShadowStaticMethod_ReportsConc105() {
        (Compilation _, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            RichTarget + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("Reset")]
                internal abstract partial class PawnPatch : Game.Pawn {
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "CONC105");
    }

    [Fact]
    public void ShadowIndexer_ReportsConc105() {
        (Compilation _, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            RichTarget + """

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("this[]")]
                internal abstract partial class PawnPatch : Game.Pawn {
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "CONC105");
    }

    [Fact]
    public void MethodShadowOnNonAbstractClass_ReportsConc102() {
        (Compilation _, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Run(
            new ShadowMemberGenerator(),
            RichTarget + """

            namespace Mod {
                using Concord;

                [Patch(typeof(Game.Pawn))]
                [Shadow("Mood")]
                internal partial class ConcretePatch {
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "CONC102");
    }
}
