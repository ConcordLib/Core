using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Concord.Generators;

/// <summary>
///     Generates injected-member declarations for <c>[Shadow]</c> requests on partial patch
///     declaration classes, deriving each member's exact signature from target metadata.
/// </summary>
[Generator]
public sealed class ShadowMemberGenerator : IIncrementalGenerator {
    internal static readonly DiagnosticDescriptor MemberNotFound = new DiagnosticDescriptor(
        "CONC100",
        "Shadow member not found",
        "Target type '{0}' has no member named '{1}'",
        "Concord.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor AmbiguousMember = new DiagnosticDescriptor(
        "CONC101",
        "Shadow member ambiguous",
        "Target member '{0}.{1}' has {2} overloads; disambiguate with parameter types on [Shadow]",
        "Concord.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NotPartial = new DiagnosticDescriptor(
        "CONC102",
        "Declaration class must be partial",
        "Class '{0}' uses [Shadow] and must be declared partial{1}",
        "Concord.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TargetUnresolvable = new DiagnosticDescriptor(
        "CONC103",
        "Shadow target unresolvable",
        "Target type of declaration '{0}' cannot be resolved at compile time; shadow generation skipped",
        "Concord.Generators",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsupportedMember = new DiagnosticDescriptor(
        "CONC105",
        "Shadow member kind unsupported",
        "Member '{0}.{1}' cannot be shadowed: {2}",
        "Concord.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        IncrementalValuesProvider<(INamedTypeSymbol Declaration, ClassDeclarationSyntax Syntax)> declarations =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                "Concord.ShadowAttribute",
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ((INamedTypeSymbol)ctx.TargetSymbol, (ClassDeclarationSyntax)ctx.TargetNode));

        IncrementalValueProvider<Compilation> compilation = context.CompilationProvider;

        context.RegisterSourceOutput(
            declarations.Combine(compilation),
            static (productionContext, pair) => Execute(productionContext, pair.Left.Declaration, pair.Left.Syntax, pair.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        INamedTypeSymbol declaration,
        ClassDeclarationSyntax syntax,
        Compilation compilation) {
        Location location = syntax.Identifier.GetLocation();

        if (!IsPartial(declaration)) {
            context.ReportDiagnostic(Diagnostic.Create(NotPartial, location, declaration.Name, string.Empty));
            return;
        }

        INamedTypeSymbol? target = TargetResolution.ResolveTarget(declaration, compilation);
        if (target is null) {
            context.ReportDiagnostic(Diagnostic.Create(TargetUnresolvable, location, declaration.Name));
            return;
        }

        StringBuilder members = new StringBuilder();
        foreach (AttributeData attribute in declaration.GetAttributes()) {
            if (attribute.AttributeClass?.ToDisplayString() != "Concord.ShadowAttribute") {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not string memberName) {
                continue;
            }

            ImmutableArray<TypedConstant> parameterTypes = attribute.ConstructorArguments.Length > 1
                ? attribute.ConstructorArguments[1].Values
                : ImmutableArray<TypedConstant>.Empty;

            EmitMember(context, declaration, target, memberName, parameterTypes, location, members);
        }

        if (members.Length == 0) {
            return;
        }

        string source = WrapInContainers(declaration, members.ToString());
        string hint = declaration.ToDisplayString().Replace('<', '_').Replace('>', '_') + ".Shadows.g.cs";
        context.AddSource(hint, SourceText.From(source, Encoding.UTF8));
    }

    private static bool IsPartial(INamedTypeSymbol declaration) {
        foreach (SyntaxReference reference in declaration.DeclaringSyntaxReferences) {
            if (reference.GetSyntax() is ClassDeclarationSyntax { Modifiers: { } modifiers } &&
                modifiers.Any(SyntaxKind.PartialKeyword)) {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<ISymbol> FindMembers(INamedTypeSymbol target, string name) {
        for (INamedTypeSymbol? type = target; type is not null; type = type.BaseType) {
            ImmutableArray<ISymbol> members = type.GetMembers(name);
            if (members.Length > 0) {
                return members;
            }
        }

        return ImmutableArray<ISymbol>.Empty;
    }

    private static void EmitMember(
        SourceProductionContext context,
        INamedTypeSymbol declaration,
        INamedTypeSymbol target,
        string memberName,
        ImmutableArray<TypedConstant> parameterTypes,
        Location location,
        StringBuilder members) {
        ImmutableArray<ISymbol> candidates = FindMembers(target, memberName);
        if (candidates.Length == 0) {
            context.ReportDiagnostic(Diagnostic.Create(MemberNotFound, location, target.Name, memberName));
            return;
        }

        if (candidates[0] is IFieldSymbol field) {
            string staticModifier = field.IsStatic ? "static " : string.Empty;
            members.AppendLine("    #pragma warning disable CS0649");
            members.AppendLine("    [global::Concord.InjectField(\"" + field.Name + "\")]");
            members.AppendLine("    private " + staticModifier + field.Type.ToDisplayString(TypeFormat) + " " + field.Name + ";");
            members.AppendLine("    #pragma warning restore CS0649");
            return;
        }

        EmitNonFieldMember(context, declaration, target, memberName, candidates, parameterTypes, location, members);
    }

    private static void EmitNonFieldMember(
        SourceProductionContext context,
        INamedTypeSymbol declaration,
        INamedTypeSymbol target,
        string memberName,
        ImmutableArray<ISymbol> candidates,
        ImmutableArray<TypedConstant> parameterTypes,
        Location location,
        StringBuilder members) {
        if (!declaration.IsAbstract) {
            context.ReportDiagnostic(Diagnostic.Create(
                NotPartial, location, declaration.Name, " and abstract for method/property shadows"));
            return;
        }

        if (candidates[0] is IPropertySymbol property) {
            if (property.IsStatic) {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedMember, location, target.Name, memberName, "static properties are not supported"));
                return;
            }

            if (property.IsIndexer) {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedMember, location, target.Name, memberName, "indexers are not supported"));
                return;
            }

            string accessors = (property.GetMethod is not null ? "get; " : string.Empty) +
                               (property.SetMethod is not null ? "set; " : string.Empty);
            members.AppendLine("    [global::Concord.InjectProperty(\"" + property.Name + "\")]");
            members.AppendLine(
                "    protected abstract " + property.Type.ToDisplayString(TypeFormat) + " " + property.Name +
                " { " + accessors.TrimEnd() + " }");
            return;
        }

        List<IMethodSymbol> methods = [];
        foreach (ISymbol candidate in candidates) {
            if (candidate is IMethodSymbol { MethodKind: MethodKind.Ordinary } method) {
                methods.Add(method);
            }
        }

        if (methods.Count == 0) {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedMember, location, target.Name, memberName, "only fields, properties, and ordinary methods are supported"));
            return;
        }

