using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Concord.Analyzers;

/// <summary>
///     Validates Concord injected member declarations against their patch target type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InjectedMemberAnalyzer : DiagnosticAnalyzer {
    /// <summary>
    ///     Diagnostic id for injected member declarations whose target member cannot be found.
    /// </summary>
    public const string MissingMemberDiagnosticId = "CONCORD002";

    /// <summary>
    ///     Diagnostic id for injected member declarations whose target member has an incompatible shape.
    /// </summary>
    public const string MismatchedMemberDiagnosticId = "CONCORD003";

    /// <summary>
    ///     Diagnostic id for string patch targets that cannot be resolved at analysis time.
    /// </summary>
    public const string UnresolvedPatchTargetDiagnosticId = "CONCORD004";

    /// <summary>
    ///     Diagnostic id for injections whose target method cannot be found.
    /// </summary>
    public const string MissingInjectionTargetDiagnosticId = "CONCORD005";

    /// <summary>
    ///     Diagnostic id for injections whose target method is ambiguous.
    /// </summary>
    public const string AmbiguousInjectionTargetDiagnosticId = "CONCORD006";

    /// <summary>
    ///     Diagnostic id for injections whose injection method signature cannot bind to the target method.
    /// </summary>
    public const string InvalidInjectionSignatureDiagnosticId = "CONCORD007";

    /// <summary>
    ///     Diagnostic id for patch members with invalid static or instance shape.
    /// </summary>
    public const string StaticInstanceMismatchDiagnosticId = "CONCORD008";

    /// <summary>
    ///     Diagnostic id for patch fields that look like target fields but are missing [InjectField].
    /// </summary>
    public const string AttachedFieldCouldBeInjectFieldDiagnosticId = "CONCORD009";

    /// <summary>
    ///     Diagnostic id for duplicate injection declarations.
    /// </summary>
    public const string DuplicateInjectionDiagnosticId = "CONCORD010";

    /// <summary>
    ///     Diagnostic id for unsupported Concord declaration member shapes.
    /// </summary>
    public const string UnsupportedDeclarationShapeDiagnosticId = "CONCORD011";

    /// <summary>
    ///     Diagnostic id for string patch targets that can be written as typeof.
    /// </summary>
    public const string PreferTypeofPatchTargetDiagnosticId = "CONCORD012";

    /// <summary>
    ///     Diagnostic id for string member targets that can be written as nameof.
    /// </summary>
    public const string PreferNameofMemberTargetDiagnosticId = "CONCORD013";

    /// <summary>
    ///     Diagnostic id for explicit patch targets that can be expressed by inheritance.
    /// </summary>
    public const string PreferInheritedPatchTargetDiagnosticId = "CONCORD014";

    private static readonly DiagnosticDescriptor MissingMemberRule = new(
        MissingMemberDiagnosticId,
        "Injected member target was not found",
        "Injected member '{0}' could not find target member '{1}' on '{2}'",
        "Concord.Patches",
        DiagnosticSeverity.Error,
        true,
        "Injected member declarations must name a field, property, or method that exists on the patch target type.");

    private static readonly DiagnosticDescriptor MismatchedMemberRule = new(
        MismatchedMemberDiagnosticId,
        "Injected member target does not match",
        "Injected member '{0}' does not match target member '{1}' on '{2}': {3}",
        "Concord.Patches",
        DiagnosticSeverity.Error,
        true,
        "Injected member declarations must match the target member type, static-ness, return type, and signature.");

    private static readonly DiagnosticDescriptor UnresolvedPatchTargetRule = new(
        UnresolvedPatchTargetDiagnosticId,
        "Patch target could not be resolved",
        "Patch target '{0}' could not be resolved; Concord analyzers cannot validate this declaration",
        "Concord.Patches",
        DiagnosticSeverity.Warning,
        true,
        "String patch targets should resolve from source or project references so Concord analyzers can validate the declaration.");

    private static readonly DiagnosticDescriptor MissingInjectionTargetRule = new(
        MissingInjectionTargetDiagnosticId,
        "Injection target was not found",
        "[Inject] declaration '{0}' could not find target '{1}' on '{2}'",
        "Concord.Patches",
        DiagnosticSeverity.Error,
        true,
        "Injections must name a target method or constructor that exists on the patch target type.");

    private static readonly DiagnosticDescriptor AmbiguousInjectionTargetRule = new(
        AmbiguousInjectionTargetDiagnosticId,
        "Injection target is ambiguous",
        "[Inject] declaration '{0}' target '{1}' on '{2}' is ambiguous; specify parameterTypes",
        "Concord.Patches",
        DiagnosticSeverity.Error,
        true,
        "Overloaded injection targets must be disambiguated with parameterTypes.");

    private static readonly DiagnosticDescriptor InvalidInjectionSignatureRule = new(
        InvalidInjectionSignatureDiagnosticId,
        "Injection signature is invalid",
        "[Inject] declaration '{0}' does not match target '{1}' on '{2}': {3}",
        "Concord.Patches",
        DiagnosticSeverity.Error,
        true,
        "Injection method parameters must bind to target parameters by name and type, and ControlHandle<T> must match the target return type.");

    private static readonly DiagnosticDescriptor StaticInstanceMismatchRule = new(
        StaticInstanceMismatchDiagnosticId,
        "Patch member static-ness is invalid",
        "Patch member '{0}' does not match target '{1}' on '{2}': {3}",
        "Concord.Patches",
        DiagnosticSeverity.Error,
        true,
        "Static target methods cannot use instance declaration members or injected target instances.");

    private static readonly DiagnosticDescriptor AttachedFieldCouldBeInjectFieldRule = new(
        AttachedFieldCouldBeInjectFieldDiagnosticId,
        "Patch field matches a target field but is not [InjectField]",
        "Patch field '{0}' matches a target field on '{1}' and will become attached data; add [InjectField] if it should access the target field",
        "Concord.Patches",
        DiagnosticSeverity.Warning,
        true,
        "Plain fields on patch declarations become attached data. Use [InjectField] when the field is intended to access a real target field.");

    private static readonly DiagnosticDescriptor DuplicateInjectionRule = new(
        DuplicateInjectionDiagnosticId,
        "Duplicate injection declaration",
        "[Inject] declaration '{0}' duplicates injection target '{1}' on '{2}' at '{3}'",
        "Concord.Patches",
        DiagnosticSeverity.Warning,
        true,
        "Duplicate injection declarations at the same target and position are usually accidental.");

    private static readonly DiagnosticDescriptor UnsupportedDeclarationShapeRule = new(
        UnsupportedDeclarationShapeDiagnosticId,
        "Unsupported Concord declaration member",
        "Patch member '{0}' uses unsupported Concord declaration shape: {1}",
        "Concord.Patches",
        DiagnosticSeverity.Error,
        true,
        "Concord declaration members must use supported declaration shapes.");

    private static readonly DiagnosticDescriptor PreferTypeofPatchTargetRule = new(
        PreferTypeofPatchTargetDiagnosticId,
        "Patch target should use typeof",
        "Patch target '{0}' is available at compile time; use typeof({0}) instead of a string target",
        "Concord.Patches",
        DiagnosticSeverity.Warning,
        true,
        "String patch targets are intended for late-bound or inaccessible types. Use typeof when the target type is available at compile time.");

    private static readonly DiagnosticDescriptor PreferNameofMemberTargetRule = new(
        PreferNameofMemberTargetDiagnosticId,
        "Member target should use nameof",
        "Target member '{0}' on '{1}' is available at compile time; use nameof(...) instead of a string literal",
        "Concord.Patches",
        DiagnosticSeverity.Warning,
        true,
        "String member targets are intended for inaccessible members. Use nameof when the target member is available at compile time.");

    private static readonly DiagnosticDescriptor PreferInheritedPatchTargetRule = new(
        PreferInheritedPatchTargetDiagnosticId,
        "Patch target should be inherited",
        "Patch target '{0}' can be inherited; derive the patch declaration from '{0}' and use [Patch] instead of [Patch(typeof(...))]",
        "Concord.Patches",
        DiagnosticSeverity.Warning,
        true,
        "When a target type can be inherited, deriving the patch declaration from the target lets C# bind visible members directly and lets Concord infer the patch target.");

    private enum MetadataMemberKind {
        Field,
        Property,
        Method,
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            MissingMemberRule,
            MismatchedMemberRule,
            UnresolvedPatchTargetRule,
            MissingInjectionTargetRule,
            AmbiguousInjectionTargetRule,
            InvalidInjectionSignatureRule,
            StaticInstanceMismatchRule,
            AttachedFieldCouldBeInjectFieldRule,
            DuplicateInjectionRule,
            UnsupportedDeclarationShapeRule,
            PreferTypeofPatchTargetRule,
            PreferNameofMemberTargetRule,
            PreferInheritedPatchTargetRule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context) {
        if (context.Symbol is not INamedTypeSymbol patchType ||
            patchType.TypeKind != TypeKind.Class) {
            return;
        }

        PatchTargetResult? patchTarget = GetPatchTarget(context.Compilation, patchType);
        if (patchTarget is null) {
            return;
        }

        if (patchTarget.UnresolvedTarget is not null) {
            ReportUnresolvedPatchTarget(context, patchTarget.PatchAttribute, patchType, patchTarget.UnresolvedTarget);
            return;
        }

        if (patchTarget.FailureReason is not null || patchTarget.TargetType is null) {
            ReportUnsupported(context, patchType, patchTarget.PatchAttribute, patchTarget.FailureReason ?? "patch target could not be resolved");
            return;
        }

        INamedTypeSymbol targetType = patchTarget.TargetType;
        AnalyzePatchTargetStyle(context, patchType, targetType, patchTarget);
        AnalyzeFields(context, patchType, targetType);
        AnalyzeProperties(context, patchType, targetType);
        AnalyzeMethods(context, patchType, targetType);
        AnalyzeInjectionMethods(context, patchType, targetType);
        AnalyzeAttachedFields(context, patchType, targetType);
        AnalyzeUnsupportedInjectionMembers(context, patchType, targetType);
    }

    private static PatchTargetResult? GetPatchTarget(Compilation compilation, INamedTypeSymbol patchType) {
        AttributeData? patchAttribute = patchType.GetAttributes().FirstOrDefault(IsPatchAttribute);
        if (patchAttribute is null) {
            return null;
        }

        if (patchAttribute.ConstructorArguments.Length == 0) {
            INamedTypeSymbol? baseType = patchType.BaseType;
            return IsObject(baseType)
                ? new PatchTargetResult(patchAttribute, null, null, "declaration has no target base type and no explicit patch target", false, false)
                : new PatchTargetResult(patchAttribute, baseType, null, null, false, false);
        }

        TypedConstant targetArgument = patchAttribute.ConstructorArguments[0];
        if (targetArgument.Kind == TypedConstantKind.Type && targetArgument.Value is INamedTypeSymbol explicitTarget) {
            return new PatchTargetResult(patchAttribute, explicitTarget, null, null, false, true);
        }

        if (targetArgument.Kind == TypedConstantKind.Primitive &&
            targetArgument.Value is string targetTypeName) {
            return TryResolveStringTarget(compilation, targetTypeName, out INamedTypeSymbol targetType)
                ? new PatchTargetResult(patchAttribute, targetType, null, null, true, false)
                : new PatchTargetResult(patchAttribute, null, targetTypeName, null, true, false);
        }

        return new PatchTargetResult(patchAttribute, null, null, "patch target argument is not a type or string", false, false);
    }

    private static bool TryResolveStringTarget(Compilation compilation, string targetTypeName, out INamedTypeSymbol targetType) {
        targetType = null!;

        string metadataName = targetTypeName.Split(',')[0].Trim();
        if (metadataName.Length == 0) {
            return false;
        }

        INamedTypeSymbol? resolved = compilation.GetTypeByMetadataName(metadataName);
        if (resolved is null) {
            return false;
        }

        targetType = resolved;
        return true;
    }

    private static void AnalyzePatchTargetStyle(
        SymbolAnalysisContext context,
        INamedTypeSymbol patchType,
        INamedTypeSymbol targetType,
        PatchTargetResult patchTarget) {
        if (patchTarget.UsesStringTarget &&
            context.Compilation.IsSymbolAccessibleWithin(targetType, patchType) &&
            IsStringLiteralConstructorArgument(patchTarget.PatchAttribute, "targetTypeName", context.CancellationToken, out Location? stringLocation)) {
            ReportPreferTypeofPatchTarget(context, patchTarget.PatchAttribute, patchType, targetType, stringLocation);
        }

        if (patchTarget.UsesExplicitTypeTarget && CanInheritPatchTarget(context.Compilation, patchType, targetType)) {
            ReportPreferInheritedPatchTarget(context, patchTarget.PatchAttribute, patchType, targetType);
        }
    }

    private static void AnalyzeFields(SymbolAnalysisContext context, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        foreach (IFieldSymbol field in patchType.GetMembers().OfType<IFieldSymbol>()) {
            AttributeData? injectAttribute = field.GetAttributes().FirstOrDefault(IsInjectFieldAttribute);
            if (injectAttribute is null) {
                continue;
            }

            string targetName = TargetName(injectAttribute, field.Name);
            IFieldSymbol? targetField = FindMember(targetType, targetName, type => type.GetMembers(targetName).OfType<IFieldSymbol>());
            if (targetField is null) {
                if (MetadataMemberExists(context.Compilation, targetType, targetName, MetadataMemberKind.Field)) {
                    continue;
                }

                ReportMissing(context, field, injectAttribute, targetName, targetType);
                continue;
            }

            AnalyzeMemberNameStyle(context, patchType, targetType, targetField, injectAttribute, "targetName");

            if (!SymbolEqualityComparer.Default.Equals(targetField.Type, field.Type) || targetField.IsStatic != field.IsStatic) {
                ReportMismatch(
                    context,
                    field,
                    injectAttribute,
                    targetName,
                    targetType,
                    "field type and static-ness must match exactly");
            }
        }
    }

    private static void AnalyzeInjectionMethods(SymbolAnalysisContext context, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        Dictionary<string, InjectionInfo> seen = new Dictionary<string, InjectionInfo>();

        foreach (IMethodSymbol method in patchType.GetMembers().OfType<IMethodSymbol>()) {
            AttributeData? injectAttribute = method.GetAttributes().FirstOrDefault(IsInjectAttribute);
            if (injectAttribute is null || method.MethodKind != MethodKind.Ordinary) {
                continue;
            }

            if (method.IsGenericMethod) {
                ReportUnsupported(context, method, injectAttribute, "[Inject] declarations cannot be generic");
                continue;
            }

            if (method.IsAbstract) {
                ReportUnsupported(context, method, injectAttribute, "[Inject] declarations must have a body");
                continue;
            }

            if (!TryGetInjectionInfo(method, injectAttribute, out InjectionInfo? maybeInjection)) {
                ReportUnsupported(context, method, injectAttribute, "[Inject] constructor arguments could not be read");
                continue;
            }

            InjectionInfo injection = maybeInjection!;
            if (seen.TryGetValue(injection.DuplicateKey, out InjectionInfo? first)) {
                ReportDuplicateInjection(context, injection, first, targetType);
            } else {
                seen[injection.DuplicateKey] = injection;
            }

            InjectionTarget? target = ResolveInjectionTarget(context, injection, targetType);
            if (target is null || !target.SignatureValidated) {
                continue;
            }

            if (target.IsStatic && !method.IsStatic) {
                ReportStaticInstanceMismatch(
                    context,
                    method,
                    injectAttribute,
                    target,
                    targetType,
                    "static target methods require static [Inject] injection methods");
                continue;
            }

            if (target.IsStatic && HasInjectInstanceProperty(patchType)) {
                ReportStaticInstanceMismatch(
                    context,
                    method,
                    injectAttribute,
                    target,
                    targetType,
                    "static target methods cannot use [InjectInstance]");
                continue;
            }

            ValidateInjectionSignature(context, injection, target, targetType);
        }
    }

    private static void AnalyzeAttachedFields(SymbolAnalysisContext context, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        foreach (IFieldSymbol field in patchType.GetMembers().OfType<IFieldSymbol>()) {
            if (field.IsImplicitlyDeclared ||
                field.IsConst ||
                field.GetAttributes().Any(IsInjectFieldAttribute)) {
                continue;
            }

            IFieldSymbol? targetField = FindMember(targetType, field.Name, type => type.GetMembers(field.Name).OfType<IFieldSymbol>());
            if (targetField is null) {
                continue;
            }

            if (targetField.IsStatic == field.IsStatic &&
                SymbolEqualityComparer.Default.Equals(targetField.Type, field.Type)) {
                ReportAttachedFieldCouldBeInjectField(context, field, targetType);
            }
        }
    }

    private static void AnalyzeUnsupportedInjectionMembers(SymbolAnalysisContext context, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        List<IPropertySymbol> instanceProperties = patchType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => property.GetAttributes().Any(IsInjectInstanceAttribute))
            .ToList();

        for (int i = 1; i < instanceProperties.Count; i++) {
            IPropertySymbol property = instanceProperties[i];
            ReportUnsupported(
                context,
                property,
                property.GetAttributes().First(IsInjectInstanceAttribute),
                "only one [InjectInstance] property is allowed");
        }

        foreach (IPropertySymbol property in instanceProperties) {
            AttributeData attribute = property.GetAttributes().First(IsInjectInstanceAttribute);
            if (property.GetMethod is null || property.SetMethod is not null || property.IsStatic) {
                ReportUnsupported(context, property, attribute, "[InjectInstance] must be a non-static get-only property");
                continue;
            }

            if (targetType.IsValueType) {
                ReportUnsupported(context, property, attribute, "[InjectInstance] does not support value-type patch targets");
                continue;
            }

            if (!CanAssignTargetToProperty(targetType, property.Type)) {
                ReportUnsupported(context, property, attribute, "[InjectInstance] property type must be assignable from the patch target type");
            }
        }
    }

    private static bool TryGetInjectionInfo(IMethodSymbol method, AttributeData attribute, out InjectionInfo? injection) {
        injection = null;

        bool targetsConstructor = !ConstructorHasParameter(attribute, "method");
        bool targetsInvoke = ConstructorHasParameter(attribute, "invokeDeclaringType");
        string targetName = ".ctor";
        if (!targetsConstructor) {
            if (!TryGetStringConstructorArgument(attribute, "method", out string? methodName) || string.IsNullOrWhiteSpace(methodName)) {
                return false;
            }

            targetName = methodName!;
        }

        int atValue = TryGetIntConstructorArgument(attribute, targetsInvoke ? "shift" : "at", out int parsedAtValue) ? parsedAtValue : 0;
        uint by = TryGetUIntConstructorArgument(attribute, "by", out uint parsedBy) ? parsedBy : 0;
        ImmutableArray<ITypeSymbol>? parameterTypes = TryGetTypeArrayConstructorArgument(
            attribute,
            targetsInvoke ? "targetParameterTypes" : "parameterTypes");

        injection = new InjectionInfo(
            method,
            attribute,
            targetName,
            targetsConstructor,
            targetsInvoke,
            atValue,
            by,
            parameterTypes,
            DuplicateKey(targetName, targetsConstructor, targetsInvoke, atValue, by, parameterTypes));
        return true;
    }

    private static InjectionTarget? ResolveInjectionTarget(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        INamedTypeSymbol targetType) {
        if (injection.TargetsConstructor) {
            ImmutableArray<IMethodSymbol> constructors = targetType.InstanceConstructors
                .Where(constructor => ConstructorParameterTypesMatch(injection.ParameterTypes, constructor.Parameters))
                .ToImmutableArray();

            if (constructors.Length == 0) {
                if (MetadataMemberExists(context.Compilation, targetType, ".ctor", MetadataMemberKind.Method)) {
                    return new InjectionTarget(".ctor", false, null, ImmutableArray<IParameterSymbol>.Empty, false);
                }

                ReportMissingInjectionTarget(context, injection, targetType);
                return null;
            }

            if (constructors.Length > 1) {
                ReportAmbiguousInjectionTarget(context, injection, targetType);
                return null;
            }

            IMethodSymbol constructor = constructors[0];
            return new InjectionTarget(".ctor", false, null, constructor.Parameters, true);
        }

        ImmutableArray<IMethodSymbol> candidates = FindMethodCandidates(targetType, injection.TargetName)
            .Where(method => ParameterTypesMatch(injection.ParameterTypes, method.Parameters))
            .ToImmutableArray();

        if (candidates.Length == 0) {
            if (MetadataMemberExists(context.Compilation, targetType, injection.TargetName, MetadataMemberKind.Method)) {
                return new InjectionTarget(injection.TargetName, false, null, ImmutableArray<IParameterSymbol>.Empty, false);
            }

            ReportMissingInjectionTarget(context, injection, targetType);
            return null;
        }

        if (candidates.Length > 1) {
            ReportAmbiguousInjectionTarget(context, injection, targetType);
            return null;
        }

        IMethodSymbol targetMethod = candidates[0];
        AnalyzeMemberNameStyle(context, injection.Method.ContainingType, targetType, targetMethod, injection.Attribute, "method");
        return new InjectionTarget(
            targetMethod.Name,
            targetMethod.IsStatic,
            targetMethod.ReturnType,
            targetMethod.Parameters,
            true);
    }

    private static IEnumerable<IMethodSymbol> FindMethodCandidates(INamedTypeSymbol targetType, string targetName) {
        List<IMethodSymbol> moreDerivedCandidates = new List<IMethodSymbol>();

        for (INamedTypeSymbol? current = targetType; current is not null && !IsObject(current); current = current.BaseType) {
            foreach (IMethodSymbol method in current.GetMembers(targetName).OfType<IMethodSymbol>()) {
                if (method.MethodKind != MethodKind.Ordinary) {
                    continue;
                }

                if (current.Equals(targetType, SymbolEqualityComparer.Default) || method.DeclaredAccessibility != Accessibility.Private) {
                    if (moreDerivedCandidates.Any(candidate => MethodSignatureMatches(candidate, method))) {
                        continue;
                    }

                    moreDerivedCandidates.Add(method);
                    yield return method;
                }
            }
        }
    }

    private static bool MethodSignatureMatches(IMethodSymbol left, IMethodSymbol right) {
        return left.TypeParameters.Length == right.TypeParameters.Length &&
               ParametersMatch(left.Parameters, right.Parameters);
    }

    private static bool ParameterTypesMatch(ImmutableArray<ITypeSymbol>? expectedTypes, ImmutableArray<IParameterSymbol> parameters) {
        if (!expectedTypes.HasValue) {
            return true;
        }

        ImmutableArray<ITypeSymbol> types = expectedTypes.Value;
        if (types.Length != parameters.Length) {
            return false;
        }

        for (int i = 0; i < types.Length; i++) {
            if (!SymbolEqualityComparer.Default.Equals(types[i], parameters[i].Type)) {
                return false;
            }
        }

        return true;
    }

    private static bool ConstructorParameterTypesMatch(ImmutableArray<ITypeSymbol>? expectedTypes, ImmutableArray<IParameterSymbol> parameters) {
        return expectedTypes.HasValue
            ? ParameterTypesMatch(expectedTypes, parameters)
            : parameters.Length == 0;
    }

    private static void ValidateInjectionSignature(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType) {
        bool hasControlHandle = false;
        bool hasOperation = false;

        foreach (IParameterSymbol parameter in injection.Method.Parameters) {
            if (IsControlHandleType(parameter.Type, out ITypeSymbol? controlHandleReturnType)) {
                if (hasControlHandle) {
                    ReportInvalidInjectionSignature(context, injection, target, targetType, "only one ControlHandle parameter is supported");
                    continue;
                }

                hasControlHandle = true;
                ValidateControlHandle(context, injection, target, targetType, controlHandleReturnType);
                continue;
            }

            if (IsOperationType(parameter.Type)) {
                if (hasOperation) {
                    ReportInvalidInjectionSignature(context, injection, target, targetType, "only one Operation parameter is supported");
                    continue;
                }

                hasOperation = true;
                if (!injection.TargetsInvoke || injection.AtValue != 3) {
                    ReportInvalidInjectionSignature(
                        context,
                        injection,
                        target,
                        targetType,
                        "Operation parameters are only supported on call-site [Inject] declarations with At.Around");
                }

                continue;
            }

            IParameterSymbol? targetParameter = target.Parameters.FirstOrDefault(candidate => candidate.Name == parameter.Name);
            if (targetParameter is null || !SymbolEqualityComparer.Default.Equals(targetParameter.Type, parameter.Type)) {
                ReportInvalidInjectionSignature(
                    context,
                    injection,
                    target,
                    targetType,
                    "injection method parameters must match target parameters by name and type");
            }
        }

        if (!injection.TargetsInvoke) {
            ValidateInjectionReturnType(context, injection, target, targetType);
        }
    }

    private static void ValidateControlHandle(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType,
        ITypeSymbol? controlHandleReturnType) {
        if (target.ReturnType is null || IsVoid(target.ReturnType)) {
            if (controlHandleReturnType is not null) {
                ReportInvalidInjectionSignature(
                    context,
                    injection,
                    target,
                    targetType,
                    "void targets must use ControlHandle, not ControlHandle<T>");
            }

            return;
        }

        if (controlHandleReturnType is null) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "non-void targets must use ControlHandle<T> with the target return type");
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(controlHandleReturnType, target.ReturnType)) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "ControlHandle<T> type argument must match the target return type");
        }
    }

    private static void ValidateInjectionReturnType(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType) {
        if (IsVoid(injection.Method.ReturnType)) {
            return;
        }

        if (target.ReturnType is null || IsVoid(target.ReturnType)) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "void targets require void [Inject] injection methods");
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(injection.Method.ReturnType, target.ReturnType)) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "[Inject] injection method return type must be void or match the target return type");
        }
    }

    private static void AnalyzeProperties(SymbolAnalysisContext context, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        foreach (IPropertySymbol property in patchType.GetMembers().OfType<IPropertySymbol>()) {
            AttributeData? injectAttribute = property.GetAttributes().FirstOrDefault(IsInjectPropertyAttribute);
            if (injectAttribute is null) {
                continue;
            }

            string targetName = TargetName(injectAttribute, property.Name);
            IPropertySymbol? targetProperty = FindMember(
                targetType,
                targetName,
                type => type.GetMembers(targetName).OfType<IPropertySymbol>().Where(member => ParametersMatch(member.Parameters, property.Parameters)));

            if (targetProperty is null) {
                if (MetadataMemberExists(context.Compilation, targetType, targetName, MetadataMemberKind.Property)) {
                    continue;
                }

                ReportMissing(context, property, injectAttribute, targetName, targetType);
                continue;
            }

            AnalyzeMemberNameStyle(context, patchType, targetType, targetProperty, injectAttribute, "targetName");

            if (!SymbolEqualityComparer.Default.Equals(targetProperty.Type, property.Type) || targetProperty.IsStatic != property.IsStatic) {
                ReportMismatch(
                    context,
                    property,
                    injectAttribute,
                    targetName,
                    targetType,
                    "property type, index parameters, and static-ness must match exactly");
                continue;
            }

            if (property.GetMethod is not null && targetProperty.GetMethod is null) {
                ReportMissing(context, property, injectAttribute, targetName + ".get", targetType);
            }

            if (property.SetMethod is not null && targetProperty.SetMethod is null) {
                ReportMissing(context, property, injectAttribute, targetName + ".set", targetType);
            }
        }
    }

    private static void AnalyzeMethods(SymbolAnalysisContext context, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        foreach (IMethodSymbol method in patchType.GetMembers().OfType<IMethodSymbol>()) {
            AttributeData? injectAttribute = method.GetAttributes().FirstOrDefault(IsInjectMethodAttribute);
            if (injectAttribute is null || method.MethodKind != MethodKind.Ordinary) {
                continue;
            }

            string targetName = TargetName(injectAttribute, method.Name);
            IMethodSymbol? targetMethod = FindMember(
                targetType,
                targetName,
                type => type.GetMembers(targetName)
                    .OfType<IMethodSymbol>()
                    .Where(member => member.MethodKind == MethodKind.Ordinary && ParametersMatch(member.Parameters, method.Parameters)));

            if (targetMethod is null) {
                if (MetadataMemberExists(context.Compilation, targetType, targetName, MetadataMemberKind.Method)) {
                    continue;
                }

                ReportMissing(context, method, injectAttribute, targetName, targetType);
                continue;
            }

            AnalyzeMemberNameStyle(context, patchType, targetType, targetMethod, injectAttribute, "targetName");

            if (!SymbolEqualityComparer.Default.Equals(targetMethod.ReturnType, method.ReturnType) ||
                targetMethod.IsStatic != method.IsStatic ||
                targetMethod.TypeParameters.Length != method.TypeParameters.Length) {
                ReportMismatch(
                    context,
                    method,
                    injectAttribute,
                    targetName,
                    targetType,
                    "return type, parameter types, static-ness, and generic arity must match exactly");
            }
        }
    }

    private static TSymbol? FindMember<TSymbol>(
        INamedTypeSymbol targetType,
        string targetName,
        Func<INamedTypeSymbol, IEnumerable<TSymbol>> candidates)
        where TSymbol : ISymbol {
        for (INamedTypeSymbol? current = targetType; current is not null && !IsObject(current); current = current.BaseType) {
            TSymbol? match = candidates(current)
                .FirstOrDefault(member => current.Equals(targetType, SymbolEqualityComparer.Default) || member.DeclaredAccessibility != Accessibility.Private);

            if (match is not null) {
                return match;
            }
        }

        return default;
    }

    private static bool ParametersMatch(ImmutableArray<IParameterSymbol> left, ImmutableArray<IParameterSymbol> right) {
        if (left.Length != right.Length) {
            return false;
        }

        for (int i = 0; i < left.Length; i++) {
            if (!SymbolEqualityComparer.Default.Equals(left[i].Type, right[i].Type)) {
                return false;
            }
        }

        return true;
    }

    private static bool MetadataMemberExists(
        Compilation compilation,
        INamedTypeSymbol targetType,
        string targetName,
        MetadataMemberKind memberKind) {
        string targetMetadataName = MetadataName(targetType);

        foreach (PortableExecutableReference reference in compilation.References.OfType<PortableExecutableReference>()) {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly ||
                !assembly.Identity.Equals(targetType.ContainingAssembly.Identity)) {
                continue;
            }

            if (MetadataMemberExists(reference.GetMetadata(), targetMetadataName, targetName, memberKind)) {
                return true;
            }
        }

        return false;
    }

    private static bool MetadataMemberExists(
        Metadata metadata,
        string targetMetadataName,
        string targetName,
        MetadataMemberKind memberKind) {
        return metadata switch {
            AssemblyMetadata assemblyMetadata => assemblyMetadata.GetModules()
                .Any(module => MetadataMemberExists(module.GetMetadataReader(), targetMetadataName, targetName, memberKind)),
            ModuleMetadata moduleMetadata => MetadataMemberExists(moduleMetadata.GetMetadataReader(), targetMetadataName, targetName, memberKind),
            _ => false,
        };
    }

    private static bool MetadataMemberExists(
        MetadataReader reader,
        string targetMetadataName,
        string targetName,
        MetadataMemberKind memberKind) {
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions) {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            if (TypeDefinitionMetadataName(reader, handle) != targetMetadataName) {
                continue;
            }

            return memberKind switch {
                MetadataMemberKind.Field => HasField(reader, definition, targetName),
                MetadataMemberKind.Property => HasProperty(reader, definition, targetName),
                MetadataMemberKind.Method => HasMethod(reader, definition, targetName),
                _ => false,
            };
        }

        return false;
    }

    private static bool HasField(MetadataReader reader, TypeDefinition definition, string targetName) {
        foreach (FieldDefinitionHandle handle in definition.GetFields()) {
            FieldDefinition field = reader.GetFieldDefinition(handle);
            if (reader.GetString(field.Name) == targetName) {
                return true;
            }
        }

        return false;
    }

    private static bool HasProperty(MetadataReader reader, TypeDefinition definition, string targetName) {
        foreach (PropertyDefinitionHandle handle in definition.GetProperties()) {
            PropertyDefinition property = reader.GetPropertyDefinition(handle);
            if (reader.GetString(property.Name) == targetName) {
                return true;
            }
        }

        return false;
    }

    private static bool HasMethod(MetadataReader reader, TypeDefinition definition, string targetName) {
        foreach (MethodDefinitionHandle handle in definition.GetMethods()) {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            if (reader.GetString(method.Name) == targetName) {
                return true;
            }
        }

        return false;
    }

    private static string MetadataName(INamedTypeSymbol type) {
        if (type.ContainingType is not null) {
            return MetadataName(type.ContainingType) + "+" + type.MetadataName;
        }

        string containingNamespace = type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString();
        return containingNamespace.Length == 0 ? type.MetadataName : containingNamespace + "." + type.MetadataName;
    }

    private static string TypeDefinitionMetadataName(MetadataReader reader, TypeDefinitionHandle handle) {
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        string name = reader.GetString(definition.Name);
        TypeDefinitionHandle declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil) {
            return TypeDefinitionMetadataName(reader, declaringType) + "+" + name;
        }

        string ns = reader.GetString(definition.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static void ReportMissing(
        SymbolAnalysisContext context,
        ISymbol declaration,
        AttributeData injectAttribute,
        string targetName,
        INamedTypeSymbol targetType) {
        context.ReportDiagnostic(Diagnostic.Create(
            MissingMemberRule,
            LocationOf(injectAttribute, declaration, context.CancellationToken),
            declaration.Name,
            targetName,
            targetType.ToDisplayString()));
    }

    private static void ReportMismatch(
        SymbolAnalysisContext context,
        ISymbol declaration,
        AttributeData injectAttribute,
        string targetName,
        INamedTypeSymbol targetType,
        string reason) {
        context.ReportDiagnostic(Diagnostic.Create(
            MismatchedMemberRule,
            LocationOf(injectAttribute, declaration, context.CancellationToken),
            declaration.Name,
            targetName,
            targetType.ToDisplayString(),
            reason));
    }

    private static void ReportUnresolvedPatchTarget(
        SymbolAnalysisContext context,
        AttributeData patchAttribute,
        INamedTypeSymbol patchType,
        string targetTypeName) {
        context.ReportDiagnostic(Diagnostic.Create(
            UnresolvedPatchTargetRule,
            LocationOf(patchAttribute, patchType, context.CancellationToken),
            targetTypeName));
    }

    private static void ReportMissingInjectionTarget(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        INamedTypeSymbol targetType) {
        context.ReportDiagnostic(Diagnostic.Create(
            MissingInjectionTargetRule,
            LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
            injection.Method.Name,
            injection.TargetName,
            targetType.ToDisplayString()));
    }

    private static void ReportAmbiguousInjectionTarget(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        INamedTypeSymbol targetType) {
        context.ReportDiagnostic(Diagnostic.Create(
            AmbiguousInjectionTargetRule,
            LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
            injection.Method.Name,
            injection.TargetName,
            targetType.ToDisplayString()));
    }

    private static void ReportInvalidInjectionSignature(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType,
        string reason) {
        context.ReportDiagnostic(Diagnostic.Create(
            InvalidInjectionSignatureRule,
            LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
            injection.Method.Name,
            target.Name,
            targetType.ToDisplayString(),
            reason));
    }

    private static void ReportStaticInstanceMismatch(
        SymbolAnalysisContext context,
        ISymbol patchMember,
        AttributeData attribute,
        InjectionTarget target,
        INamedTypeSymbol targetType,
        string reason) {
        context.ReportDiagnostic(Diagnostic.Create(
            StaticInstanceMismatchRule,
            LocationOf(attribute, patchMember, context.CancellationToken),
            patchMember.Name,
            target.Name,
            targetType.ToDisplayString(),
            reason));
    }

    private static void ReportAttachedFieldCouldBeInjectField(
        SymbolAnalysisContext context,
        IFieldSymbol field,
        INamedTypeSymbol targetType) {
        context.ReportDiagnostic(Diagnostic.Create(
            AttachedFieldCouldBeInjectFieldRule,
            LocationOf(field),
            field.Name,
            targetType.ToDisplayString()));
    }

    private static void ReportDuplicateInjection(
        SymbolAnalysisContext context,
        InjectionInfo duplicate,
        InjectionInfo first,
        INamedTypeSymbol targetType) {
        context.ReportDiagnostic(Diagnostic.Create(
            DuplicateInjectionRule,
            LocationOf(duplicate.Attribute, duplicate.Method, context.CancellationToken),
            duplicate.Method.Name,
            first.TargetName,
            targetType.ToDisplayString(),
            PositionName(first)));
    }

    private static void ReportUnsupported(
        SymbolAnalysisContext context,
        ISymbol patchMember,
        AttributeData attribute,
        string reason) {
        context.ReportDiagnostic(Diagnostic.Create(
            UnsupportedDeclarationShapeRule,
            LocationOf(attribute, patchMember, context.CancellationToken),
            patchMember.Name,
            reason));
    }

    private static void ReportPreferTypeofPatchTarget(
        SymbolAnalysisContext context,
        AttributeData patchAttribute,
        INamedTypeSymbol patchType,
        INamedTypeSymbol targetType,
        Location? location) {
        context.ReportDiagnostic(Diagnostic.Create(
            PreferTypeofPatchTargetRule,
            location ?? LocationOf(patchAttribute, patchType, context.CancellationToken),
            targetType.ToDisplayString()));
    }

    private static void ReportPreferNameofMemberTarget(
        SymbolAnalysisContext context,
        AttributeData attribute,
        ISymbol patchMember,
        ISymbol targetMember,
        Location? location) {
        context.ReportDiagnostic(Diagnostic.Create(
            PreferNameofMemberTargetRule,
            location ?? LocationOf(attribute, patchMember, context.CancellationToken),
            targetMember.Name,
            targetMember.ContainingType.ToDisplayString()));
    }

    private static void ReportPreferInheritedPatchTarget(
        SymbolAnalysisContext context,
        AttributeData patchAttribute,
        INamedTypeSymbol patchType,
        INamedTypeSymbol targetType) {
        Location? location = ArgumentLocation(patchAttribute, "target", context.CancellationToken);
        context.ReportDiagnostic(Diagnostic.Create(
            PreferInheritedPatchTargetRule,
            location ?? LocationOf(patchAttribute, patchType, context.CancellationToken),
            targetType.ToDisplayString()));
    }

    private static void AnalyzeMemberNameStyle(
        SymbolAnalysisContext context,
        INamedTypeSymbol patchType,
        INamedTypeSymbol targetType,
        ISymbol targetMember,
        AttributeData attribute,
        string parameterName) {
        if (!context.Compilation.IsSymbolAccessibleWithin(targetMember, patchType, patchType)) {
            return;
        }

        if (IsStringLiteralConstructorArgument(attribute, parameterName, context.CancellationToken, out Location? location)) {
            ReportPreferNameofMemberTarget(context, attribute, patchType, targetMember, location);
        }
    }

    private static bool CanInheritPatchTarget(Compilation compilation, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        if (targetType.TypeKind != TypeKind.Class ||
            targetType.IsSealed ||
            targetType.IsStatic ||
            IsBclType(targetType) ||
            !compilation.IsSymbolAccessibleWithin(targetType, patchType)) {
            return false;
        }

        return IsObject(patchType.BaseType) || SymbolEqualityComparer.Default.Equals(patchType.BaseType, targetType);
    }

    private static bool IsStringLiteralConstructorArgument(
        AttributeData attribute,
        string parameterName,
        CancellationToken cancellationToken,
        out Location? location) {
        location = null;
        AttributeArgumentSyntax? argument = ConstructorArgumentSyntax(attribute, parameterName, cancellationToken);
        if (argument?.Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression)) {
            location = literal.GetLocation();
            return true;
        }

        return false;
    }

    private static Location? ArgumentLocation(AttributeData attribute, string parameterName, CancellationToken cancellationToken) {
        return ConstructorArgumentSyntax(attribute, parameterName, cancellationToken)?.Expression.GetLocation();
    }

    private static AttributeArgumentSyntax? ConstructorArgumentSyntax(
        AttributeData attribute,
        string parameterName,
        CancellationToken cancellationToken) {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax attributeSyntax ||
            attributeSyntax.ArgumentList is null ||
            attribute.AttributeConstructor is null) {
            return null;
        }

        foreach (AttributeArgumentSyntax argument in attributeSyntax.ArgumentList.Arguments) {
            if (argument.NameColon?.Name.Identifier.ValueText == parameterName) {
                return argument;
            }
        }

        int parameterIndex = -1;
        ImmutableArray<IParameterSymbol> parameters = attribute.AttributeConstructor.Parameters;
        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].Name == parameterName) {
                parameterIndex = i;
                break;
            }
        }

        if (parameterIndex < 0) {
            return null;
        }

        int positionalIndex = 0;
        foreach (AttributeArgumentSyntax argument in attributeSyntax.ArgumentList.Arguments) {
            if (argument.NameColon is not null || argument.NameEquals is not null) {
                continue;
            }

            if (positionalIndex == parameterIndex) {
                return argument;
            }

            positionalIndex++;
        }

        return null;
    }

    private static string PositionName(InjectionInfo injection) {
        string at = injection.AtValue switch {
            1 => "Return",
            2 => "Tail",
            3 => "Around",
            _ => "Head",
        };

        return at + "/" + injection.By.ToString();
    }

    private static string DuplicateKey(
        string targetName,
        bool targetsConstructor,
        bool targetsInvoke,
        int atValue,
        uint by,
        ImmutableArray<ITypeSymbol>? parameterTypes) {
        string parameters = parameterTypes.HasValue
            ? string.Join(",", parameterTypes.Value.Select(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            : "*";

        return targetName + "|" + targetsConstructor + "|" + targetsInvoke + "|" + atValue + "|" + by + "|" + parameters;
    }

    private static bool ConstructorHasParameter(AttributeData attribute, string parameterName) {
        return attribute.AttributeConstructor?.Parameters.Any(parameter => parameter.Name == parameterName) == true;
    }

    private static bool TryGetStringConstructorArgument(AttributeData attribute, string parameterName, out string? value) {
        value = null;
        if (!TryGetConstructorArgument(attribute, parameterName, out TypedConstant argument)) {
            return false;
        }

        value = argument.Value as string;
        return true;
    }

    private static bool TryGetIntConstructorArgument(AttributeData attribute, string parameterName, out int value) {
        value = 0;
        if (!TryGetConstructorArgument(attribute, parameterName, out TypedConstant argument) || argument.Value is null) {
            return false;
        }

        value = (int)argument.Value;
        return true;
    }

    private static bool TryGetUIntConstructorArgument(AttributeData attribute, string parameterName, out uint value) {
        value = 0;
        if (!TryGetConstructorArgument(attribute, parameterName, out TypedConstant argument) || argument.Value is null) {
            return false;
        }

        value = (uint)argument.Value;
        return true;
    }

    private static ImmutableArray<ITypeSymbol>? TryGetTypeArrayConstructorArgument(AttributeData attribute, string parameterName) {
        if (!TryGetConstructorArgument(attribute, parameterName, out TypedConstant argument) ||
            argument.IsNull ||
            argument.Kind != TypedConstantKind.Array) {
            return null;
        }

        ImmutableArray<ITypeSymbol>.Builder builder = ImmutableArray.CreateBuilder<ITypeSymbol>(argument.Values.Length);
        foreach (TypedConstant value in argument.Values) {
            if (value.Value is ITypeSymbol type) {
                builder.Add(type);
            }
        }

        return builder.ToImmutable();
    }

    private static bool TryGetConstructorArgument(AttributeData attribute, string parameterName, out TypedConstant argument) {
        if (attribute.AttributeConstructor is not null) {
            ImmutableArray<IParameterSymbol> parameters = attribute.AttributeConstructor.Parameters;
            for (int i = 0; i < parameters.Length && i < attribute.ConstructorArguments.Length; i++) {
                if (parameters[i].Name == parameterName) {
                    argument = attribute.ConstructorArguments[i];
                    return true;
                }
            }
        }

        foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments) {
            if (namedArgument.Key == parameterName) {
                argument = namedArgument.Value;
                return true;
            }
        }

        argument = default;
        return false;
    }

    private static bool HasInjectInstanceProperty(INamedTypeSymbol patchType) {
        return patchType.GetMembers()
            .OfType<IPropertySymbol>()
            .Any(property => property.GetAttributes().Any(IsInjectInstanceAttribute));
    }

    private static bool IsControlHandleType(ITypeSymbol type, out ITypeSymbol? returnType) {
        returnType = null;
        if (type is not INamedTypeSymbol named ||
            named.ContainingNamespace.ToDisplayString() != "Concord") {
            return false;
        }

        if (named.Name == "ControlHandle" && !named.IsGenericType) {
            return true;
        }

        if (named.Name == "ControlHandle" && named.IsGenericType && named.TypeArguments.Length == 1) {
            returnType = named.TypeArguments[0];
            return true;
        }

        return false;
    }

    private static bool IsOperationType(ITypeSymbol type) {
        if (type is not INamedTypeSymbol named ||
            named.ContainingNamespace.ToDisplayString() != "Concord") {
            return false;
        }

        return named.Name == "Operation" && (!named.IsGenericType || named.TypeArguments.Length == 1);
    }

    private static bool IsVoid(ITypeSymbol type) {
        return type.SpecialType == SpecialType.System_Void;
    }

    private static bool CanAssignTargetToProperty(INamedTypeSymbol targetType, ITypeSymbol propertyType) {
        if (SymbolEqualityComparer.Default.Equals(targetType, propertyType)) {
            return true;
        }

        if (propertyType.SpecialType == SpecialType.System_Object && targetType.IsReferenceType) {
            return true;
        }

        if (propertyType is INamedTypeSymbol namedPropertyType) {
            for (INamedTypeSymbol? current = targetType.BaseType; current is not null && !IsObject(current); current = current.BaseType) {
                if (SymbolEqualityComparer.Default.Equals(current, namedPropertyType)) {
                    return true;
                }
            }

            return targetType.AllInterfaces.Any(interfaceType => SymbolEqualityComparer.Default.Equals(interfaceType, namedPropertyType));
        }

        return false;
    }

    private static Location? LocationOf(AttributeData attribute, ISymbol fallbackSymbol, CancellationToken cancellationToken) {
        return attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? LocationOf(fallbackSymbol);
    }

    private static Location? LocationOf(ISymbol symbol) {
        return symbol.Locations.FirstOrDefault(location => location.IsInSource) ?? symbol.Locations.FirstOrDefault();
    }

    private static string TargetName(AttributeData attribute, string fallback) {
        if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string targetName) {
            return targetName;
        }

        return fallback;
    }

    private static bool IsPatchAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "PatchAttribute");
    }

    private static bool IsInjectFieldAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "InjectFieldAttribute");
    }

    private static bool IsInjectPropertyAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "InjectPropertyAttribute");
    }

    private static bool IsInjectMethodAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "InjectMethodAttribute");
    }

    private static bool IsInjectAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "InjectAttribute");
    }

    private static bool IsInjectInstanceAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "InjectInstanceAttribute");
    }

    private static bool IsConcordAttribute(AttributeData attribute, string name) {
        INamedTypeSymbol? attributeClass = attribute.AttributeClass;
        return attributeClass?.Name == name &&
               attributeClass.ContainingNamespace.ToDisplayString() == "Concord";
    }

    private static bool IsObject(INamedTypeSymbol? type) {
        return type is null || type.SpecialType == SpecialType.System_Object;
    }

    private static bool IsBclType(INamedTypeSymbol type) {
        string? ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        return ns == "System" || ns?.StartsWith("System.", StringComparison.Ordinal) == true;
    }

    private sealed class PatchTargetResult {
        public PatchTargetResult(
            AttributeData patchAttribute,
            INamedTypeSymbol? targetType,
            string? unresolvedTarget,
            string? failureReason,
            bool usesStringTarget,
            bool usesExplicitTypeTarget) {
            PatchAttribute = patchAttribute;
            TargetType = targetType;
            UnresolvedTarget = unresolvedTarget;
            FailureReason = failureReason;
            UsesStringTarget = usesStringTarget;
            UsesExplicitTypeTarget = usesExplicitTypeTarget;
        }

        public AttributeData PatchAttribute { get; }

        public INamedTypeSymbol? TargetType { get; }

        public string? UnresolvedTarget { get; }

        public string? FailureReason { get; }

        public bool UsesStringTarget { get; }

        public bool UsesExplicitTypeTarget { get; }
    }

    private sealed class InjectionInfo {
        public InjectionInfo(
            IMethodSymbol method,
            AttributeData attribute,
            string targetName,
            bool targetsConstructor,
            bool targetsInvoke,
            int atValue,
            uint by,
            ImmutableArray<ITypeSymbol>? parameterTypes,
            string duplicateKey) {
            Method = method;
            Attribute = attribute;
            TargetName = targetName;
            TargetsConstructor = targetsConstructor;
            TargetsInvoke = targetsInvoke;
            AtValue = atValue;
            By = by;
            ParameterTypes = parameterTypes;
            DuplicateKey = duplicateKey;
        }

        public IMethodSymbol Method { get; }

        public AttributeData Attribute { get; }

        public string TargetName { get; }

        public bool TargetsConstructor { get; }

        public bool TargetsInvoke { get; }

        public int AtValue { get; }

        public uint By { get; }

        public ImmutableArray<ITypeSymbol>? ParameterTypes { get; }

        public string DuplicateKey { get; }
    }

    private sealed class InjectionTarget {
        public InjectionTarget(
            string name,
            bool isStatic,
            ITypeSymbol? returnType,
            ImmutableArray<IParameterSymbol> parameters,
            bool signatureValidated) {
            Name = name;
            IsStatic = isStatic;
            ReturnType = returnType;
            Parameters = parameters;
            SignatureValidated = signatureValidated;
        }

        public string Name { get; }

        public bool IsStatic { get; }

        public ITypeSymbol? ReturnType { get; }

        public ImmutableArray<IParameterSymbol> Parameters { get; }

        public bool SignatureValidated { get; }
    }
}
