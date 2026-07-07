using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Concord.Generators.Tests;

internal static class GeneratorTestHarness {
    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static IReadOnlyList<MetadataReference> BuildReferences() {
        Assembly[] forceLoaded = [
            typeof(Concord.PatchAttribute).Assembly,
            typeof(Concord.InjectAttribute).Assembly,
        ];
        HashSet<string> locations = [];
        List<MetadataReference> references = [];
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Concat(forceLoaded)) {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location) && locations.Add(assembly.Location)) {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        return references;
    }

    public static (Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics) Run(IIncrementalGenerator generator, string source) {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestMod",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (output, diagnostics);
    }

    public static string GeneratedSource(Compilation output, string hintSubstring) {
        SyntaxTree? tree = null;
        foreach (SyntaxTree candidate in output.SyntaxTrees) {
            if (candidate.FilePath.Contains(hintSubstring)) {
                tree = candidate;
                break;
            }
        }

        Assert.NotNull(tree);
        return tree!.ToString();
    }

    public static void AssertCompiles(Compilation output) {
        List<Diagnostic> errors = [];
        foreach (Diagnostic diagnostic in output.GetDiagnostics()) {
            if (diagnostic.Severity == DiagnosticSeverity.Error) {
                errors.Add(diagnostic);
            }
        }

        Assert.Empty(errors);
    }
}
