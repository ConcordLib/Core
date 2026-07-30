using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class ExtendedEnumDiagnosticsTests {
    private const string EnumSource = """

    public enum WeatherKind {
        Clear,
        Rain,
        Storm,
    }
    """;

    [Fact]
    public async Task EnumDeclarationWithInjection_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class WeatherPatch : Concord.ExtendedEnum<WeatherKind> {
                public static WeatherKind Frozen;

                [Concord.Inject(Concord.At.Head, "Anything")]
                private void Peek(Concord.ControlHandle ch) { }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.EnumDeclarationInjectionDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task EnumDeclarationWithoutInjection_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class WeatherPatch : Concord.ExtendedEnum<WeatherKind> {
                public static WeatherKind Frozen;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task EnumMemberOnInstanceField_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class WeatherPatch : Concord.ExtendedEnum<WeatherKind> {
                [Concord.EnumMember("weather.frozen")]
                public WeatherKind Frozen;
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.InvalidEnumMemberFieldDiagnosticId, diagnostic.Id);
        Assert.Contains("static", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnumMemberOnWrongType_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class WeatherPatch : Concord.ExtendedEnum<WeatherKind> {
                [Concord.EnumMember("weather.frozen")]
                public static string Frozen;
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.InvalidEnumMemberFieldDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task EnumMemberOnValidField_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class WeatherPatch : Concord.ExtendedEnum<WeatherKind> {
                [Concord.EnumMember("weather.frozen")]
                public static WeatherKind Frozen;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NonConstInitializer_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class WeatherPatch : Concord.ExtendedEnum<WeatherKind> {
                public static WeatherKind Ashfall = (WeatherKind)32;
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.NonConstEnumMemberInitializerDiagnosticId, diagnostic.Id);
        Assert.Contains("const", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConstInitializer_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class WeatherPatch : Concord.ExtendedEnum<WeatherKind> {
                public const WeatherKind Ashfall = (WeatherKind)32;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DuplicateMemberId_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class FirstPatch : Concord.ExtendedEnum<WeatherKind> {
                [Concord.EnumMember("weather.frozen")]
                public static WeatherKind Frozen;
            }

            [Concord.Patch]
            public abstract class SecondPatch : Concord.ExtendedEnum<WeatherKind> {
                [Concord.EnumMember("weather.frozen")]
                public static WeatherKind AlsoFrozen;
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.DuplicateEnumMemberIdDiagnosticId, diagnostic.Id);
        Assert.Contains("weather.frozen", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task DistinctMemberIds_ReportNothing() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class FirstPatch : Concord.ExtendedEnum<WeatherKind> {
                [Concord.EnumMember("weather.frozen")]
                public static WeatherKind Frozen;
            }

            [Concord.Patch]
            public abstract class SecondPatch : Concord.ExtendedEnum<WeatherKind> {
                [Concord.EnumMember("weather.ashfall")]
                public static WeatherKind Ashfall;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task MemberReadFromStaticConstructor_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class WeatherPatch : Concord.ExtendedEnum<WeatherKind> {
                public static WeatherKind Frozen;
                public static int Cached;

                static WeatherPatch() {
                    Cached = (int)Frozen;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.EnumMemberReadBeforeApplyDiagnosticId, diagnostic.Id);
        Assert.Contains("Frozen", diagnostic.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberReadOutsideStaticConstructor_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await GetAsync("""

            [Concord.Patch]
            public abstract class WeatherPatch : Concord.ExtendedEnum<WeatherKind> {
                public static WeatherKind Frozen;

                public static int Read() {
                    return (int)Frozen;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> GetAsync(string declaration) {
        return InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            InjectedMemberAnalyzerTests.AttributeSource + EnumSource + declaration);
    }
}
