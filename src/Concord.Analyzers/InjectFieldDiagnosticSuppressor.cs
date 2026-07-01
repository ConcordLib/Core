using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Concord.Analyzers;

/// <summary>
///     Suppresses compiler field-use diagnostics for Concord injected field declarations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InjectFieldDiagnosticSuppressor : DiagnosticSuppressor {
    private static readonly SuppressionDescriptor NeverAssigned = new(
        "CONCORDSUP001",
        "CS0649",
        "[InjectField] fields are assigned by Concord lowering.");

    private static readonly SuppressionDescriptor NeverUsed = new(
        "CONCORDSUP002",
        "CS0169",
        "[InjectField] fields are consumed by Concord lowering.");

    private static readonly SuppressionDescriptor AssignedButNeverUsed = new(
        "CONCORDSUP003",
        "CS0414",
        "[InjectField] fields are consumed by Concord lowering.");

    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(NeverAssigned, NeverUsed, AssignedButNeverUsed);

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context) {
        foreach (Diagnostic diagnostic in context.ReportedDiagnostics) {
            SuppressionDescriptor? descriptor = DescriptorFor(diagnostic.Id);
            if (descriptor is null || !TryGetFieldSymbol(context, diagnostic, out IFieldSymbol field) || !HasInjectFieldAttribute(field)) {
                continue;
            }

            context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
        }
    }

    private static SuppressionDescriptor? DescriptorFor(string diagnosticId) {
        return diagnosticId switch {
            "CS0649" => NeverAssigned,
            "CS0169" => NeverUsed,
            "CS0414" => AssignedButNeverUsed,
            _ => null,
        };
    }

    private static bool TryGetFieldSymbol(SuppressionAnalysisContext context, Diagnostic diagnostic, out IFieldSymbol field) {
        field = null!;

        Location location = diagnostic.Location;
        if (!location.IsInSource || location.SourceTree is null) {
            return false;
        }

        SyntaxNode root = location.SourceTree.GetRoot(context.CancellationToken);
        SyntaxNode node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        VariableDeclaratorSyntax? declarator = FindDeclarator(node) ?? FindDeclarator(root.FindToken(location.SourceSpan.Start).Parent);
        if (declarator is null) {
            return false;
        }

        SemanticModel model = context.GetSemanticModel(location.SourceTree);
        if (model.GetDeclaredSymbol(declarator, context.CancellationToken) is not IFieldSymbol declaredField) {
            return false;
        }

        field = declaredField;
        return true;
    }

    private static VariableDeclaratorSyntax? FindDeclarator(SyntaxNode? node) {
        for (SyntaxNode? current = node; current is not null; current = current.Parent) {
            if (current is VariableDeclaratorSyntax declarator) {
                return declarator;
            }

            if (current is FieldDeclarationSyntax { Declaration.Variables.Count: 1 } fieldDeclaration) {
                return fieldDeclaration.Declaration.Variables[0];
            }
        }

        return null;
    }

    private static bool HasInjectFieldAttribute(IFieldSymbol field) {
        foreach (AttributeData attribute in field.GetAttributes()) {
            INamedTypeSymbol? attributeClass = attribute.AttributeClass;
            if (attributeClass?.Name == "InjectFieldAttribute" &&
                attributeClass.ContainingNamespace.ToDisplayString() == "Concord") {
                return true;
            }
        }

        return false;
    }
}
