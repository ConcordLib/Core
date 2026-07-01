using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Concord.Analyzers;

/// <summary>
///     Reports hard references from bootstrap assemblies to the Concord Assembly, runtime adapters,
///     or patching implementation dependencies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BootstrapReferenceAnalyzer : DiagnosticAnalyzer {
    /// <summary>
    ///     Diagnostic id for bootstrap assembly hard-reference violations.
    /// </summary>
    public const string DiagnosticId = "CONCORD001";

    private const string AdapterAssembliesPropertyName = "build_property.ConcordBootstrapAdapterAssemblies";
    private const string EnabledPropertyName = "build_property.ConcordBootstrapAssembly";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Bootstrap assembly must not hard-reference the Concord Assembly or runtime adapter",
        "Bootstrap assembly has a hard reference to '{0}'; use reflection or move this code into the runtime adapter",
        "Concord.Bootstrap",
        DiagnosticSeverity.Error,
        true,
        "Bootstrap assemblies can run before the Concord Assembly or runtime adapter is loadable and must not mention Concord, runtime adapter, MonoMod, or Mono.Cecil types.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
        context.RegisterSyntaxNodeAction(AnalyzeSimpleName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context) {
        if (!IsEnabled(context.Options.AnalyzerConfigOptionsProvider)) {
            return;
        }

        ImmutableHashSet<string> adapterAssemblies = AdapterAssemblies(context.Options.AnalyzerConfigOptionsProvider);
        foreach (AssemblyIdentity reference in context.Compilation.ReferencedAssemblyNames) {
            if (IsForbiddenAssembly(reference.Name, adapterAssemblies)) {
                context.ReportDiagnostic(Diagnostic.Create(Rule, Location.None, reference.Name));
            }
        }
    }

    private static void AnalyzeSimpleName(SyntaxNodeAnalysisContext context) {
        if (!IsEnabled(context.Options.AnalyzerConfigOptionsProvider) ||
            context.Node is not SimpleNameSyntax name ||
            ShouldSkipMemberAccessName(name)) {
            return;
        }

        SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
        ISymbol? symbol = symbolInfo.Symbol ?? FirstCandidate(symbolInfo);
        if (symbol == null) {
            return;
        }

        string? assemblyName = GetOwningAssemblyName(symbol);
        if (assemblyName == null ||
            !IsForbiddenAssembly(assemblyName, AdapterAssemblies(context.Options.AnalyzerConfigOptionsProvider))) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), assemblyName));
    }

    private static bool IsEnabled(AnalyzerConfigOptionsProvider optionsProvider) {
        return optionsProvider.GlobalOptions.TryGetValue(EnabledPropertyName, out string? value) &&
               bool.TryParse(value, out bool enabled) &&
               enabled;
    }

    private static ImmutableHashSet<string> AdapterAssemblies(AnalyzerConfigOptionsProvider optionsProvider) {
        if (!optionsProvider.GlobalOptions.TryGetValue(AdapterAssembliesPropertyName, out string? value)) {
            return ImmutableHashSet<string>.Empty;
        }

        ImmutableHashSet<string>.Builder assemblies = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (string segment in value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries)) {
            string assemblyName = segment.Trim();
            if (assemblyName.Length != 0) {
                assemblies.Add(assemblyName);
            }
        }

        return assemblies.ToImmutable();
    }

    private static ISymbol? FirstCandidate(SymbolInfo symbolInfo) {
        return symbolInfo.CandidateSymbols.Length == 0 ? null : symbolInfo.CandidateSymbols[0];
    }

    private static bool ShouldSkipMemberAccessName(SimpleNameSyntax name) {
        return name.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == name;
    }

    private static string? GetOwningAssemblyName(ISymbol symbol) {
        IAssemblySymbol? assembly = symbol switch {
            INamedTypeSymbol type => type.ContainingAssembly,
            IMethodSymbol method => method.ContainingType?.ContainingAssembly,
            IPropertySymbol property => property.ContainingType?.ContainingAssembly,
            IFieldSymbol field => field.ContainingType?.ContainingAssembly,
            IEventSymbol eventSymbol => eventSymbol.ContainingType?.ContainingAssembly,
            IAliasSymbol alias => GetAliasedAssembly(alias),
            _ => null,
        };

        return assembly?.Name;
    }

    private static IAssemblySymbol? GetAliasedAssembly(IAliasSymbol alias) {
        return alias.Target switch {
            INamedTypeSymbol type => type.ContainingAssembly,
            INamespaceSymbol => null,
            ISymbol symbol => GetSymbolAssembly(symbol),
            _ => null,
        };
    }

    private static IAssemblySymbol? GetSymbolAssembly(ISymbol symbol) {
        return symbol.ContainingType?.ContainingAssembly;
    }

    private static bool IsForbiddenAssembly(string assemblyName, ImmutableHashSet<string> adapterAssemblies) {
        return assemblyName == "Concord" ||
               assemblyName.StartsWith("Concord.", StringComparison.Ordinal) ||
               adapterAssemblies.Contains(assemblyName) ||
               assemblyName.StartsWith("MonoMod", StringComparison.Ordinal) ||
               assemblyName.StartsWith("Mono.Cecil", StringComparison.Ordinal);
    }
}
