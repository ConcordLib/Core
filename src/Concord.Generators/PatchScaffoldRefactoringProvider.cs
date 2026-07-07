using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Concord.Generators;

/// <summary>
///     Context-menu scaffolding for Concord patch declarations: add an injection stub, add a
///     shadow member, or convert a class into a patch declaration.
/// </summary>
[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(PatchScaffoldRefactoringProvider))]
[Shared]
public sealed class PatchScaffoldRefactoringProvider : CodeRefactoringProvider {
    private const int MaxNestedTargets = 20;

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <inheritdoc />
    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context) {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        ClassDeclarationSyntax? classDeclaration =
            root?.FindNode(context.Span).FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDeclaration is null) {
            return;
        }

        SemanticModel? model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(classDeclaration, context.CancellationToken) is not INamedTypeSymbol declaration) {
            return;
        }

        INamedTypeSymbol? target = TargetResolution.ResolveTarget(declaration, model.Compilation);
        if (target is null) {
            return;
        }

        if (TargetResolution.HasPatchAttribute(declaration)) {
            RegisterAddInjection(context, classDeclaration, target);
            RegisterAddShadow(context, classDeclaration, target);
        } else {
            context.RegisterRefactoring(CodeAction.Create(
                "Concord: convert to patch declaration",
                token => ConvertToPatchAsync(context.Document, classDeclaration, token),
                equivalenceKey: "ConcordConvertToPatch"));
        }
    }

    internal static IEnumerable<IMethodSymbol> PatchableMethods(INamedTypeSymbol target) {
        foreach (ISymbol member in target.GetMembers()) {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false } method) {
                yield return method;
            }
        }
    }

    private static void RegisterAddInjection(
        CodeRefactoringContext context, ClassDeclarationSyntax classDeclaration, INamedTypeSymbol target) {
        ImmutableArray<CodeAction>.Builder children = ImmutableArray.CreateBuilder<CodeAction>();
        foreach (IMethodSymbol method in PatchableMethods(target)) {
            if (children.Count == MaxNestedTargets) {
                break;
            }

            IMethodSymbol captured = method;
            children.Add(CodeAction.Create(
                captured.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                token => AddInjectionAsync(context.Document, classDeclaration, captured, token),
                equivalenceKey: "ConcordAddInjection:" + captured.ToDisplayString()));
        }

        if (children.Count > 0) {
            context.RegisterRefactoring(CodeAction.Create(
                "Concord: add injection…", children.ToImmutable(), isInlinable: false));
        }
    }

    private static async Task<Document> AddInjectionAsync(
        Document document, ClassDeclarationSyntax classDeclaration, IMethodSymbol targetMethod, CancellationToken token) {
        string handle = targetMethod.ReturnsVoid
            ? "global::Concord.ControlHandle"
            : "global::Concord.ControlHandle<" + targetMethod.ReturnType.ToDisplayString(TypeFormat) + ">";
        string stub =
            "[global::Concord.Inject(global::Concord.At.Head, \"" + targetMethod.Name + "\")]\n" +
            "private void " + targetMethod.Name + "Head(" + handle + " ch)\n{\n}\n";

        MemberDeclarationSyntax member = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseMemberDeclaration(stub)!
            .WithAdditionalAnnotations(Microsoft.CodeAnalysis.Formatting.Formatter.Annotation);

        DocumentEditor editor = await DocumentEditor.CreateAsync(document, token).ConfigureAwait(false);
        editor.AddMember(classDeclaration, member);
        return editor.GetChangedDocument();
    }

    private static void RegisterAddShadow(
        CodeRefactoringContext context, ClassDeclarationSyntax classDeclaration, INamedTypeSymbol target) {
        ImmutableArray<CodeAction>.Builder children = ImmutableArray.CreateBuilder<CodeAction>();
        foreach (ISymbol member in target.GetMembers()) {
            if (children.Count == MaxNestedTargets) {
                break;
            }

            bool isShadowable = member.DeclaredAccessibility == Accessibility.Private && member switch {
                IFieldSymbol { IsImplicitlyDeclared: false } => true,
                IPropertySymbol => true,
                IMethodSymbol { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false } => true,
                _ => false,
            };
            if (!isShadowable) {
                continue;
            }

            ISymbol captured = member;
            children.Add(CodeAction.Create(
                captured.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                token => AddShadowAsync(context.Document, classDeclaration, captured, token),
                equivalenceKey: "ConcordAddShadow:" + captured.ToDisplayString()));
        }

        if (children.Count > 0) {
            context.RegisterRefactoring(CodeAction.Create(
                "Concord: add shadow member…", children.ToImmutable(), isInlinable: false));
        }
    }

    private static async Task<Document> AddShadowAsync(
        Document document, ClassDeclarationSyntax classDeclaration, ISymbol member, CancellationToken token) {
        DocumentEditor editor = await DocumentEditor.CreateAsync(document, token).ConfigureAwait(false);
        SyntaxGenerator generator = editor.Generator;

        SyntaxNode attribute = generator.Attribute(
            "global::Concord.Shadow", generator.LiteralExpression(member.Name));
        editor.AddAttribute(classDeclaration, attribute);

        DeclarationModifiers modifiers = generator.GetModifiers(classDeclaration);
        if (!modifiers.IsPartial) {
            editor.SetModifiers(classDeclaration, modifiers.WithPartial(true));
        }

        return editor.GetChangedDocument();
    }

    private static async Task<Document> ConvertToPatchAsync(
        Document document, ClassDeclarationSyntax classDeclaration, CancellationToken token) {
        DocumentEditor editor = await DocumentEditor.CreateAsync(document, token).ConfigureAwait(false);
        SyntaxGenerator generator = editor.Generator;

        editor.AddAttribute(classDeclaration, generator.Attribute("global::Concord.Patch"));
        DeclarationModifiers modifiers = generator.GetModifiers(classDeclaration);
        editor.SetModifiers(classDeclaration, modifiers.WithIsAbstract(true).WithPartial(true));

        return editor.GetChangedDocument();
    }
}
