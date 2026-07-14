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

    [Fact]
    public async Task Around_EightValueArgOperation_Accepted() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Worker {
                public int Compute(int a, int b, int c, int d, int e, int f, int g, int h) {
                    return a + b + c + d + e + f + g + h;
                }
            }

            public class Manager {
                public Worker worker = new Worker();
                public int Total(int a, int b, int c, int d, int e, int f, int g, int h) {
                    return worker.Compute(a, b, c, d, e, f, g, h);
                }
            }

            [Concord.Patch]
            public abstract class ManagerPatch : Manager {
                [Concord.Inject(nameof(Total), typeof(Worker), nameof(Worker.Compute), Concord.At.Around)]
                private int Wrap(Concord.Operation<int, int, int, int, int, int, int, int, int> op) {
                    return op.Invoke(1, 2, 3, 4, 5, 6, 7, 8);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Around_NineValueArgOperation_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Worker {
                public int Compute(int a, int b, int c, int d, int e, int f, int g, int h, int i) {
                    return a + b + c + d + e + f + g + h + i;
                }
            }

            public class Manager {
                public Worker worker = new Worker();
                public int Total(int a, int b, int c, int d, int e, int f, int g, int h, int i) {
                    return worker.Compute(a, b, c, d, e, f, g, h, i);
                }
            }

            [Concord.Patch]
            public abstract class ManagerPatch : Manager {
                [Concord.Inject(nameof(Total), typeof(Worker), nameof(Worker.Compute), Concord.At.Around)]
                private int Wrap(Concord.Operation<int, int, int, int, int, int, int, int, int> op) {
                    return op.Invoke(1, 2, 3, 4, 5, 6, 7, 8);
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == InjectedMemberAnalyzer.OperationShapeMismatchDiagnosticId);
        Assert.Contains("Operation<int, int, int, int, int, int, int, int, int", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Around_EightVoidArgVoidOperation_Accepted() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Door {
                public void Close(int a, int b, int c, int d, int e, int f, int g, int h) { }
            }

            public class Building {
                public Door door = new Door();
                public void Secure(int a, int b, int c, int d, int e, int f, int g, int h) {
                    door.Close(a, b, c, d, e, f, g, h);
                }
            }

            [Concord.Patch]
            public abstract class BuildingPatch : Building {
                [Concord.Inject(nameof(Secure), typeof(Door), nameof(Door.Close), Concord.At.Around)]
                private void Wrap(Concord.VoidOperation<int, int, int, int, int, int, int, int> op) {
                    op.Invoke(1, 2, 3, 4, 5, 6, 7, 8);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
