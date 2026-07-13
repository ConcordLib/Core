using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class ConstantArgumentAnalyzerTests {
    [Fact]
    public async Task Constant_WrongParameterType_ReportsInvalidShape() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class ShopItem {
                public float Total() { return 18f + 2f; }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(nameof(Total), 18f, Concord.At.Constant)]
                private int EjectAge(int original) {
                    return 20;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.InvalidValueInjectionShapeDiagnosticId);
        Assert.Contains("EjectAge", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Constant_CorrectShape_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class ShopItem {
                public float Total() { return 18f + 2f; }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(nameof(Total), 18f, Concord.At.Constant)]
                private float EjectAge(float original) {
                    return 20f;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Constant_TooManyParameters_ReportsInvalidShape() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class ShopItem {
                public float Total() { return 18f + 2f; }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(nameof(Total), 18f, Concord.At.Constant)]
                private float EjectAge(float original, float extra) {
                    return 20f;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.InvalidValueInjectionShapeDiagnosticId);
        Assert.Contains("EjectAge", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Argument_ExplicitIndex_CorrectShape_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class PriceRules {
                public int ApplyMarkup(int basePrice, int markup) { return basePrice + markup; }
            }

            public class ShopItem {
                public PriceRules rules = new PriceRules();
                public int Total(int listed, int markup) { return rules.ApplyMarkup(listed, markup); }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(nameof(Total), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), Concord.At.Argument, arg: 2)]
                private int Clamp(int original) {
                    return original;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Argument_InferredIndex_AmbiguousType_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class PriceRules {
                public int ApplyMarkup(int basePrice, int markup) { return basePrice + markup; }
            }

            public class ShopItem {
                public PriceRules rules = new PriceRules();
                public int Total(int listed, int markup) { return rules.ApplyMarkup(listed, markup); }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(nameof(Total), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), Concord.At.Argument)]
                private int Clamp(int original) {
                    return original;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.AmbiguousArgumentInjectionDiagnosticId);
        Assert.Contains("Clamp", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Argument_InferredIndex_UniqueType_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class PriceRules {
                public int ApplyMarkup(int basePrice, float multiplier) { return basePrice + (int)multiplier; }
            }

            public class ShopItem {
                public PriceRules rules = new PriceRules();
                public int Total(int listed, float multiplier) { return rules.ApplyMarkup(listed, multiplier); }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(nameof(Total), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), Concord.At.Argument)]
                private int Clamp(int original) {
                    return original;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Argument_CallSiteNameIsAmbiguousProperty_ReportsAccessorDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class PriceRules {
                public int Markup { get; set; }
            }

            public class ShopItem {
                public PriceRules rules = new PriceRules();
                public int Total() { return rules.Markup; }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(nameof(Total), typeof(PriceRules), "Markup", Concord.At.Argument, arg: 1)]
                private int Clamp(int original) {
                    return original;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.AmbiguousAccessorNameDiagnosticId);
        Assert.Contains("Markup", diagnostic.GetMessage());
    }

    [Fact]
    public async Task ConstantConstructor_WithNonConstantPosition_ReportsInvalidPosition() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class ShopItem {
                public float Total() { return 18f + 2f; }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(nameof(Total), 18f, Concord.At.Head)]
                private float EjectAge(float original) {
                    return 20f;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.InvalidConstantPositionDiagnosticId);
        Assert.Contains("EjectAge", diagnostic.GetMessage());
    }

    [Fact]
    public async Task ConstantConstructor_WithConstantPosition_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class ShopItem {
                public float Total() { return 18f + 2f; }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(nameof(Total), 18f, Concord.At.Constant)]
                private float EjectAge(float original) {
                    return 20f;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task GeneralConstructor_WithConstantAt_ReportsInvalidPosition() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class ShopItem {
                public float Total() { return 18f + 2f; }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(Concord.At.Constant, nameof(Total))]
                private void EjectAge() {
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.InvalidConstantPositionDiagnosticId);
        Assert.Contains("EjectAge", diagnostic.GetMessage());
    }

    [Fact]
    public async Task GeneralConstructor_WithHeadAt_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class ShopItem {
                public float Total() { return 18f + 2f; }
            }

            [Concord.Patch]
            public abstract class ShopItemPatch : ShopItem {
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                private void EjectAge() {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
