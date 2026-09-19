using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class LocalPositionAnalyzerTests {
    [Fact]
    public async Task InjectAtLocal_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                private int OnStore(int value) { return value; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    // Twin of BodyCopier.ValueParameterIndex: a bare LocalHandle<T> is a local sibling, not the
    // replaced value, so it must not be counted into the 'T M(T original)' shape.
    [Fact]
    public async Task BareHandleBesideTheValueParameter_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { long tax = listed; int doubled = listed * 2; return doubled + (int)tax; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                private int OnStore(Concord.LocalHandle<long> tax, int value) { tax.Value = 100L; return value; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SliceAtLocal_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public static class Opened {
                public static void Mark() { }
            }

            public static class Closed {
                public static void Mark() { }
            }

            public class Shop {
                public int Total(int listed) { Opened.Mark(); int doubled = listed * 2; Closed.Mark(); return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                [Concord.Slice(typeof(Opened), nameof(Opened.Mark), 1, typeof(Closed), nameof(Closed.Mark), 1)]
                private int OnStore(int value) { return value; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TwoDistinctLocalInjectionsOnOneTarget_ReportNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; string tag = "x"; return doubled + tag.Length; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                private int OnCount(int value) { return value; }

                [Concord.Inject(nameof(Total), typeof(string), Concord.LocalAccess.Load, Concord.At.Local)]
                private string OnTag(string value) { return value; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TwoLocalInjectionsDifferingOnlyByAccess_ReportNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                private int OnStore(int value) { return value; }

                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Load, Concord.At.Local)]
                private int OnLoad(int value) { return value; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    // The duplicate rule still has to bite. Two injections identical down to the selector are a
    // real double-declaration, and CONCORD010 must name the position as Local rather than Head.
    [Fact]
    public async Task TwoIdenticalLocalInjectionsOnOneTarget_ReportDuplicate() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                private int OnStore(int value) { return value; }

                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                private int OnStoreAgain(int value) { return value; }
            }
            """);

        Diagnostic duplicate = Assert.Single(diagnostics);
        Assert.Equal("CONCORD010", duplicate.Id);
        Assert.Contains("Local/0", duplicate.GetMessage());
    }

    [Fact]
    public async Task LocalInjectionsSeparatedOnlyByIndex_ReportNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int first = listed * 2; int second = first + 1; return second; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local, 0, 0, 0)]
                private int OnSlotZero(int value) { return value; }

                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local, 0, 0, 1)]
                private int OnSlotOne(int value) { return value; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task LocalInjectionsSeparatedOnlyByName_ReportNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int first = listed * 2; int second = first + 1; return second; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local, 0, 0, -1, "first")]
                private int OnFirstNamed(int value) { return value; }

                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local, 0, 0, -1, "second")]
                private int OnSecondNamed(int value) { return value; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task InjectAtLocal_WithTheWrongShape_ReportsInvalidValueInjectionShape() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                private string OnStore(int value) { return value.ToString(); }
            }
            """);

        Diagnostic shape = Assert.Single(diagnostics);
        Assert.Equal("CONCORD017", shape.Id);
        Assert.Contains("int M(int original)", shape.GetMessage());
    }

    // The sibling-local case At.Local exists for: a [Local] parameter is not part of the value shape,
    // so the one-value-parameter rule still passes with it present.
    [Fact]
    public async Task InjectAtLocal_WithASiblingLocalParameter_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { string tag = "x"; int doubled = listed * 2; return doubled + tag.Length; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local)]
                private int OnStore(int value, [Concord.Local] string tag) { return value + tag.Length; }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task InjectAtLocal_SettingTwoSelectors_ReportsConflict() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local, 0, 2, 1)]
                private int OnStore(int value) { return value; }
            }
            """);

        Diagnostic conflict = Assert.Single(diagnostics);
        Assert.Equal("CONCORD046", conflict.Id);
        Assert.Contains("Ordinal and Index", conflict.GetMessage());
    }

    [Fact]
    public async Task LocalConstructorAtAnotherPosition_ReportsInvalidLocalPosition() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Tail)]
                private int OnStore(int value) { return value; }
            }
            """);

        Diagnostic invalid = Assert.Single(diagnostics);
        Assert.Equal("CONCORD047", invalid.Id);
        Assert.Contains("is not At.Local", invalid.GetMessage());
    }

    [Fact]
    public async Task AtLocalWithoutTheLocalConstructor_ReportsInvalidLocalPosition() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int doubled = listed * 2; return doubled; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), Concord.At.Local)]
                private int OnStore(int value) { return value; }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "CONCORD047" && d.GetMessage().Contains("without its dedicated constructor form"));
    }

    [Fact]
    public async Task LocalInjectionsSeparatedOnlyByOrdinal_ReportNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource +
            """

            public class Shop {
                public int Total(int listed) { int first = listed * 2; int second = first + 1; return second; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local, 0, 1)]
                private int OnFirst(int value) { return value; }

                [Concord.Inject(nameof(Total), typeof(int), Concord.LocalAccess.Store, Concord.At.Local, 0, 2)]
                private int OnSecond(int value) { return value; }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
