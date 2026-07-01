using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Concord.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class BootstrapReferenceAnalyzerTests {
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> BasicReferences = ImmutableArray.Create<MetadataReference>(
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));

    [Fact]
    public async Task DoesNotReportWhenBootstrapPropertyIsDisabled() {
        MetadataReference concordReference = CreateReference(
            "Concord",
            """
            namespace Concord.Orchestration;

            public static class PatchDeclarationScanner {
                public static void ScanAssembly() {
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            """
            using Concord.Orchestration;

            public static class Bootstrap {
                public static void Run() {
                    PatchDeclarationScanner.ScanAssembly();
                }
            }
            """,
            false,
            concordReference);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsConcordAssemblyReferenceWhenEnabled() {
        MetadataReference concordReference = CreateReference(
            "Concord",
            """
            namespace Concord.Orchestration;

            public static class PatchDeclarationScanner {
                public static void ScanAssembly() {
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            """
            using Concord.Orchestration;

            public static class Bootstrap {
                public static void Run() {
                    PatchDeclarationScanner.ScanAssembly();
                }
            }
            """,
            true,
            concordReference);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == BootstrapReferenceAnalyzer.DiagnosticId);
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("Concord"));
    }

    [Fact]
    public async Task AllowsAdapterAssemblyReferenceWhenNotConfigured() {
        MetadataReference adapterReference = CreateReference(
            "ConcordSampleAdapter",
            """
            namespace Concord.SampleAdapter;

            public static class SampleAdapter {
                public static void Wire() {
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            """
            using Concord.SampleAdapter;

            public static class Bootstrap {
                public static void Run() {
                    SampleAdapter.Wire();
                }
            }
            """,
            true,
            adapterReference);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsConfiguredAdapterAssemblyReferenceWhenEnabled() {
        MetadataReference adapterReference = CreateReference(
            "ConcordSampleAdapter",
            """
            namespace Concord.SampleAdapter;

            public static class SampleAdapter {
                public static void Wire() {
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            """
            using Concord.SampleAdapter;

            public static class Bootstrap {
                public static void Run() {
                    SampleAdapter.Wire();
                }
            }
            """,
            true,
            "ConcordOtherAdapter; ConcordSampleAdapter",
            adapterReference);

        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("ConcordSampleAdapter"));
    }

    [Fact]
    public async Task AllowsReflectionStringNamesWhenEnabled() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            """
            public static class Bootstrap {
                public static string AdapterName => "Concord.SampleAdapter.SampleAdapter, ConcordSampleAdapter";
            }
            """,
            true);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsLocalConcordNamespaceTypesWhenEnabled() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            """
            namespace Concord.SampleAdapter;

            internal sealed class LocalBootstrapState {
            }

            public static class Bootstrap {
                public static object Create() {
                    return new LocalBootstrapState();
                }
            }
            """,
            true);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        bool enabled,
        string adapterAssemblies,
        params MetadataReference[] references) {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Bootstrap",
            new[] {
                CSharpSyntaxTree.ParseText(source, ParseOptions),
            },
            BasicReferences.AddRange(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        AnalyzerOptions options = new(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(enabled, adapterAssemblies));

        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new BootstrapReferenceAnalyzer()),
            options);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        bool enabled,
        params MetadataReference[] references) {
        return GetAnalyzerDiagnosticsAsync(source, enabled, string.Empty, references);
    }

    private static MetadataReference CreateReference(string assemblyName, string source) {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] {
                CSharpSyntaxTree.ParseText(source, ParseOptions),
            },
            BasicReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        EmitResult emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider {
        private readonly TestAnalyzerConfigOptions globalOptions;

        public TestAnalyzerConfigOptionsProvider(bool enabled, string adapterAssemblies) {
            globalOptions = new TestAnalyzerConfigOptions(enabled, adapterAssemblies);
        }

        public override AnalyzerConfigOptions GlobalOptions => globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) {
            return TestAnalyzerConfigOptions.Empty;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) {
            return TestAnalyzerConfigOptions.Empty;
        }
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions {
        public static readonly TestAnalyzerConfigOptions Empty = new(false, string.Empty);

        private readonly ImmutableDictionary<string, string> values;

        public TestAnalyzerConfigOptions(bool enabled, string adapterAssemblies) {
            values = ImmutableDictionary<string, string>.Empty
                .Add("build_property.ConcordBootstrapAssembly", enabled.ToString())
                .Add("build_property.ConcordBootstrapAdapterAssemblies", adapterAssemblies);
        }

        public override bool TryGetValue(string key, out string value) {
            if (values.TryGetValue(key, out string? found)) {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
