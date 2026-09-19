using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class LocalAnalyzerTests {
    [Theory]
    [InlineData("doubled = 999;")]
    [InlineData("doubled++;")]
    [InlineData("Bump(ref doubled);")]
    [InlineData("int.TryParse(\"7\", out doubled);")]
    public async Task WritingALocalParameter_ReportsCONCORD049(string write) {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Return, nameof(Total))]
                private void Peek([Concord.Local] int doubled) { WRITE }
                private static void Bump(ref int v) { v++; }
            }
            """.Replace("WRITE", write));

        Diagnostic reported = Assert.Single(diagnostics);
        Assert.Equal("CONCORD049", reported.Id);
        Assert.Contains("doubled", reported.GetMessage());
    }

    // A struct field write compiles to the same ldarga an out argument does, so it lands on the
    // by-value copy and does nothing. A reference-type field write reaches the object the target
    // itself holds, which is legitimate, so only the struct case is reported.
    //
    // The SetX row is a known gap, pinned on purpose. The check is syntax-only, so a mutating
    // instance method escapes it, and so does an aliased write such as `Point alias = spot;
    // alias.X = 1;`. Both lower to the same copy the field write does, so the runtime leaves them as
    // silent no-ops rather than corruption. Closing the gap needs a symbol-level test for a
    // non-readonly instance method on a value-type parameter.
    [Theory]
    [InlineData("Point", "spot.X = 999;", "CONCORD049")]
    [InlineData("Point", "spot.X++;", "CONCORD049")]
    [InlineData("Box", "spot.X = 999;", null)]
    [InlineData("Point", "spot.SetX(999);", null)]
    public async Task WritingAFieldThroughALocalParameter_ReportsOnlyForAStruct(string type, string write, string? expected) {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public struct Point { public int X; public void SetX(int value) { X = value; } }

            public class Box { public int X; }

            public class Shop {
                public int Total(int listed) { Point p = default; Box b = new Box(); return listed + p.X + b.X; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Return, nameof(Total))]
                private void Peek([Concord.Local] TYPE spot) { WRITE }
            }
            """.Replace("TYPE", type).Replace("WRITE", write));

        if (expected is null) {
            Assert.Empty(diagnostics);
            return;
        }

        Diagnostic reported = Assert.Single(diagnostics);
        Assert.Equal(expected, reported.Id);
        Assert.Contains("spot", reported.GetMessage());
    }

    // A LocalHandle is how an author is meant to write, so its Value assignment must not trip the
    // read-only rule.
    [Fact]
    public async Task WritingALocalHandleValue_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                private int Bump(int total) { return total + 1; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CaptureAndLocalOnOneParameter_ReportsCONCORD050() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Ledger {
                public void Record(int amount) { }
            }

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; new Ledger().Record(doubled); return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Ledger), nameof(Ledger.Record), Concord.At.Head)]
                private void Peek([Concord.Capture(1), Concord.Local] int doubled) { }
            }
            """);

        Diagnostic reported = Assert.Single(diagnostics);
        Assert.Equal("CONCORD050", reported.Id);
        Assert.Contains("doubled", reported.GetMessage());
    }

    [Fact]
    public async Task LocalParameter_IsNotReportedAsAMismatchedTargetParameter() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Return, nameof(Total))]
                private void Peek([Concord.Local] int doubled) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LocalParameter_AtFinally_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Finally, nameof(Total))]
                private void Peek([Concord.Local] int doubled) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LocalParameter_OnInvokeHeadShift_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Ledger {
                public void Record(int amount) { }
            }

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; new Ledger().Record(doubled); return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Ledger), nameof(Ledger.Record), Concord.At.Head)]
                private void Peek([Concord.Local] int doubled) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LocalParameter_WithOrdinalAndIndex_ReportsConflictingSelectors() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Return, nameof(Total))]
                private void Peek([Concord.Local(Ordinal = 1, Index = 0)] int doubled) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.ConflictingLocalSelectorDiagnosticId, diagnostic.Id);
        Assert.Contains("sets Ordinal and Index", diagnostic.GetMessage());
        Assert.Equal("Concord.Local(Ordinal = 1, Index = 0)", SpanText(diagnostic));
    }

    [Fact]
    public async Task LocalParameter_WithOrdinalOnly_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Return, nameof(Total))]
                private void Peek([Concord.Local(Ordinal = 1)] int doubled) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LocalParameter_AtHead_ReportsLocalAtHead() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Head, nameof(Total))]
                private void Peek([Concord.Local] int doubled) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.LocalAtHeadDiagnosticId, diagnostic.Id);
        Assert.Contains("at At.Head", diagnostic.GetMessage());
        Assert.Equal("doubled", SpanText(diagnostic));
    }

    [Fact]
    public async Task LocalParameter_OnInvokeAroundShift_ReportsMisplacedLocal() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Ledger {
                public void Record(int amount) { }
            }

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; new Ledger().Record(doubled); return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Ledger), nameof(Ledger.Record), Concord.At.Around)]
                private void Peek(Concord.VoidOperation<int> op, [Concord.Local] int doubled) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MisplacedLocalDiagnosticId, diagnostic.Id);

        // A call-site shift is spelled with its owning position, so this cannot be confused with a
        // whole-method Around. Twin of the runtime's WrapperComposer.PositionName.
        Assert.Contains("position 'At.Invoke/At.Around'", diagnostic.GetMessage());
        Assert.Equal("doubled", SpanText(diagnostic));
    }

    [Fact]
    public async Task LocalParameter_WithAllThreeSelectors_NamesEverySelector() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Return, nameof(Total))]
                private void Peek([Concord.Local(Ordinal = 1, Index = 0, Name = "doubled")] int doubled) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.ConflictingLocalSelectorDiagnosticId, diagnostic.Id);
        Assert.Contains("sets Ordinal, Index and Name", diagnostic.GetMessage());
    }

    [Fact]
    public async Task LocalParameter_AtTail_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Tail, nameof(Total))]
                private void Peek([Concord.Local] int doubled) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LocalParameter_OnConstructionTailShift_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Receipt {
                public Receipt(int amount) { }
            }

            public class Shop {
                public Receipt Total(int listed) { int doubled = listed * 2; return new Receipt(doubled); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.InjectNew(nameof(Total), typeof(Receipt), Concord.At.Tail)]
                private void Peek([Concord.Local] int doubled) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LocalHandle_OnInvokeTailShift_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Ledger {
                public void Record(int amount) { }
            }

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; new Ledger().Record(doubled); return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Ledger), nameof(Ledger.Record), Concord.At.Tail)]
                private void Bump([Concord.Local] Concord.LocalHandle<int> doubled) { doubled.Value += 1; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LocalHandle_AtReturn_ReportsLocalWriteAtReadOnlyPosition() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Return, nameof(Total))]
                private void Bump([Concord.Local] Concord.LocalHandle<int> doubled) { doubled.Value += 1; }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.LocalWriteAtReadOnlyPositionDiagnosticId, diagnostic.Id);
        Assert.Contains("At.Return", diagnostic.GetMessage());
    }

    [Fact]
    public async Task LocalHandle_AtTail_ReportsLocalWriteAtReadOnlyPosition() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Tail, nameof(Total))]
                private void Bump([Concord.Local] Concord.LocalHandle<int> doubled) { doubled.Value += 1; }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.LocalWriteAtReadOnlyPositionDiagnosticId, diagnostic.Id);
        Assert.Contains("At.Tail", diagnostic.GetMessage());
    }

    [Fact]
    public async Task LocalHandle_AtFinally_ReportsLocalWriteAtReadOnlyPosition() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Finally, nameof(Total))]
                private void Bump([Concord.Local] Concord.LocalHandle<int> doubled) { doubled.Value += 1; }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.LocalWriteAtReadOnlyPositionDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task LocalHandle_OnWholeMethodAround_ReportsLocalOnWholeMethodAround() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public sealed class Target {
                private int Recalculate(int add) => add;
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Around, "Recalculate")]
                private int Wrap(Concord.Operation<int, int> original, Concord.LocalHandle<int> scratch) {
                    return original.Invoke(1);
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.LocalOnWholeMethodAroundDiagnosticId, diagnostic.Id);
        Assert.Contains("LocalHandle<T>", diagnostic.GetMessage());
    }

    [Fact]
    public async Task LocalParameter_OnWholeMethodAround_ReportsLocalOnWholeMethodAround() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public sealed class Target {
                private int Recalculate(int add) => add;
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Around, "Recalculate")]
                private int Wrap(Concord.Operation<int, int> original, [Concord.Local] int scratch) {
                    return original.Invoke(1);
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.LocalOnWholeMethodAroundDiagnosticId, diagnostic.Id);
        Assert.Contains("At.Around", diagnostic.GetMessage());
        Assert.Equal("scratch", SpanText(diagnostic));
    }

    private static string SpanText(Diagnostic diagnostic) {
        return diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
    }
}
