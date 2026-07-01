using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Concord.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class InjectFieldDiagnosticSuppressorTests {
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> BasicReferences = ImmutableArray.Create<MetadataReference>(
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));

    [Fact]
    public async Task InjectField_SuppressesNeverAssignedWarning() {
        const string source = """
        using System;

        namespace Concord {
            [AttributeUsage(AttributeTargets.Field)]
            public sealed class InjectFieldAttribute : Attribute {
                public InjectFieldAttribute(string targetName) {
                }
            }
        }

        public sealed class Target {
            [Concord.InjectField("configCopy")]
            private object configCopy;

            public object Read() {
                return configCopy;
            }
        }
        """;

        CSharpCompilation compilation = CreateCompilation(source);
        Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0649");

        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsWithSuppressorAsync(compilation);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0649" && !diagnostic.IsSuppressed);
    }

    [Fact]
    public async Task PlainField_DoesNotSuppressNeverAssignedWarning() {
        const string source = """
        public sealed class Target {
            private object configCopy;

            public object Read() {
                return configCopy;
            }
        }
        """;

        CSharpCompilation compilation = CreateCompilation(source);
        Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0649");

        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsWithSuppressorAsync(compilation);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CS0649" && !diagnostic.IsSuppressed);
    }

    [Fact]
    public async Task InjectField_SuppressesNeverAssignedWarningInAbstractPatch() {
        const string source = """
        using System;

        namespace Concord {
            [AttributeUsage(AttributeTargets.Field)]
            public sealed class InjectFieldAttribute : Attribute {
                public InjectFieldAttribute(string targetName) {
                }
            }
        }

        public abstract class Patch {
            [Concord.InjectField("configCopy")]
            private object configCopy;

            public object Read() {
                return configCopy;
            }
        }
        """;

        CSharpCompilation compilation = CreateCompilation(source);
        Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0649");

        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsWithSuppressorAsync(compilation);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0649" && !diagnostic.IsSuppressed);
    }

    private static CSharpCompilation CreateCompilation(string source) {
        return CSharpCompilation.Create(
            "InjectFieldConsumer",
            new[] {
                CSharpSyntaxTree.ParseText(source, ParseOptions),
            },
            BasicReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, warningLevel: 4));
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsWithSuppressorAsync(CSharpCompilation compilation) {
        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new InjectFieldDiagnosticSuppressor()));

        return await compilationWithAnalyzers.GetAllDiagnosticsAsync();
    }
}
