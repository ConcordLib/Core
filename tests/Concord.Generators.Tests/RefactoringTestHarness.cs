using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Concord.Generators.Tests;

internal static class RefactoringTestHarness {
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

    /// <summary>
    ///     Runs the provider with the cursor at <paramref name="cursorMarker" /> (the marker text is
    ///     removed from the source before parsing).
    /// </summary>
    public static async Task<(Document Document, ImmutableArray<CodeAction> Actions)> ComputeActions(
        string source, string cursorMarker) {
        int position = source.IndexOf(cursorMarker, StringComparison.Ordinal);
        Assert.True(position >= 0, "cursor marker not found");
        string cleaned = source.Remove(position, cursorMarker.Length);

        AdhocWorkspace workspace = new AdhocWorkspace();
        Project project = workspace
            .AddProject("Consumer", LanguageNames.CSharp)
            .WithMetadataReferences(References)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest));
        Document document = project.AddDocument("Consumer.cs", cleaned);

        List<CodeAction> actions = [];
        CodeRefactoringContext context = new CodeRefactoringContext(
            document, new TextSpan(position, 0), actions.Add, CancellationToken.None);
        await new PatchScaffoldRefactoringProvider().ComputeRefactoringsAsync(context);
        return (document, [.. actions]);
    }

    public static async Task<string> ApplyAsync(Document document, CodeAction action) {
        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(CancellationToken.None);
        ApplyChangesOperation applyChanges = (ApplyChangesOperation)operations[0];
        Document changed = applyChanges.ChangedSolution.GetDocument(document.Id)!;
        SourceText text = await changed.GetTextAsync();
        return text.ToString();
    }
}
