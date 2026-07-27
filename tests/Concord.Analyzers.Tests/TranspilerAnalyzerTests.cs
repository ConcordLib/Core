using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class TranspilerAnalyzerTests {
    private const string TargetSource = """

        public class Shop {
            public int Total(int listed) { return listed + 5; }
        }
        """;

    [Fact]
    public async Task Transpiler_InstanceMethod_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource + TargetSource +
            """

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Transpiler, nameof(Total))]
                private System.Collections.Generic.IEnumerable<Concord.CodeInstruction> Rewrite(
                    System.Collections.Generic.IEnumerable<Concord.CodeInstruction> instructions) {
                    return instructions;
                }
            }
            """);

        Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.TranspilerMustBeStaticDiagnosticId);
    }

    [Fact]
    public async Task Transpiler_WrongReturnType_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource + TargetSource +
            """

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Transpiler, nameof(Total))]
                private static int Rewrite(
                    System.Collections.Generic.IEnumerable<Concord.CodeInstruction> instructions) {
                    return 0;
                }
            }
            """);

        Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.InvalidTranspilerSignatureDiagnosticId);
    }

    [Fact]
    public async Task Transpiler_WrongParameterType_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource + TargetSource +
            """

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Transpiler, nameof(Total))]
                private static System.Collections.Generic.IEnumerable<Concord.CodeInstruction> Rewrite(int wrong) {
                    return null;
                }
            }
            """);

        Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.InvalidTranspilerSignatureDiagnosticId);
    }

    [Fact]
    public async Task Transpiler_ValidSingleParameter_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource + TargetSource +
            """

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Transpiler, nameof(Total))]
                private static System.Collections.Generic.IEnumerable<Concord.CodeInstruction> Rewrite(
                    System.Collections.Generic.IEnumerable<Concord.CodeInstruction> instructions) {
                    return instructions;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Transpiler_ValidWithContext_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource + TargetSource +
            """

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.TranspilerFinal, nameof(Total))]
                private static System.Collections.Generic.IEnumerable<Concord.CodeInstruction> Rewrite(
                    System.Collections.Generic.IEnumerable<Concord.CodeInstruction> instructions,
                    Concord.ITranspilerContext context) {
                    return instructions;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Transpiler_ReferencingShadowField_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                private int stock = 3;
                public int Total(int listed) { return listed + stock; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Shadow]
                private static int stock;

                [Concord.Inject(Concord.At.Transpiler, nameof(Total))]
                private static System.Collections.Generic.IEnumerable<Concord.CodeInstruction> Rewrite(
                    System.Collections.Generic.IEnumerable<Concord.CodeInstruction> instructions) {
                    _ = stock;
                    return instructions;
                }
            }
            """);

        Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.TranspilerInjectedMemberAccessDiagnosticId);
    }
}