        IMethodSymbol? selected = SelectOverload(methods, parameterTypes);
        if (selected is null) {
            context.ReportDiagnostic(Diagnostic.Create(
                AmbiguousMember, location, target.Name, memberName, methods.Count));
            return;
        }

        if (selected.IsStatic || selected.IsGenericMethod) {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedMember, location, target.Name, memberName, "static and generic methods are not supported"));
            return;
        }

        StringBuilder signature = new StringBuilder();
        signature.Append("    protected abstract ");
        signature.Append(selected.ReturnsVoid ? "void" : selected.ReturnType.ToDisplayString(TypeFormat));
        signature.Append(' ').Append(selected.Name).Append('(');
        for (int index = 0; index < selected.Parameters.Length; index++) {
            IParameterSymbol parameter = selected.Parameters[index];
            if (index > 0) {
                signature.Append(", ");
            }

            signature.Append(RefKindPrefix(parameter.RefKind));
            signature.Append(parameter.Type.ToDisplayString(TypeFormat));
            signature.Append(' ');
            signature.Append(string.IsNullOrEmpty(parameter.Name) ? "arg" + index : parameter.Name);
        }

        signature.Append(");");

        members.AppendLine("    [global::Concord.InjectMethod(\"" + selected.Name + "\")]");
        members.AppendLine(signature.ToString());
    }

    private static string RefKindPrefix(RefKind refKind) {
        return refKind switch {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => string.Empty,
        };
    }

    private static IMethodSymbol? SelectOverload(List<IMethodSymbol> methods, ImmutableArray<TypedConstant> parameterTypes) {
        if (methods.Count == 1 && parameterTypes.IsDefaultOrEmpty) {
            return methods[0];
        }

        if (parameterTypes.IsDefaultOrEmpty) {
            return null;
        }

        foreach (IMethodSymbol method in methods) {
            if (method.Parameters.Length != parameterTypes.Length) {
                continue;
            }

            bool matches = true;
            for (int index = 0; index < parameterTypes.Length; index++) {
                if (parameterTypes[index].Value is not ITypeSymbol requested ||
                    !SymbolEqualityComparer.Default.Equals(requested, method.Parameters[index].Type)) {
                    matches = false;
                    break;
                }
            }

            if (matches) {
                return method;
            }
        }

        return null;
    }

    private static string WrapInContainers(INamedTypeSymbol declaration, string members) {
        StringBuilder source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");

        string? containingNamespace = declaration.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;
        if (containingNamespace is not null) {
            source.AppendLine("namespace " + containingNamespace + " {");
        }

        List<INamedTypeSymbol> containers = [];
        for (INamedTypeSymbol? container = declaration; container is not null; container = container.ContainingType) {
            containers.Insert(0, container);
        }

        foreach (INamedTypeSymbol container in containers) {
            string keyword = container.IsStatic ? "static partial class " : "partial class ";
            string abstractModifier = !container.IsStatic && container.IsAbstract ? "abstract " : string.Empty;
            source.AppendLine(abstractModifier + keyword + container.Name + " {");
        }

        source.Append(members);

        for (int index = 0; index < containers.Count; index++) {
            source.AppendLine("}");
        }

        if (containingNamespace is not null) {
            source.AppendLine("}");
        }

        return source.ToString();
    }
}
