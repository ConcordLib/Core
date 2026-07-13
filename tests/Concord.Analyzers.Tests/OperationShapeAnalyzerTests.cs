using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class OperationShapeAnalyzerTests {
    [Fact]
    public async Task Around_OperationShapeMismatch_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Prices {
                public int Markup(int basePrice) { return basePrice + 1; }
            }

            public class Shop {
                public Prices prices = new Prices();
                public int Total(int listed) { return prices.Markup(listed); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Prices), nameof(Prices.Markup), Concord.At.Around)]
                private int Wrap(Concord.Operation<int> op) {
                    return op.Invoke();
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.OperationShapeMismatchDiagnosticId);
        Assert.Contains("Operation<int, int>", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Around_OperationShapeMatches_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Prices {
                public int Markup(int basePrice) { return basePrice + 1; }
            }

            public class Shop {
                public Prices prices = new Prices();
                public int Total(int listed) { return prices.Markup(listed); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Prices), nameof(Prices.Markup), Concord.At.Around)]
                private int Wrap(Concord.Operation<int, int> op) {
                    return op.Invoke(5);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Around_VoidCallSite_ExpectsVoidOperation() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Door {
                public void Slam(int force) { }
            }

            public class Shop {
                public Door door = new Door();
                public void Close(int force) { door.Slam(force); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Close), typeof(Door), nameof(Door.Slam), Concord.At.Around)]
                private void Wrap(Concord.Operation<int> op) {
                    op.Invoke();
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.OperationShapeMismatchDiagnosticId);
        Assert.Contains("VoidOperation<int>", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Around_VoidCallSite_VoidOperationMatches_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Door {
                public void Slam(int force) { }
            }

            public class Shop {
                public Door door = new Door();
                public void Close(int force) { door.Slam(force); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Close), typeof(Door), nameof(Door.Slam), Concord.At.Around)]
                private void Wrap(Concord.VoidOperation<int> op) {
                    op.Invoke(1);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Around_AmbiguousCallSite_DoesNotReportShapeMismatch() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Prices {
                public int Markup(int basePrice) { return basePrice + 1; }
                public int Markup(long basePrice) { return (int)basePrice + 1; }
            }

            public class Shop {
                public Prices prices = new Prices();
                public int Total(int listed) { return prices.Markup(listed); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Prices), nameof(Prices.Markup), Concord.At.Around)]
                private int Wrap(Concord.Operation<int> op) {
                    return op.Invoke();
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == InjectedMemberAnalyzer.OperationShapeMismatchDiagnosticId);
    }

    [Fact]
    public async Task Around_CallSiteNameIsAmbiguousProperty_ReportsAccessorDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Prices {
                public int Markup { get; set; }
            }

            public class Shop {
                public Prices prices = new Prices();
                public int Total(int listed) { return prices.Markup; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Prices), "Markup", Concord.At.Head)]
                private void Before() {
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.AmbiguousAccessorNameDiagnosticId);
        Assert.Contains("Markup", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Around_CallSiteNameUsesExplicitAccessor_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Prices {
                public int Markup { get; set; }
            }

            public class Shop {
                public Prices prices = new Prices();
                public int Total(int listed) { return prices.Markup; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Prices), "get_Markup", Concord.At.Head)]
                private void Before() {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
