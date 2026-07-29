using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class InjectionSurfaceDiagnosticsTests {
    [Fact]
    public async Task Capture_OnWholeMethodPosition_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                private void Peek([Concord.Capture(1)] int listed) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MisplacedCaptureDiagnosticId, diagnostic.Id);
        Assert.Contains("whole-method position (At.Head)", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Capture_OnConstructionAroundShift_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Widget {
                public Widget(int size) { }
            }

            public class Shop {
                public Widget Build(int size) { return new Widget(size); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.InjectNew(nameof(Build), typeof(Widget), Concord.At.Around)]
                private void Peek([Concord.Capture(1)] int size) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MisplacedCaptureDiagnosticId, diagnostic.Id);
        Assert.Contains("shift At.Around", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Capture_OnConstructionHeadShift_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Widget {
                public Widget(int size) { }
            }

            public class Shop {
                public Widget Build(int size) { return new Widget(size); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.InjectNew(nameof(Build), typeof(Widget), Concord.At.Head)]
                private void Peek([Concord.Capture(1)] int size) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Capture_OnInvokeTailShift_ReportsNothing() {
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
                [Concord.Inject(nameof(Total), typeof(Prices), nameof(Prices.Markup), Concord.At.Tail)]
                private void Peek([Concord.Capture(1)] int basePrice) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Slice_OnWholeMethodPosition_ReportsDiagnostic() {
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
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                [Concord.Slice(typeof(Prices), nameof(Prices.Markup))]
                private void Peek() { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MisplacedSliceDiagnosticId, diagnostic.Id);
        Assert.Contains("At.Head", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Slice_OnInvokePosition_ReportsNothing() {
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
                [Concord.Inject(nameof(Total), typeof(Prices), nameof(Prices.Markup), Concord.At.Head)]
                [Concord.Slice(typeof(Prices), nameof(Prices.Markup))]
                private void Peek() { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Slice_OnConstructionAroundShift_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Widget {
                public Widget(int size) { }
            }

            public class Shop {
                public Widget Build(int size) { return new Widget(size); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.InjectNew(nameof(Build), typeof(Widget), Concord.At.Around)]
                [Concord.Slice(typeof(Widget), ".ctor")]
                private void Peek() { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Capture_WithZeroArgument_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Widget {
                public Widget(int size) { }
            }

            public class Shop {
                public Widget Build(int size) { return new Widget(size); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.InjectNew(nameof(Build), typeof(Widget), Concord.At.Head)]
                private void Peek([Concord.Capture(0)] int size) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.InvalidCaptureArgumentDiagnosticId, diagnostic.Id);
        Assert.Contains("1-based", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Capture_BeyondConstructorArity_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Widget {
                public Widget(int size) { }
            }

            public class Shop {
                public Widget Build(int size) { return new Widget(size); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.InjectNew(nameof(Build), typeof(Widget), Concord.At.Head)]
                private void Peek([Concord.Capture(2)] int size) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.InvalidCaptureArgumentDiagnosticId, diagnostic.Id);
        Assert.Contains("takes 1 argument", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Capture_BeyondInvokeArity_ReportsDiagnostic() {
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
                [Concord.Inject(nameof(Total), typeof(Prices), nameof(Prices.Markup), Concord.At.Head)]
                private void Peek([Concord.Capture(2)] int basePrice) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.InvalidCaptureArgumentDiagnosticId, diagnostic.Id);
        Assert.Contains("takes 1 argument", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Capture_WithinInvokeArity_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Prices {
                public int Markup(int basePrice, int tax) { return basePrice + tax; }
            }

            public class Shop {
                public Prices prices = new Prices();
                public int Total(int listed) { return prices.Markup(listed, 2); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Prices), nameof(Prices.Markup), Concord.At.Head)]
                private void Peek([Concord.Capture(2)] int tax) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Capture_OnUnresolvedCallSite_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Widget {
                public Widget(int size) { }

                public Widget(int size, int weight) { }
            }

            public class Shop {
                public Widget Build(int size) { return new Widget(size); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.InjectNew(nameof(Build), typeof(Widget), Concord.At.Head)]
                private void Peek([Concord.Capture(9)] int size) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task State_ConflictingTypesOnSameTarget_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                private void Begin(Concord.ControlHandle<int> control) {
                    control.SetState<int>(1);
                }

                [Concord.Inject(Concord.At.Tail, nameof(Total))]
                private void Finish(Concord.ControlHandle<int> control) {
                    long carried = control.GetState<long>();
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.ConflictingStateTypeDiagnosticId, diagnostic.Id);
        Assert.Contains("int", diagnostic.GetMessage());
        Assert.Contains("long", diagnostic.GetMessage());
    }

    [Fact]
    public async Task State_ConflictingTypesInOneInjection_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                private void Begin(Concord.ControlHandle<int> control) {
                    control.SetState<int>(1);
                    control.SetState<long>(2L);
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.ConflictingStateTypeDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task State_MatchingTypesOnSameTarget_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                private void Begin(Concord.ControlHandle<int> control) {
                    control.SetState<int>(1);
                }

                [Concord.Inject(Concord.At.Tail, nameof(Total))]
                private void Finish(Concord.ControlHandle<int> control) {
                    int carried = control.GetState<int>();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task State_DifferentTypesOnDifferentTargets_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }

                public int Tax(int listed) { return listed / 10; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                private void BeginTotal(Concord.ControlHandle<int> control) {
                    control.SetState<int>(1);
                }

                [Concord.Inject(Concord.At.Tail, nameof(Total))]
                private void FinishTotal(Concord.ControlHandle<int> control) {
                    int carried = control.GetState<int>();
                }

                [Concord.Inject(Concord.At.Head, nameof(Tax))]
                private void BeginTax(Concord.ControlHandle<int> control) {
                    control.SetState<long>(1L);
                }

                [Concord.Inject(Concord.At.Tail, nameof(Tax))]
                private void FinishTax(Concord.ControlHandle<int> control) {
                    long carried = control.GetState<long>();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task State_ReadWithoutWrite_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Tail, nameof(Total))]
                private void Finish(Concord.ControlHandle<int> control) {
                    int carried = control.GetState<int>();
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.UnwrittenStateSlotDiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("SetState", diagnostic.GetMessage());
    }

    [Fact]
    public async Task State_WriteInConditionalBranch_CountsAsWrite() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                private void Begin(int listed, Concord.ControlHandle<int> control) {
                    if (listed > 0) {
                        control.SetState<int>(listed);
                    }
                }

                [Concord.Inject(Concord.At.Tail, nameof(Total))]
                private void Finish(Concord.ControlHandle<int> control) {
                    int carried = control.GetState<int>();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task State_ReadWithWriteOnAnotherTarget_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }

                public int Tax(int listed) { return listed / 10; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                private void BeginTotal(Concord.ControlHandle<int> control) {
                    control.SetState<int>(1);
                }

                [Concord.Inject(Concord.At.Tail, nameof(Tax))]
                private void FinishTax(Concord.ControlHandle<int> control) {
                    int carried = control.GetState<int>();
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.UnwrittenStateSlotDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task State_OutsideInjectionMethod_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                private static int Helper(Concord.ControlHandle<int> control) {
                    return control.GetState<int>();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
