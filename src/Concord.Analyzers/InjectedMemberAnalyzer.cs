using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
    ///     Diagnostic id for injected member declarations whose target member has an incompatible signature.
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
    ///     Diagnostic id for patch members with mismatched static or instance usage.
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
    ///     Diagnostic id for unsupported Concord declaration member forms.
    /// </summary>
    public const string UnsupportedDeclarationFormDiagnosticId = "CONCORD011";

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

    /// <summary>
    ///     Diagnostic id for [Inject] methods that return Control outside the head position.
    /// </summary>
    public const string ControlReturnPositionDiagnosticId = "CONCORD015";

    /// <summary>
    ///     Diagnostic id for around-invoke Operation parameters whose shape does not match the resolved call site.
    /// </summary>
    public const string OperationShapeMismatchDiagnosticId = "CONCORD016";

    /// <summary>
    ///     Diagnostic id for At.Constant/At.Argument injection methods that are not shaped 'T M(T original)'.
    /// </summary>
    public const string InvalidValueInjectionShapeDiagnosticId = "CONCORD017";

    /// <summary>
    ///     Diagnostic id for [Inject] declarations that pair a constant constructor with a non-Constant position,
    ///     or pair At.Constant/At.Argument with a non-dedicated constructor.
    /// </summary>
    public const string InvalidConstantPositionDiagnosticId = "CONCORD018";

    /// <summary>
    ///     Diagnostic id for At.Argument injections with arg: 0 that cannot infer a unique argument by type.
    /// </summary>
    public const string AmbiguousArgumentInjectionDiagnosticId = "CONCORD019";

    /// <summary>
    ///     Diagnostic id for target or call-site names that resolve to a property with both accessors and
    ///     nothing to disambiguate which one is meant.
    /// </summary>
    public const string AmbiguousAccessorNameDiagnosticId = "CONCORD020";

    /// <summary>
    ///     Diagnostic id for invalid patch ordering declarations.
    /// </summary>
    public const string InvalidPatchOrderingDiagnosticId = "CONCORD021";

    /// <summary>
    ///     Diagnostic id for At.Transpiler/At.TranspilerFinal injection methods that are not static.
    /// </summary>
    public const string TranspilerMustBeStaticDiagnosticId = "CONCORD022";

    /// <summary>
    ///     Diagnostic id for At.Transpiler/At.TranspilerFinal injection methods whose signature is not
    ///     IEnumerable&lt;CodeInstruction&gt; in and out, with an optional ITranspilerContext second parameter.
    /// </summary>
    public const string InvalidTranspilerSignatureDiagnosticId = "CONCORD023";

    /// <summary>
    ///     Diagnostic id for transpiler method bodies that reference a [Shadow]/[InjectField]/[InjectProperty]/
    ///     [InjectMethod] member of the declaring [Patch] type.
    /// </summary>
    public const string TranspilerInjectedMemberAccessDiagnosticId = "CONCORD024";

    /// <summary>
    ///     Diagnostic id for a plain method on a [Patch] type that references a [Shadow]/[InjectField]/
    ///     [InjectProperty]/[InjectMethod] member.
    /// </summary>
    public const string InjectedMemberOutsideInjectionDiagnosticId = "CONCORD025";

    /// <summary>
    ///     Diagnostic id for a patch declaration whose injections carry two state types for one target.
    /// </summary>
    public const string ConflictingStateTypeDiagnosticId = "CONCORD026";

    /// <summary>
    ///     Diagnostic id for a state slot a declaration reads but never writes.
    /// </summary>
    public const string UnwrittenStateSlotDiagnosticId = "CONCORD027";

    /// <summary>
    ///     Diagnostic id for a [Capture] parameter on an injection position that matches no call.
    /// </summary>
    public const string MisplacedCaptureDiagnosticId = "CONCORD028";

    /// <summary>
    ///     Diagnostic id for [Slice] on an injection position that is neither invoke nor construction.
    /// </summary>
    public const string MisplacedSliceDiagnosticId = "CONCORD029";

    /// <summary>
    ///     Diagnostic id for a [Capture] ordinal the matched call cannot supply.
    /// </summary>
    public const string InvalidCaptureArgumentDiagnosticId = "CONCORD030";

    /// <summary>
    ///     Diagnostic id for an extended enum declaration that also declares an injection.
    /// </summary>
    public const string EnumDeclarationInjectionDiagnosticId = "CONCORD031";

    /// <summary>
    ///     Diagnostic id for an [EnumMember] field that is not static or not typed as the extended enum.
    /// </summary>
    public const string InvalidEnumMemberFieldDiagnosticId = "CONCORD032";

    /// <summary>
    ///     Diagnostic id for a member field with a non-const initializer, which Concord overwrites.
    /// </summary>
    public const string NonConstEnumMemberInitializerDiagnosticId = "CONCORD033";

    /// <summary>
    ///     Diagnostic id for two extended enum members in one assembly that resolve to the same id.
    /// </summary>
    public const string DuplicateEnumMemberIdDiagnosticId = "CONCORD034";

    /// <summary>
    ///     Diagnostic id for a member read from a static constructor, before Concord assigns it.
    /// </summary>
    public const string EnumMemberReadBeforeApplyDiagnosticId = "CONCORD035";

    /// <summary>
    ///     Diagnostic id for a [Local] parameter that sets more than one of Ordinal, Index and Name.
    /// </summary>
    public const string ConflictingLocalSelectorDiagnosticId = "CONCORD036";

    /// <summary>
    ///     Diagnostic id for a [Local] parameter on an injection at At.Head.
    /// </summary>
    public const string LocalAtHeadDiagnosticId = "CONCORD037";

    /// <summary>
    ///     Diagnostic id for a [Local] parameter on an injection position that binds no local.
    /// </summary>
    public const string MisplacedLocalDiagnosticId = "CONCORD044";

    /// <summary>
    ///     Diagnostic id for a [Local] parameter on a whole-method Around injection method.
    /// </summary>
    public const string LocalOnWholeMethodAroundDiagnosticId = "CONCORD045";

    /// <summary>
    ///     Diagnostic id for an At.Local position that sets more than one of Ordinal, Index and Name.
    /// </summary>
    public const string ConflictingLocalPositionSelectorDiagnosticId = "CONCORD046";

    /// <summary>
    ///     Diagnostic id for an [Inject] declaration that pairs the local-targeting constructor with a
    ///     position other than At.Local, or At.Local with a non-dedicated constructor.
    /// </summary>
    public const string InvalidLocalPositionDiagnosticId = "CONCORD047";

    /// <summary>
    ///     Diagnostic id for a LocalHandle&lt;T&gt; parameter at a position where the write cannot be read back.
    /// </summary>
    public const string LocalWriteAtReadOnlyPositionDiagnosticId = "CONCORD048";

    /// <summary>
    ///     Diagnostic id for an injection body that writes to a plain [Local] parameter.
    /// </summary>
    public const string LocalParameterWriteDiagnosticId = "CONCORD049";

    /// <summary>
    ///     Diagnostic id for one parameter carrying both [Capture] and [Local].
    /// </summary>
    public const string CaptureAndLocalDiagnosticId = "CONCORD050";

    private const string ConcordPatchesNamespace = "Concord.Patches";    private const string ConcordNamespace = "Concord";
    private const string OperationTypeName = "Operation";
    private const string VoidOperationPrefix = "VoidOperation<";
    private const string ConstructorName = ".ctor";
    private const string InvokeDeclaringTypeParameter = "invokeDeclaringType";

    // Mirrors WrapperComposer.SupportsLocalBinding's message. The two lists are deliberate twins:
    // the analyzer cannot reference Concord.Emit, so changing one means changing the other.
    private const string LocalPositionHelp = "[Local] is supported at At.Return, At.Tail, At.Finally, At.Local, and the At.Head and " +
                                             "At.Tail shifts of At.Invoke and At.NewObj.";

    // Mirrors WrapperComposer.LocalWriteHelp. The two lists are deliberate twins: the analyzer
    // cannot reference Concord.Emit, so changing one means changing the other.
    private const string LocalWriteHelp = "LocalHandle<T> is supported at At.Local and the At.Head and At.Tail shifts of " +
                                          "At.Invoke and At.NewObj. Use a plain [Local] parameter to read a local at the other positions.";

    private const string LocalHandleTypeName = "LocalHandle";

    private static readonly DiagnosticDescriptor MissingMemberRule = new(
        MissingMemberDiagnosticId,
        "Injected member target was not found",
        "Injected member '{0}' could not find target member '{1}' on '{2}'",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Injected member declarations must name a field, property, or method that exists on the patch target type.");

    private static readonly DiagnosticDescriptor MismatchedMemberRule = new(
        MismatchedMemberDiagnosticId,
        "Injected member target does not match",
        "Injected member '{0}' does not match target member '{1}' on '{2}': {3}",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Injected member declarations must match the target member type, static-ness, return type, and signature.");

    private static readonly DiagnosticDescriptor UnresolvedPatchTargetRule = new(
        UnresolvedPatchTargetDiagnosticId,
        "Patch target could not be resolved",
        "Patch target '{0}' could not be resolved. Concord analyzers cannot validate this declaration.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Warning,
        true,
        "String patch targets should resolve from source or project references so Concord analyzers can validate the declaration.");

    private static readonly DiagnosticDescriptor MissingInjectionTargetRule = new(
        MissingInjectionTargetDiagnosticId,
        "Injection target was not found",
        "[Inject] declaration '{0}' could not find target '{1}' on '{2}'",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Injections must name a target method or constructor that exists on the patch target type.");

    private static readonly DiagnosticDescriptor AmbiguousInjectionTargetRule = new(
        AmbiguousInjectionTargetDiagnosticId,
        "Injection target is ambiguous",
        "[Inject] declaration '{0}' target '{1}' on '{2}' is ambiguous. Specify parameterTypes.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Overloaded injection targets must be disambiguated with parameterTypes.");

    private static readonly DiagnosticDescriptor InvalidInjectionSignatureRule = new(
        InvalidInjectionSignatureDiagnosticId,
        "Injection signature is invalid",
        "[Inject] declaration '{0}' does not match target '{1}' on '{2}': {3}",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Injection method parameters must bind to target parameters by name and type, and ControlHandle<T> must match the target return type.");

    private static readonly DiagnosticDescriptor StaticInstanceMismatchRule = new(
        StaticInstanceMismatchDiagnosticId,
        "Patch member static-ness is invalid",
        "Patch member '{0}' does not match target '{1}' on '{2}': {3}",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Static target methods cannot use instance declaration members or injected target instances.");

    private static readonly DiagnosticDescriptor AttachedFieldCouldBeInjectFieldRule = new(
        AttachedFieldCouldBeInjectFieldDiagnosticId,
        "Patch field matches a target field but is not [InjectField]",
        "Patch field '{0}' matches a target field on '{1}' and will become attached data. Add [InjectField] if it should access the target field.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Warning,
        true,
        "Plain fields on patch declarations become attached data. Use [InjectField] when the field is intended to access a real target field.");

    private static readonly DiagnosticDescriptor DuplicateInjectionRule = new(
        DuplicateInjectionDiagnosticId,
        "Duplicate injection declaration",
        "[Inject] declaration '{0}' duplicates injection target '{1}' on '{2}' at '{3}'",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Warning,
        true,
        "Duplicate injection declarations at the same target and position are usually accidental.");

    private static readonly DiagnosticDescriptor UnsupportedDeclarationFormRule = new(
        UnsupportedDeclarationFormDiagnosticId,
        "Unsupported Concord declaration member",
        "Patch member '{0}' uses unsupported Concord declaration form: {1}",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Concord declaration members must use supported declaration forms.");

    private static readonly DiagnosticDescriptor PreferTypeofPatchTargetRule = new(
        PreferTypeofPatchTargetDiagnosticId,
        "Patch target should use typeof",
        "Patch target '{0}' is available at compile time. Use typeof({0}) instead of a string target.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Warning,
        true,
        "String patch targets are intended for late-bound or inaccessible types. Use typeof when the target type is available at compile time.");

    private static readonly DiagnosticDescriptor PreferNameofMemberTargetRule = new(
        PreferNameofMemberTargetDiagnosticId,
        "Member target should use nameof",
        "Target member '{0}' on '{1}' is available at compile time. Use nameof(...) instead of a string literal.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Warning,
        true,
        "String member targets are intended for inaccessible members. Use nameof when the target member is available at compile time.");

    private static readonly DiagnosticDescriptor PreferInheritedPatchTargetRule = new(
        PreferInheritedPatchTargetDiagnosticId,
        "Patch target should be inherited",
        "Patch target '{0}' can be inherited. Derive the patch declaration from '{0}' and use [Patch] instead of [Patch(typeof(...))].",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Warning,
        true,
        "When a target type can be inherited, deriving the patch declaration from the target lets C# bind visible members directly and lets Concord infer the patch target.");

    private static readonly DiagnosticDescriptor ControlReturnPositionRule = new(
        ControlReturnPositionDiagnosticId,
        "Control return is only valid on head injections",
        "[Inject] method '{0}' returns Control at the {1} position. A Control return is only valid on a head injection.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Returning Control decides whether the original method runs, which only makes sense before it does. Move the injection to At.Head or return void.");

    private static readonly DiagnosticDescriptor OperationShapeMismatchRule = new(
        OperationShapeMismatchDiagnosticId,
        "Operation parameter shape does not match the call site",
        "[Inject] method '{0}' declares '{1}', but call site '{2}' requires '{3}'",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "An around-invoke Operation parameter must match the shape of the matched call: leading parameter types, then the result type last, or VoidOperation/Operation for void calls.");

    private static readonly DiagnosticDescriptor InvalidValueInjectionShapeRule = new(
        InvalidValueInjectionShapeDiagnosticId,
        "Value injection signature is invalid",
        "[Inject] method '{0}' must be shaped '{1} M({1} original)'",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "At.Constant and At.Argument injection methods must take and return the matched value's type unchanged in shape.");

    private static readonly DiagnosticDescriptor InvalidConstantPositionRule = new(
        InvalidConstantPositionDiagnosticId,
        "Constant or argument position is invalid",
        "[Inject] method '{0}': {1}",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Constant-targeting constructors require At.Constant, and At.Constant/At.Argument require their dedicated [Inject] constructor.");

    private static readonly DiagnosticDescriptor AmbiguousArgumentInjectionRule = new(
        AmbiguousArgumentInjectionDiagnosticId,
        "Argument injection cannot infer a unique argument",
        "[Inject] method '{0}' on call site '{1}' cannot infer a unique '{2}' argument. Pass arg: to select one.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "At.Argument with arg: 0 infers the argument by matching the injection method's parameter type against the call site's parameter types. Pass arg: when more than one parameter shares that type.");

    private static readonly DiagnosticDescriptor AmbiguousAccessorNameRule = new(
        AmbiguousAccessorNameDiagnosticId,
        "Accessor name is ambiguous",
        "'{0}' is a property with both accessors and nothing selects one. Write '{1}' or '{2}' explicitly.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "When a target or call-site name resolves to a property with both a getter and a setter, name it explicitly (get_X/set_X) unless an around-invoke Operation parameter disambiguates it.");

    private static readonly DiagnosticDescriptor InvalidPatchOrderingRule = new(
        InvalidPatchOrderingDiagnosticId,
        "Patch ordering declaration is invalid",
        "{0} is invalid: {1}",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "PatchBefore and PatchAfter must appear on patch declarations and name valid, non-conflicting patch owners.");

    private static readonly DiagnosticDescriptor TranspilerMustBeStaticRule = new(
        TranspilerMustBeStaticDiagnosticId,
        "Transpiler injection must be static",
        "Transpiler injection method '{0}' must be static. A [Patch] declaration is abstract, so an instance transpiler can never be invoked.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "At.Transpiler and At.TranspilerFinal injection methods rewrite raw IL directly and are invoked by reflection, so they must be static.");

    private static readonly DiagnosticDescriptor InvalidTranspilerSignatureRule = new(
        InvalidTranspilerSignatureDiagnosticId,
        "Transpiler injection signature is invalid",
        "Transpiler injection method '{0}' must be shaped 'static IEnumerable<CodeInstruction> M(IEnumerable<CodeInstruction> instructions)', optionally with a second 'ITranspilerContext' parameter",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "At.Transpiler and At.TranspilerFinal injection methods must take and return IEnumerable<CodeInstruction> exactly, with an optional ITranspilerContext second parameter.");

    private static readonly DiagnosticDescriptor TranspilerInjectedMemberAccessRule = new(
        TranspilerInjectedMemberAccessDiagnosticId,
        "Transpiler must not reference injected members",
        "Transpiler injection method '{0}' references '{1}', a [Shadow]/[InjectField]/[InjectProperty]/[InjectMethod] member. Those members are abstract IL-copy sources that only exist for declarative injections, so an invoked transpiler can never reach them.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Shadow and injected members exist only as IL-copy sources for declarative injections. A transpiler is invoked directly rather than copied, so touching one fails at runtime instead of compile time.");

    private static readonly DiagnosticDescriptor InjectedMemberOutsideInjectionRule = new(
        InjectedMemberOutsideInjectionDiagnosticId,
        "Injected member referenced outside an injection method",
        "'{0}' references '{1}', a [Shadow]/[InjectField]/[InjectProperty]/[InjectMethod] member, but is not itself an [Inject] method. Only an injection body is copied into the wrapper, so this reads the declaration's own field and gets null or default.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Concord rewrites injected member accesses only inside the bodies it copies into the generated wrapper. A helper, constructor or ordinary property on the patch type is called normally, so it sees the declaration's own member - whatever its initializer left there - with no error at build or patch time. Pass the value in as a parameter from the injection method, or read the target member by reflection.");

    private static readonly DiagnosticDescriptor ConflictingStateTypeRule = new(
        ConflictingStateTypeDiagnosticId,
        "Patch declaration uses two state types for one target",
        "Patch declaration '{0}' uses state type '{1}' and '{2}' for the same slot on '{3}'. One declaration must use one state type per target.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "A patch declaration gets one control-handle state slot per target method, so every SetState and GetState in that declaration must name the same type for a given target.");

    private static readonly DiagnosticDescriptor UnwrittenStateSlotRule = new(
        UnwrittenStateSlotDiagnosticId,
        "State slot is read but never written",
        "Patch declaration '{0}' reads state as '{1}' on '{2}', but no injection in the declaration calls SetState<{1}>. The read yields default({1}).",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Warning,
        true,
        "An unwritten state slot reads back as the default value, so a GetState with no matching SetState anywhere in the declaration is usually a missing write rather than an intended default.");

    private static readonly DiagnosticDescriptor MisplacedCaptureRule = new(
        MisplacedCaptureDiagnosticId,
        "Capture parameter is not at a matched call",
        "Injection method '{0}' declares a [Capture] parameter, but its position {1}",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "[Capture] binds an argument of a call matched inside the target body, so it needs an invoke or construction injection shifted to At.Head or At.Tail. At.Around and At.Argument already receive the call's arguments.");

    private static readonly DiagnosticDescriptor MisplacedSliceRule = new(
        MisplacedSliceDiagnosticId,
        "Slice is only valid on invoke and construction positions",
        "Injection method '{0}' carries [Slice] at position '{1}'; [Slice] applies to invoke and construction positions only",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "[Slice] bounds the call search an invoke or construction injection performs, so it has no meaning on a position that matches no call.");

    private static readonly DiagnosticDescriptor InvalidCaptureArgumentRule = new(
        InvalidCaptureArgumentDiagnosticId,
        "Capture argument is out of range",
        "[Capture] on parameter '{0}' of '{1}' {2}",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "[Capture] names a 1-based argument of the matched call, so the ordinal must be at least 1 and no greater than the number of arguments the matched call takes.");

    private static readonly DiagnosticDescriptor EnumDeclarationInjectionRule = new(
        EnumDeclarationInjectionDiagnosticId,
        "Extended enum declaration cannot inject",
        "'{0}' extends an enum, so it cannot also declare the injection '{1}'",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "A declaration whose base is ExtendedEnum<T> holds member fields. Move injections to their own [Patch] class.");

    private static readonly DiagnosticDescriptor InvalidEnumMemberFieldRule = new(
        InvalidEnumMemberFieldDiagnosticId,
        "[EnumMember] field has the wrong shape",
        "Field '{0}' carries [EnumMember] but must be static and typed as '{1}'",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Concord assigns extended enum members to static fields typed as the extended enum.");

    private static readonly DiagnosticDescriptor NonConstEnumMemberInitializerRule = new(
        NonConstEnumMemberInitializerDiagnosticId,
        "Extended enum member initializer is discarded",
        "Member '{0}' has an initializer Concord cannot read and overwrites. Declare it const to pin the value.",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Warning,
        true,
        "Concord reads a pinned value from a compile-time constant. A plain static initializer runs before Concord assigns the field, so the value is lost.");

    private static readonly DiagnosticDescriptor DuplicateEnumMemberIdRule = new(
        DuplicateEnumMemberIdDiagnosticId,
        "Two extended enum members share an id",
        "Member id '{0}' is already declared by '{1}'",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Each extended enum member needs its own persisted id, because the id is the key Concord stores its value under.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    private static readonly DiagnosticDescriptor EnumMemberReadBeforeApplyRule = new(
        EnumMemberReadBeforeApplyDiagnosticId,
        "Extended enum member read before Concord assigns it",
        "Member '{0}' is read from a static constructor, which runs before Concord assigns it",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Warning,
        true,
        "Concord assigns member fields during Patcher.Apply. A static constructor on the declaring type can run first and read the default value.");

    private static readonly DiagnosticDescriptor ConflictingLocalSelectorRule = new(
        ConflictingLocalSelectorDiagnosticId,
        "Local parameter sets more than one selector",
        "[Local] on parameter '{0}' of '{1}' sets {2}; keep one and delete the rest",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Ordinal, Index and Name each pick the target local a different way, so setting two leaves no single answer for which local to bind.");

    private static readonly DiagnosticDescriptor LocalAtHeadRule = new(
        LocalAtHeadDiagnosticId,
        "Local parameter is read before the target assigns it",
        "Injection method '{0}' declares a [Local] parameter at At.Head, which runs before the target body assigns any local",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "At.Head runs ahead of the target body, so every local still holds its default. Move the injection to a position that runs after the assignment.");

    // Mirrors WrapperComposer.RejectMisplacedLocals' CONC160 message. The two are deliberate twins:
    // this analyzer is netstandard2.0 and reads At values from the user's compilation, the runtime
    // switches an InjectAt graph, so neither can call the other.
    private static readonly DiagnosticDescriptor LocalOnWholeMethodAroundRule = new(
        LocalOnWholeMethodAroundDiagnosticId,
        "Local parameter on a whole-method Around injection method",
        "Injection method '{0}' declares a [Local] or LocalHandle<T> parameter at At.Around, where the target's locals do not exist",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "An Around splices a copy of the target body in at each original.Invoke, so the target's locals only live inside those " +
        "copies and the Around method's own statements never see one. With more than one Invoke site there is no single copy to " +
        "bind. Move the parameter to an At.Return or At.Tail injection on the same target.");

    private static readonly DiagnosticDescriptor ConflictingLocalPositionSelectorRule = new(
        ConflictingLocalPositionSelectorDiagnosticId,
        "At.Local position sets more than one selector",
        "At.Local on '{0}' sets {1}; keep one and delete the rest",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "Ordinal, Index and Name each pick the target local a different way, so setting two leaves no single answer for which local the injection targets.");

    private static readonly DiagnosticDescriptor InvalidLocalPositionRule = new(
        InvalidLocalPositionDiagnosticId,
        "Local position is invalid",
        "Injection method '{0}' {1}",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "The local-targeting [Inject] constructor carries the local type and access, so it requires At.Local, and At.Local requires it.");

    private static readonly DiagnosticDescriptor MisplacedLocalRule = new(
        MisplacedLocalDiagnosticId,
        "Local parameter is at a position that binds no local",
        "Injection method '{0}' declares a [Local] parameter at position '{1}', which binds no local",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        LocalPositionHelp);

    private static readonly DiagnosticDescriptor LocalWriteAtReadOnlyPositionRule = new(
        LocalWriteAtReadOnlyPositionDiagnosticId,
        "Local handle is at a position where the write is dead",
        "Injection method '{0}' declares a LocalHandle<T> parameter at position '{1}', where the target body is done with its locals",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        LocalWriteHelp);

    private static readonly DiagnosticDescriptor LocalParameterWriteRule = new(
        LocalParameterWriteDiagnosticId,
        "Local parameter is written",
        "Injection method '{0}' writes to [Local] parameter '{1}'",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "A [Local] parameter reads the target's local. It has no argument slot of its own, so an assignment would " +
        "store into the wrapper's argument of the same index, which is one of the target's parameters. Declare the " +
        "parameter as LocalHandle<T> and assign its Value to write the slot.");

    private static readonly DiagnosticDescriptor CaptureAndLocalRule = new(
        CaptureAndLocalDiagnosticId,
        "Parameter carries both [Capture] and [Local]",
        "Injection method '{0}' parameter '{1}' carries both [Capture] and [Local]",
        ConcordPatchesNamespace,
        DiagnosticSeverity.Error,
        true,
        "[Capture] binds an argument of the matched call and [Local] binds a local of the target body. One parameter " +
        "cannot be both, and the runtime would silently keep the local and throw the capture away.");

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
            UnsupportedDeclarationFormRule,
            PreferTypeofPatchTargetRule,
            PreferNameofMemberTargetRule,
            PreferInheritedPatchTargetRule,
            ControlReturnPositionRule,
            OperationShapeMismatchRule,
            InvalidValueInjectionShapeRule,
            InvalidConstantPositionRule,
            AmbiguousArgumentInjectionRule,
            AmbiguousAccessorNameRule,
            InvalidPatchOrderingRule,
            TranspilerMustBeStaticRule,
            InvalidTranspilerSignatureRule,
            TranspilerInjectedMemberAccessRule,
            InjectedMemberOutsideInjectionRule,
            ConflictingStateTypeRule,
            UnwrittenStateSlotRule,
            MisplacedCaptureRule,
            MisplacedSliceRule,
            InvalidCaptureArgumentRule,
            EnumDeclarationInjectionRule,
            InvalidEnumMemberFieldRule,
            NonConstEnumMemberInitializerRule,
            DuplicateEnumMemberIdRule,
            EnumMemberReadBeforeApplyRule,
            ConflictingLocalSelectorRule,
            LocalAtHeadRule,
            MisplacedLocalRule,
            LocalWriteAtReadOnlyPositionRule,
            LocalOnWholeMethodAroundRule,
            ConflictingLocalPositionSelectorRule,
            InvalidLocalPositionRule,
            LocalParameterWriteRule,
            CaptureAndLocalRule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterSymbolStartAction(AnalyzeStateSlots, SymbolKind.NamedType);
        context.RegisterCompilationStartAction(AnalyzeEnumMemberIds);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context) {
        if (context.Symbol is not INamedTypeSymbol patchType ||
            patchType.TypeKind != TypeKind.Class) {
            return;
        }

        if (GetExtendedEnumType(patchType) is INamedTypeSymbol extendedEnum) {
            AnalyzeExtendedEnumDeclaration(context, patchType, extendedEnum);
            return;
        }

        ImmutableArray<AttributeData> orderingAttributes = patchType.GetAttributes()
            .Where(IsPatchOrderingAttribute)
            .ToImmutableArray();
        PatchTargetResult? patchTarget = GetPatchTarget(context.Compilation, patchType);
        if (patchTarget is null) {
            foreach (AttributeData attribute in orderingAttributes) {
                ReportInvalidPatchOrdering(context, patchType, attribute, "the declaring class is not marked with [Patch]");
            }

            return;
        }

        AnalyzePatchOrdering(context, patchType, orderingAttributes);

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
        AnalyzeInjectedMemberScope(context, patchType, targetType);
        AnalyzeInjectionSurface(context, patchType);
    }

    private static void AnalyzeInjectionSurface(SymbolAnalysisContext context, INamedTypeSymbol patchType) {
        foreach (IMethodSymbol method in patchType.GetMembers().OfType<IMethodSymbol>()) {
            if (method.MethodKind != MethodKind.Ordinary) {
                continue;
            }

            InjectionDeclaration? declaration = TryGetInjectionDeclaration(method);
            if (declaration is null) {
                continue;
            }

            ValidateSlicePosition(context, declaration);
            ValidateCaptureParameters(context, declaration);
            ValidateLocalParameters(context, declaration);
        }
    }

    private static void ValidateSlicePosition(SymbolAnalysisContext context, InjectionDeclaration declaration) {
        AttributeData? slice = declaration.Method.GetAttributes().FirstOrDefault(IsSliceAttribute);
        if (slice is null || declaration.TargetsCallSite || declaration.AtName == "Local") {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            MisplacedSliceRule,
            LocationOf(slice, declaration.Method, context.CancellationToken),
            declaration.Method.Name,
            "At." + declaration.AtName));
    }

    // Mirrors RejectMisplacedCaptures: a capture needs a matched call, so only the Head and Tail
    // shifts of the invoke and construction forms can supply one. Whole-method positions match no
    // call at all, and the remaining shifts already hand the call's arguments to the injection.
    private static void ValidateCaptureParameters(SymbolAnalysisContext context, InjectionDeclaration declaration) {
        IParameterSymbol? captured = declaration.Method.Parameters
            .FirstOrDefault(parameter => parameter.GetAttributes().Any(IsCaptureAttribute));
        if (captured is null) {
            return;
        }

        if (!declaration.TargetsCallSite || declaration.AtName is not ("Head" or "Tail")) {
            string reason = declaration.TargetsCallSite
                ? $"uses the shift At.{declaration.AtName}, which does not name a point where the call's arguments are still on the stack"
                : $"is a whole-method position (At.{declaration.AtName}), which matches no call";
            context.ReportDiagnostic(Diagnostic.Create(
                MisplacedCaptureRule,
                LocationOf(captured),
                declaration.Method.Name,
                reason));
            return;
        }

        ValidateCaptureArguments(context, declaration);
    }

    private static void ValidateCaptureArguments(SymbolAnalysisContext context, InjectionDeclaration declaration) {
        int? arity = ResolveCallSiteArity(declaration);

        foreach (IParameterSymbol parameter in declaration.Method.Parameters) {
            AttributeData? capture = parameter.GetAttributes().FirstOrDefault(IsCaptureAttribute);
            if (capture is null || !TryGetUIntConstructorArgument(capture, "arg", out uint arg)) {
                continue;
            }

            string? reason = null;
            if (arg == 0) {
                reason = "captures argument 0; [Capture] is 1-based, so the first argument is 1";
            } else if (arity.HasValue && arg > arity.Value) {
                reason = $"captures argument {arg}, but the matched call takes {arity.Value} argument(s)";
            }

            if (reason is null) {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                InvalidCaptureArgumentRule,
                LocationOf(capture, parameter, context.CancellationToken),
                parameter.Name,
                declaration.Method.Name,
                reason));
        }
    }

    private static IParameterSymbol? ValidateLocalParameterAttributes(SymbolAnalysisContext context, InjectionDeclaration declaration) {
        IParameterSymbol? first = null;

        foreach (IParameterSymbol parameter in declaration.Method.Parameters) {
            AttributeData? local = parameter.GetAttributes().FirstOrDefault(IsLocalAttribute);
            if (local is null) {
                if (IsLocalHandleType(parameter.Type)) {
                    first ??= parameter;
                }

                continue;
            }

            first ??= parameter;
            ReportConflictingLocalSelectors(context, declaration, parameter, local);

            if (parameter.GetAttributes().Any(IsCaptureAttribute)) {
                context.ReportDiagnostic(Diagnostic.Create(
                    CaptureAndLocalRule,
                    LocationOf(local, parameter, context.CancellationToken),
                    declaration.Method.Name,
                    parameter.Name));
            }

            if (!IsLocalHandleType(parameter.Type)) {
                ReportLocalParameterWrite(context, declaration, parameter);
            }
        }

        return first;
    }

    // Mirrors WrapperComposer.RejectMisplacedLocals. At.Head runs before the target body assigns
    // anything, and every other unsupported position lowers through a copier that builds no binding
    // map. At values are matched by enum field name so this list diffs by eye against the runtime one.
    private static void ValidateLocalParameters(SymbolAnalysisContext context, InjectionDeclaration declaration) {
        IParameterSymbol? first = ValidateLocalParameterAttributes(context, declaration);

        if (first is null) {
            return;
        }

        if (SupportsLocalBinding(declaration)) {
            ValidateLocalWrites(context, declaration);
            return;
        }

        if (!declaration.TargetsCallSite && declaration.AtName == "Around") {
            context.ReportDiagnostic(Diagnostic.Create(
                LocalOnWholeMethodAroundRule,
                LocationOf(first),
                declaration.Method.Name));
            return;
        }

        if (!declaration.TargetsCallSite && declaration.AtName == "Head") {
            context.ReportDiagnostic(Diagnostic.Create(
                LocalAtHeadRule,
                LocationOf(first),
                declaration.Method.Name));
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            MisplacedLocalRule,
            LocationOf(first),
            declaration.Method.Name,
            PositionName(declaration)));
    }

    // Twin of BodyCopier's CONC164 and the ldarga copy. Matches by identifier text, like
    // ContainsOperationInvoke does: C# already forbids a local that shadows a parameter, and lambdas
    // and local functions are skipped, so the name is the parameter inside this body.
    private static void ReportLocalParameterWrite(
        SymbolAnalysisContext context, InjectionDeclaration declaration, IParameterSymbol parameter) {
        foreach (SyntaxReference syntaxReference in declaration.Method.DeclaringSyntaxReferences) {
            SyntaxNode declared = syntaxReference.GetSyntax(context.CancellationToken);
            foreach (SyntaxNode descendant in declared.DescendantNodes(descendIntoChildren: DescendIntoMethodBody)) {
                SyntaxNode? written = WrittenIdentifier(descendant);
                if (written is null) {
                    continue;
                }

                IdentifierNameSyntax? root = RootIdentifier(written);
                if (root is null || root.Identifier.Text != parameter.Name) {
                    continue;
                }

                // A field or indexer write through the parameter only matters when the parameter owns
                // the storage. On a reference type it reaches the same object the target holds, which
                // is a legitimate thing to do. On a struct it reaches the by-value copy and vanishes.
                if (!ReferenceEquals(root, written) && !parameter.Type.IsValueType) {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    LocalParameterWriteRule,
                    written.GetLocation(),
                    declaration.Method.Name,
                    parameter.Name));
                return;
            }
        }
    }

    // A ref or out argument counts: it lowers to the address of the bound slot, so the callee writes
    // the target's local through it just as an assignment would.
    // Peels 'p.X.Y' and 'p[0]' back to 'p'. An assignment to a struct local's field compiles to the
    // same ldarga the out argument does, so it hits the copy and does nothing, silently.
    private static IdentifierNameSyntax? RootIdentifier(SyntaxNode node) {
        while (true) {
            switch (node) {
                case IdentifierNameSyntax identifier:
                    return identifier;
                case MemberAccessExpressionSyntax memberAccess:
                    node = memberAccess.Expression;
                    break;
                case ElementAccessExpressionSyntax elementAccess:
                    node = elementAccess.Expression;
                    break;
                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    break;
                default:
                    return null;
            }
        }
    }

    private static SyntaxNode? WrittenIdentifier(SyntaxNode node) {
        return node switch {
            AssignmentExpressionSyntax assignment => assignment.Left,
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression)
                || prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression)
                || postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
            ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) => argument.Expression,
            _ => null,
        };
    }

    // Mirrors WrapperComposer.RejectMisplacedLocalWrites. Runs after the read gate, so everything
    // left here already binds a local; only the positions where the write is dead remain.
    private static void ValidateLocalWrites(SymbolAnalysisContext context, InjectionDeclaration declaration) {
        if (SupportsLocalWrite(declaration)) {
            return;
        }

        foreach (IParameterSymbol parameter in declaration.Method.Parameters) {
            if (!IsLocalHandleType(parameter.Type)) {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                LocalWriteAtReadOnlyPositionRule,
                LocationOf(parameter),
                declaration.Method.Name,
                PositionName(declaration)));
            return;
        }
    }

    // Twin of WrapperComposer.PositionName. A call-site shift is spelled with its owning position in
    // front of it, because a bare "At.Around" would read the same for a shift and a whole-method
    // Around. Keep both in step.
    private static string PositionName(InjectionDeclaration declaration) {
        if (!declaration.TargetsCallSite) {
            return "At." + declaration.AtName;
        }

        return (declaration.TargetsNewObj ? "At.NewObj/At." : "At.Invoke/At.") + declaration.AtName;
    }

    private static string PositionName(InjectionInfo injection) {
        string at = AtMemberName(injection.Attribute, injection.TargetsInvoke ? "shift" : "at") ?? "Head";

        return at + "/" + injection.By.ToString();
    }

    // Twin of WrapperComposer.SupportsLocalBinding. Keep both in step; the analyzer targets
    // netstandard2.0 with no reference to Concord.Emit, so it cannot call the runtime one.
    private static bool SupportsLocalBinding(InjectionDeclaration declaration) {
        return declaration.TargetsCallSite
            ? declaration.AtName is "Head" or "Tail"
            : declaration.AtName is "Return" or "Tail" or "Finally" or "Local";
    }

    // Twin of WrapperComposer.SupportsLocalWrite. Keep both in step; the analyzer targets
    // netstandard2.0 with no reference to Concord.Emit, so it cannot call the runtime one.
    private static bool SupportsLocalWrite(InjectionDeclaration declaration) {
        return declaration.TargetsCallSite
            ? declaration.AtName is "Head" or "Tail"
            : declaration.AtName == "Local";
    }

    private static bool IsLocalHandleType(ITypeSymbol type) {
        return type is INamedTypeSymbol named
               && named.Name == LocalHandleTypeName
               && named.TypeArguments.Length == 1
               && named.ContainingNamespace.ToDisplayString() == ConcordNamespace;
    }

    private static void ReportConflictingLocalSelectors(
        SymbolAnalysisContext context,
        InjectionDeclaration declaration,
        IParameterSymbol parameter,
        AttributeData local) {
        List<string> set = new List<string>(3);
        if (TryGetUIntConstructorArgument(local, "Ordinal", out uint ordinal) && ordinal != 0) {
            set.Add("Ordinal");
        }

        if (TryGetIntConstructorArgument(local, "Index", out int index) && index != -1) {
            set.Add("Index");
        }

        if (TryGetStringConstructorArgument(local, "Name", out string? name) && name is not null) {
            set.Add("Name");
        }

        if (set.Count < 2) {
            return;
        }

        string named = string.Join(", ", set.Take(set.Count - 1)) + " and " + set[set.Count - 1];
        context.ReportDiagnostic(Diagnostic.Create(
            ConflictingLocalSelectorRule,
            LocationOf(local, parameter, context.CancellationToken),
            parameter.Name,
            declaration.Method.Name,
            named));
    }

    // Returns the matched call's argument count, or null when the call site does not resolve to a
    // single member from source. An unresolved call site is validated at compose time instead.
    // Unlike TargetIdentity, an absent invokeParameterTypes means "any constructor of this type"
    // rather than the parameterless one, so this branch keeps ParameterTypesMatch.
    private static int? ResolveCallSiteArity(InjectionDeclaration declaration) {
        if (declaration.CallSiteType is not INamedTypeSymbol callSiteType) {
            return null;
        }

        if (declaration.TargetsNewObj) {
            ImmutableArray<IMethodSymbol> constructors = callSiteType.InstanceConstructors
                .Where(constructor => ParameterTypesMatch(declaration.CallSiteParameterTypes, constructor.Parameters))
                .ToImmutableArray();
            return constructors.Length == 1 ? constructors[0].Parameters.Length : null;
        }

        if (declaration.CallSiteMember is null) {
            return null;
        }

        string? effectiveName = ResolveAccessorName(callSiteType, declaration.CallSiteMember, declaration.Method, false, out bool ambiguous);
        if (ambiguous || effectiveName is null) {
            return null;
        }

        ImmutableArray<IMethodSymbol> candidates = FindMethodCandidates(callSiteType, effectiveName)
            .Where(candidate => ParameterTypesMatch(declaration.CallSiteParameterTypes, candidate.Parameters))
            .ToImmutableArray();
        return candidates.Length == 1 ? candidates[0].Parameters.Length : null;
    }

    private static void AnalyzeStateSlots(SymbolStartAnalysisContext context) {
        if (context.Symbol is not INamedTypeSymbol patchType || patchType.TypeKind != TypeKind.Class) {
            return;
        }

        PatchTargetResult? patchTarget = GetPatchTarget(context.Compilation, patchType);
        if (patchTarget?.TargetType is null ||
            patchTarget.UnresolvedTarget is not null ||
            patchTarget.FailureReason is not null) {
            return;
        }

        StateSlotCollector collector = new StateSlotCollector(patchType, patchTarget.TargetType);
        context.RegisterSyntaxNodeAction(collector.Collect, SyntaxKind.InvocationExpression);
        context.RegisterSymbolEndAction(collector.Report);
    }

    private static void ResolveCallSiteArguments(
        AttributeData attribute,
        bool targetsNewObj,
        bool targetsInvoke,
        out ITypeSymbol? callSiteType,
        out string? callSiteMember,
        out ImmutableArray<ITypeSymbol>? callSiteParameterTypes) {
        callSiteType = null;
        callSiteMember = null;
        if (TryGetConstructorArgument(attribute, targetsNewObj ? "constructedType" : InvokeDeclaringTypeParameter, out TypedConstant callSiteTypeArgument) &&
            callSiteTypeArgument.Value is ITypeSymbol resolvedCallSiteType) {
            callSiteType = resolvedCallSiteType;
        }

        if (targetsInvoke) {
            TryGetStringConstructorArgument(attribute, "invokeDeclaringMethod", out callSiteMember);
        }

        callSiteParameterTypes = TryGetTypeArrayConstructorArgument(attribute, "invokeParameterTypes");
    }

    private static InjectionDeclaration? TryGetInjectionDeclaration(IMethodSymbol method) {
        AttributeData? attribute = method.GetAttributes().FirstOrDefault(IsInjectAttribute);
        bool targetsNewObj = false;
        if (attribute is null) {
            attribute = method.GetAttributes().FirstOrDefault(IsInjectNewAttribute);
            targetsNewObj = attribute is not null;
        }

        if (attribute is null) {
            return null;
        }

        bool targetsInvoke = !targetsNewObj && ConstructorHasParameter(attribute, InvokeDeclaringTypeParameter);
        bool targetsCallSite = targetsInvoke || targetsNewObj;
        string atParameterName = targetsCallSite ? "shift" : "at";
        string? positionName = AtMemberName(attribute, atParameterName);
        if (positionName is null) {
            return null;
        }

        bool targetsConstructor = !ConstructorHasParameter(attribute, "method");
        string targetMemberName = ConstructorName;
        if (!targetsConstructor) {
            if (!TryGetStringConstructorArgument(attribute, "method", out string? methodName) || string.IsNullOrWhiteSpace(methodName)) {
                return null;
            }

            targetMemberName = methodName!;
        }

        ITypeSymbol? callSiteType = null;
        string? callSiteMember = null;
        ImmutableArray<ITypeSymbol>? callSiteParameterTypes = null;
        if (targetsCallSite) {
            ResolveCallSiteArguments(
                attribute,
                targetsNewObj,
                targetsInvoke,
                out callSiteType,
                out callSiteMember,
                out callSiteParameterTypes);
        }

        return new InjectionDeclaration(
            method,
            targetsCallSite,
            targetsNewObj,
            positionName,
            targetsConstructor,
            targetMemberName,
            TryGetTypeArrayConstructorArgument(attribute, targetsCallSite ? "targetParameterTypes" : "parameterTypes"),
            callSiteType,
            callSiteMember,
            callSiteParameterTypes);
    }

    // Names the At value through the enum's own fields rather than its ordinal, so the new rules do
    // not join AtValue in depending on the append-only ordering of Concord.At.
    private static string? AtMemberName(AttributeData attribute, string parameterName) {
        if (!TryGetConstructorArgument(attribute, parameterName, out TypedConstant argument) ||
            argument.Kind != TypedConstantKind.Enum ||
            argument.Type is not INamedTypeSymbol enumType) {
            return null;
        }

        foreach (IFieldSymbol field in enumType.GetMembers().OfType<IFieldSymbol>()) {
            if (field.HasConstantValue && Equals(field.ConstantValue, argument.Value)) {
                return field.Name;
            }
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S4158", Justification = "False positive: beforeOwners/afterOwners accumulate across loop iterations via owners.Add, so oppositeOwners is not empty on later iterations. The Contains check detects owners declared in both [PatchBefore] and [PatchAfter].")]
    private static void AnalyzePatchOrdering(
        SymbolAnalysisContext context,
        INamedTypeSymbol patchType,
        ImmutableArray<AttributeData> orderingAttributes) {
        string patchOwner = MetadataName(patchType);
        HashSet<string> beforeOwners = new(StringComparer.Ordinal);
        HashSet<string> afterOwners = new(StringComparer.Ordinal);
        foreach (AttributeData attribute in orderingAttributes) {
            string? owner = PatchOrderingOwner(attribute);
            if (owner is null || string.IsNullOrWhiteSpace(owner)) {
                ReportInvalidPatchOrdering(context, patchType, attribute, "the owner cannot be empty");
                continue;
            }

            if (string.Equals(owner, patchOwner, StringComparison.Ordinal)) {
                ReportInvalidPatchOrdering(context, patchType, attribute, "a patch cannot order itself");
                continue;
            }

            bool isBefore = IsPatchBeforeAttribute(attribute);
            HashSet<string> owners = isBefore ? beforeOwners : afterOwners;
            HashSet<string> oppositeOwners = isBefore ? afterOwners : beforeOwners;
            if (oppositeOwners.Contains(owner)) {
                ReportInvalidPatchOrdering(
                    context,
                    patchType,
                    attribute,
                    $"owner '{owner}' appears in both [PatchBefore] and [PatchAfter]");
            }

            owners.Add(owner);
        }
    }

    private static string? PatchOrderingOwner(AttributeData attribute) {
        if (attribute.ConstructorArguments.Length == 0) {
            return null;
        }

        TypedConstant owner = attribute.ConstructorArguments[0];
        return owner.Kind == TypedConstantKind.Type && owner.Value is INamedTypeSymbol ownerType
            ? MetadataName(ownerType)
            : owner.Value as string;
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
            IFieldSymbol? targetField = FindMember(targetType, type => type.GetMembers(targetName).OfType<IFieldSymbol>());
            if (targetField is null) {
                if (MetadataMemberExists(context.Compilation, targetType, targetName, MetadataMemberKind.Field)) {
                    ValidateMetadataFieldShape(context, field, injectAttribute, targetName, targetType);
                    continue;
                }

                ReportMissing(context, field, injectAttribute, targetName, targetType);
                continue;
            }

            AnalyzeMemberNameStyle(context, patchType, targetField, injectAttribute, "targetName");

            // object is the escape hatch for a target whose type cannot be named here, so it is
            // accepted against any target type; Concord boxes on read and unboxes on write.
            bool declaredAsObject = field.Type.SpecialType == SpecialType.System_Object &&
                                    targetField.Type.SpecialType != SpecialType.System_Object;
            if ((!declaredAsObject && !SymbolEqualityComparer.Default.Equals(targetField.Type, field.Type)) ||
                targetField.IsStatic != field.IsStatic) {
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

    // Roslyn's default MetadataImportOptions.Public leaves private members of referenced
    // assemblies out of the symbol model, so the field a [InjectField] most often targets is
    // invisible to FindMember. Read the shape straight out of metadata instead, otherwise the
    // type check never runs for the attribute's primary use case.
    private static void ValidateMetadataFieldShape(
        SymbolAnalysisContext context,
        IFieldSymbol field,
        AttributeData injectAttribute,
        string targetName,
        INamedTypeSymbol targetType) {
        string? targetTypeName = MetadataFieldTypeName(context.Compilation, targetType, targetName, out bool isStatic);
        string? declaredTypeName = SymbolMetadataTypeName(field.Type);
        if (targetTypeName is null || declaredTypeName is null) {
            return;
        }

        // See ValidateFieldParameter: object accepts any target type.
        bool declaredAsObject = declaredTypeName == "System.Object" && targetTypeName != "System.Object";
        if ((!declaredAsObject && targetTypeName != declaredTypeName) || isStatic != field.IsStatic) {
            ReportMismatch(
                context,
                field,
                injectAttribute,
                targetName,
                targetType,
                "field type and static-ness must match exactly");
        }
    }

    // Mirrors what MetadataTypeNameProvider produces so the two names compare directly. Returns
    // null for shapes that cannot be named the same way from both sides.
    private static string? SymbolMetadataTypeName(ITypeSymbol type) {
        switch (type) {
            case IArrayTypeSymbol array: {
                string? element = SymbolMetadataTypeName(array.ElementType);
                if (element is null) {
                    return null;
                }

                return array.Rank == 1
                    ? element + "[]"
                    : element + "[" + new string(',', array.Rank - 1) + "]";
            }

            case IPointerTypeSymbol pointer: {
                string? element = SymbolMetadataTypeName(pointer.PointedAtType);
                return element is null ? null : element + "*";
            }

            // A type nested in a generic carries its outer type arguments in the metadata
            // instantiation but not in TypeArguments, so the two names would never line up.
            case INamedTypeSymbol named when IsNestedInGenericType(named):
                return null;

            case INamedTypeSymbol { IsUnboundGenericType: true }:
                return null;

            case INamedTypeSymbol named when named.IsGenericType &&
                                             !SymbolEqualityComparer.Default.Equals(named, named.OriginalDefinition): {
                string definition = MetadataName(named.OriginalDefinition);
                string?[] arguments = new string?[named.TypeArguments.Length];
                for (int i = 0; i < named.TypeArguments.Length; i++) {
                    arguments[i] = SymbolMetadataTypeName(named.TypeArguments[i]);
                    if (arguments[i] is null) {
                        return null;
                    }
                }

                return definition + "[" + string.Join(",", arguments) + "]";
            }

            case INamedTypeSymbol named:
                return MetadataName(named);

            default:
                return null;
        }
    }

    private static bool IsNestedInGenericType(INamedTypeSymbol type) {
        for (INamedTypeSymbol? containing = type.ContainingType; containing is not null; containing = containing.ContainingType) {
            if (containing.IsGenericType) {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeInjectionMethods(SymbolAnalysisContext context, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        Dictionary<string, InjectionInfo> seen = new Dictionary<string, InjectionInfo>();

        foreach (IMethodSymbol method in patchType.GetMembers().OfType<IMethodSymbol>()) {
            AttributeData? injectAttribute = method.GetAttributes().FirstOrDefault(IsInjectAttribute);
            if (injectAttribute is null || method.MethodKind != MethodKind.Ordinary) {
                continue;
            }

            AnalyzeInjectionMethod(context, patchType, targetType, method, injectAttribute, seen);
        }
    }

    private static void AnalyzeInjectionMethod(
        SymbolAnalysisContext context,
        INamedTypeSymbol patchType,
        INamedTypeSymbol targetType,
        IMethodSymbol method,
        AttributeData injectAttribute,
        Dictionary<string, InjectionInfo> seen) {
        if (method.IsGenericMethod) {
            ReportUnsupported(context, method, injectAttribute, "[Inject] declarations cannot be generic");
            return;
        }

        if (method.IsAbstract) {
            ReportUnsupported(context, method, injectAttribute, "[Inject] declarations must have a body");
            return;
        }

        if (!TryGetInjectionInfo(method, injectAttribute, out InjectionInfo? maybeInjection)) {
            ReportUnsupported(context, method, injectAttribute, "[Inject] constructor arguments could not be read");
            return;
        }

        InjectionInfo injection = maybeInjection!;
        if (seen.TryGetValue(injection.DuplicateKeyValue, out InjectionInfo? first)) {
            ReportDuplicateInjection(context, injection, first, targetType);
        } else {
            seen[injection.DuplicateKeyValue] = injection;
        }

        ValidateConstantPosition(context, injection);
        ValidateLocalPosition(context, injection);

        InjectionTarget? target = ResolveInjectionTarget(context, injection, targetType);
        if (target is null || !target.SignatureValidated) {
            return;
        }

        if (ReportStaticInstanceMismatchIfAny(context, patchType, targetType, method, injectAttribute, target)) {
            return;
        }

        ValidateInjectionSignature(context, injection, target, targetType);
    }

    private static bool ReportStaticInstanceMismatchIfAny(
        SymbolAnalysisContext context,
        INamedTypeSymbol patchType,
        INamedTypeSymbol targetType,
        IMethodSymbol method,
        AttributeData injectAttribute,
        InjectionTarget target) {
        if (target.IsStatic && !method.IsStatic) {
            ReportStaticInstanceMismatch(
                context,
                method,
                injectAttribute,
                target,
                targetType,
                "static target methods require static [Inject] injection methods");
            return true;
        }

        if (target.IsStatic && HasInjectInstanceProperty(patchType)) {
            ReportStaticInstanceMismatch(
                context,
                method,
                injectAttribute,
                target,
                targetType,
                "static target methods cannot use [InjectInstance]");
            return true;
        }

        return false;
    }

    private static void AnalyzeAttachedFields(SymbolAnalysisContext context, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        foreach (IFieldSymbol field in patchType.GetMembers().OfType<IFieldSymbol>()) {
            if (field.IsImplicitlyDeclared ||
                field.IsConst ||
                field.GetAttributes().Any(IsInjectFieldAttribute)) {
                continue;
            }

            IFieldSymbol? targetField = FindMember(targetType, type => type.GetMembers(field.Name).OfType<IFieldSymbol>());
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

    private static string ResolveLocalKey(AttributeData attribute, out LocalPositionInfo? localPosition) {
        string localKey = "*";
        localPosition = null;
        if (ConstructorHasParameter(attribute, "localType")) {
            TryGetConstructorArgument(attribute, "localType", out TypedConstant localTypeArgument);
            TryGetIntConstructorArgument(attribute, "access", out int access);
            TryGetUIntConstructorArgument(attribute, "ordinal", out uint ordinal);
            TryGetIntConstructorArgument(attribute, "index", out int index);
            TryGetStringConstructorArgument(attribute, "name", out string? localName);
            string localTypeName = localTypeArgument.Value is ITypeSymbol localType
                ? localType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : "?";
            localKey = localTypeName + "," + access + "," + ordinal + "," + index + "," + (localName ?? "*");
            localPosition = new LocalPositionInfo(localTypeArgument.Value as ITypeSymbol, ordinal, index, localName);
        }

        return localKey;
    }

    private static bool TryGetInjectionInfo(IMethodSymbol method, AttributeData attribute, out InjectionInfo? injection) {
        injection = null;

        bool hasConstant = ConstructorHasParameter(attribute, "constant");
        bool targetsConstructor = !ConstructorHasParameter(attribute, "method");
        bool targetsInvoke = ConstructorHasParameter(attribute, InvokeDeclaringTypeParameter);
        string targetName = ConstructorName;
        if (!targetsConstructor) {
            if (!TryGetStringConstructorArgument(attribute, "method", out string? methodName) || string.IsNullOrWhiteSpace(methodName)) {
                return false;
            }

            targetName = methodName!;
        }

        string atParameterName = targetsInvoke ? "shift" : "at";
        int atValue = TryGetIntConstructorArgument(attribute, atParameterName, out int parsedAtValue) ? parsedAtValue : 0;
        uint by = TryGetUIntConstructorArgument(attribute, "by", out uint parsedBy) ? parsedBy : 0;
        ImmutableArray<ITypeSymbol>? parameterTypes = TryGetTypeArrayConstructorArgument(
            attribute,
            targetsInvoke ? "targetParameterTypes" : "parameterTypes");

        TypedConstant? constantValue = null;
        if (hasConstant && TryGetConstructorArgument(attribute, "constant", out TypedConstant constantArgument)) {
            constantValue = constantArgument;
        }

        ITypeSymbol? invokeDeclaringType = null;
        string? invokeMethodName = null;
        ImmutableArray<ITypeSymbol>? invokeParameterTypes = null;
        uint arg = 0;
        if (targetsInvoke) {
            if (TryGetConstructorArgument(attribute, InvokeDeclaringTypeParameter, out TypedConstant invokeTypeArgument) &&
                invokeTypeArgument.Value is ITypeSymbol invokeType) {
                invokeDeclaringType = invokeType;
            }

            TryGetStringConstructorArgument(attribute, "invokeDeclaringMethod", out invokeMethodName);
            invokeParameterTypes = TryGetTypeArrayConstructorArgument(attribute, "invokeParameterTypes");
            TryGetUIntConstructorArgument(attribute, "arg", out arg);
        }

        string localKey = ResolveLocalKey(attribute, out LocalPositionInfo? localPosition);

        injection = new InjectionInfo(
            method,
            attribute,
            targetName,
            targetsConstructor,
            targetsInvoke,
            atValue,
            by,
            parameterTypes,
            DuplicateKey(targetName, targetsConstructor, targetsInvoke, atValue, by, parameterTypes, localKey),
            hasConstant,
            constantValue,
            invokeDeclaringType,
            invokeMethodName,
            invokeParameterTypes,
            arg) { Local = localPosition };
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
                if (MetadataMemberExists(context.Compilation, targetType, ConstructorName, MetadataMemberKind.Method)) {
                    return new InjectionTarget(ConstructorName, false, null, ImmutableArray<IParameterSymbol>.Empty, false);
                }

                ReportMissingInjectionTarget(context, injection, targetType);
                return null;
            }

            if (constructors.Length > 1) {
                ReportAmbiguousInjectionTarget(context, injection, targetType);
                return null;
            }

            IMethodSymbol constructor = constructors[0];
            return new InjectionTarget(ConstructorName, false, null, constructor.Parameters, true);
        }

        string? effectiveName = ResolveAccessorName(
            targetType,
            injection.TargetMemberName,
            injection.Method,
            false,
            out bool ambiguousAccessor);
        if (ambiguousAccessor) {
            context.ReportDiagnostic(Diagnostic.Create(
                AmbiguousAccessorNameRule,
                ArgumentLocation(injection.Attribute, "method", context.CancellationToken) ??
                LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
                targetType.ToDisplayString() + "." + injection.TargetMemberName,
                "get_" + injection.TargetMemberName,
                "set_" + injection.TargetMemberName));
            return null;
        }

        ImmutableArray<IMethodSymbol> candidates = FindMethodCandidates(targetType, effectiveName!)
            .Where(method => ParameterTypesMatch(injection.ParameterTypes, method.Parameters))
            .ToImmutableArray();

        if (candidates.Length == 0) {
            if (MetadataMemberExists(context.Compilation, targetType, effectiveName!, MetadataMemberKind.Method)) {
                return new InjectionTarget(effectiveName!, false, null, ImmutableArray<IParameterSymbol>.Empty, false);
            }

            ReportMissingInjectionTarget(context, injection, targetType);
            return null;
        }

        if (candidates.Length > 1) {
            ReportAmbiguousInjectionTarget(context, injection, targetType);
            return null;
        }

        IMethodSymbol targetMethod = candidates[0];
        AnalyzeMemberNameStyle(context, injection.Method.ContainingType, targetMethod, injection.Attribute, "method");
        return new InjectionTarget(
            targetMethod.Name,
            targetMethod.IsStatic,
            targetMethod.ReturnType,
            targetMethod.Parameters,
            true,
            targetMethod);
    }

    private static IEnumerable<IMethodSymbol> FindMethodCandidates(INamedTypeSymbol targetType, string targetName) {
        List<IMethodSymbol> moreDerivedCandidates = new List<IMethodSymbol>();

        for (INamedTypeSymbol? current = targetType; current is not null && !IsObject(current); current = current.BaseType) {
            foreach (IMethodSymbol method in current.GetMembers(targetName).OfType<IMethodSymbol>()) {
                if (!IsReachableCandidate(method, current, targetType)) {
                    continue;
                }

                if (moreDerivedCandidates.Any(candidate => MethodSignatureMatches(candidate, method))) {
                    continue;
                }

                moreDerivedCandidates.Add(method);
                yield return method;
            }
        }
    }

    // A member qualifies as an ordinary method or a property accessor that the target type can
    // actually see: a private member counts only when declared on the target itself, never when
    // inherited from a base class, where it would be out of reach.
    private static bool IsReachableCandidate(IMethodSymbol method, INamedTypeSymbol current, INamedTypeSymbol targetType) {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet)) {
            return false;
        }

        return current.Equals(targetType, SymbolEqualityComparer.Default) || method.DeclaredAccessibility != Accessibility.Private;
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

    private static bool IsValueInjection(InjectionInfo injection) {
        return injection.HasConstant || injection.Local is not null || (injection.TargetsInvoke && injection.AtValue == 5);
    }

    private static void ValidateConstantPosition(SymbolAnalysisContext context, InjectionInfo injection) {
        if (injection.HasConstant && injection.AtValue != 4) {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidConstantPositionRule,
                LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
                injection.Method.Name,
                "passes a constant but position is not At.Constant. Constant injections require At.Constant"));
            return;
        }

        if (!injection.HasConstant && !injection.TargetsInvoke && (injection.AtValue == 4 || injection.AtValue == 5)) {
            string position = injection.AtValue == 4 ? "At.Constant" : "At.Argument";
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidConstantPositionRule,
                LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
                injection.Method.Name,
                "uses position " + position + " without its dedicated constructor form"));
        }
    }

    // At.Local carries the local type, the access and the selectors on one dedicated constructor, so
    // the position and the constructor form have to agree or none of them are readable.
    private static void ValidateLocalPosition(SymbolAnalysisContext context, InjectionInfo injection) {
        const int atLocal = 9;

        if (injection.Local is not null && injection.AtValue != atLocal) {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidLocalPositionRule,
                LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
                injection.Method.Name,
                "names a local type and access but its position is not At.Local"));
            return;
        }

        if (injection.Local is null && !injection.TargetsInvoke && injection.AtValue == atLocal) {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidLocalPositionRule,
                LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
                injection.Method.Name,
                "uses position At.Local without its dedicated constructor form"));
            return;
        }

        if (injection.Local is null) {
            return;
        }

        List<string> set = new List<string>(3);
        if (injection.Local.Ordinal != 0) {
            set.Add("Ordinal");
        }

        if (injection.Local.Index != -1) {
            set.Add("Index");
        }

        if (injection.Local.Name is not null) {
            set.Add("Name");
        }

        if (set.Count < 2) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ConflictingLocalPositionSelectorRule,
            LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
            injection.Method.Name,
            string.Join(", ", set.Take(set.Count - 1)) + " and " + set[set.Count - 1]));
    }

    private static void ValidateInjectionSignature(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType) {
        if (injection.AtValue is 6 or 7) {
            ValidateTranspilerInjection(context, injection, targetType);
            return;
        }

        if (IsValueInjection(injection)) {
            ValidateValueInjectionSignature(context, injection);
            return;
        }

        bool isWholeMethodAround = !injection.TargetsInvoke && injection.AtValue == 3;
        bool hasOperationForInvokeCheck = injection.Method.Parameters.Any(parameter => IsOperationType(parameter.Type));

        if (injection.TargetsInvoke && !(injection.AtValue == 3 && hasOperationForInvokeCheck)) {
            ValidateInvokeCallSiteName(context, injection);
        }

        ParameterValidationState state = new ParameterValidationState();

        foreach (IParameterSymbol parameter in injection.Method.Parameters) {
            ValidateInjectionParameter(context, injection, target, targetType, parameter, isWholeMethodAround, state);
        }

        if (isWholeMethodAround) {
            ValidateWholeMethodAroundSignature(context, injection, target, targetType, state.OperationCount, state.ControlHandleCount, state.OperationParameter);
        }

        ValidateInjectionReturnPosition(context, injection, target, targetType);
    }

    private static void ValidateTranspilerInjection(SymbolAnalysisContext context, InjectionInfo injection, INamedTypeSymbol targetType) {
        IMethodSymbol method = injection.Method;

        if (!method.IsStatic) {
            context.ReportDiagnostic(Diagnostic.Create(
                TranspilerMustBeStaticRule,
                LocationOf(injection.Attribute, method, context.CancellationToken),
                method.Name));
        }

        if (!IsValidTranspilerSignature(method)) {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidTranspilerSignatureRule,
                LocationOf(injection.Attribute, method, context.CancellationToken),
                method.Name));
        }

        ValidateTranspilerInjectedMemberAccess(context, injection, targetType);
    }

    private static bool IsValidTranspilerSignature(IMethodSymbol method) {
        if (method.RefKind != RefKind.None || !IsCodeInstructionEnumerableParameterType(method.ReturnType)) {
            return false;
        }

        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        if (parameters.Length == 1) {
            return IsCodeInstructionEnumerableParameter(parameters[0]);
        }

        return parameters.Length == 2 &&
               IsCodeInstructionEnumerableParameter(parameters[0]) &&
               parameters[1].RefKind == RefKind.None &&
               IsTranspilerContextType(parameters[1].Type);
    }

    private static bool IsCodeInstructionEnumerableParameter(IParameterSymbol parameter) {
        return parameter.RefKind == RefKind.None && IsCodeInstructionEnumerableParameterType(parameter.Type);
    }

    private static bool IsCodeInstructionEnumerableParameterType(ITypeSymbol type) {
        return type is INamedTypeSymbol named &&
               named.IsGenericType &&
               named.Name == "IEnumerable" &&
               named.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" &&
               named.TypeArguments.Length == 1 &&
               IsCodeInstructionType(named.TypeArguments[0]);
    }

    private static bool IsCodeInstructionType(ITypeSymbol type) {
        return type is INamedTypeSymbol named &&
               named.Name == "CodeInstruction" &&
               named.ContainingNamespace.ToDisplayString() == ConcordNamespace;
    }

    private static bool IsTranspilerContextType(ITypeSymbol type) {
        return type is INamedTypeSymbol named &&
               named.Name == "ITranspilerContext" &&
               named.ContainingNamespace.ToDisplayString() == ConcordNamespace;
    }

    // Concord rewrites injected member accesses only in the bodies it copies into the wrapper. Every
    // other method on the patch type is invoked normally and sees the declaration's own member, which
    // is whatever its initializer left there - so it silently reads null or default instead of the
    // target's value, with nothing to show for it at build or patch time.
    private static void AnalyzeInjectedMemberScope(SymbolAnalysisContext context, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        HashSet<string> injectedMembers = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISymbol member in patchType.GetMembers()) {
            if (member.GetAttributes().Any(IsShadowOrInjectedMemberAttribute)) {
                injectedMembers.Add(member.Name);
            }
        }

        AddShadowFieldNames(patchType, targetType, injectedMembers);

        if (injectedMembers.Count == 0) {
            return;
        }

        foreach (ISymbol member in patchType.GetMembers()) {
            if (member is not IMethodSymbol method || method.IsImplicitlyDeclared) {
                continue;
            }

            // An injection body is copied, so it is the one place these members work. Transpilers are
            // [Inject] too but are invoked rather than copied; CONCORD024 already covers those.
            if (method.GetAttributes().Any(IsInjectAttribute)) {
                continue;
            }

            // Abstract members are the declarations themselves, not code that reads them.
            if (method.IsAbstract) {
                continue;
            }

            ReportInjectedMemberUses(context, method, injectedMembers);
        }
    }

    private static void ReportInjectedMemberUses(SymbolAnalysisContext context, IMethodSymbol method, HashSet<string> injectedMembers) {
        foreach (SyntaxReference syntaxReference in method.DeclaringSyntaxReferences) {
            SyntaxNode declaration = syntaxReference.GetSyntax(context.CancellationToken);
            SyntaxNode? body = declaration switch {
                MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                ConstructorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody,
                AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody,
                ArrowExpressionClauseSyntax e => e,
                _ => null,
            };

            if (body is null) {
                continue;
            }

            foreach (SyntaxNode descendant in body.DescendantNodesAndSelf()) {
                if (descendant is not IdentifierNameSyntax identifier || !injectedMembers.Contains(identifier.Identifier.Text)) {
                    continue;
                }

                // nameof(member) is a compile-time string, not an access.
                if (identifier.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } } }) {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    InjectedMemberOutsideInjectionRule,
                    identifier.GetLocation(),
                    method.Name,
                    identifier.Identifier.Text));
                return;
            }
        }
    }

    private static void ValidateTranspilerInjectedMemberAccess(SymbolAnalysisContext context, InjectionInfo injection, INamedTypeSymbol targetType) {
        IMethodSymbol method = injection.Method;
        if (method.ContainingType is not INamedTypeSymbol patchType) {
            return;
        }

        HashSet<string> injectedMemberNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISymbol member in patchType.GetMembers()) {
            if (member.GetAttributes().Any(IsShadowOrInjectedMemberAttribute)) {
                injectedMemberNames.Add(member.Name);
            }
        }

        AddShadowFieldNames(patchType, targetType, injectedMemberNames);

        if (injectedMemberNames.Count == 0) {
            return;
        }

        ReportFirstInjectedMemberReference(context, method, injectedMemberNames);
    }

    // Reports only the first identifier in the transpiler's own body that names an injected member:
    // one diagnostic per transpiler is enough to make the point, and the same name typically recurs
    // throughout the method, so reporting every occurrence would just be noise.
    private static void ReportFirstInjectedMemberReference(SymbolAnalysisContext context, IMethodSymbol method, HashSet<string> injectedMemberNames) {
        foreach (SyntaxReference syntaxReference in method.DeclaringSyntaxReferences) {
            if (syntaxReference.GetSyntax(context.CancellationToken) is not MethodDeclarationSyntax declaration) {
                continue;
            }

            SyntaxNode? body = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody;
            if (body is null) {
                continue;
            }

            foreach (SyntaxNode descendant in body.DescendantNodesAndSelf()) {
                if (descendant is not IdentifierNameSyntax identifier || !injectedMemberNames.Contains(identifier.Identifier.Text)) {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    TranspilerInjectedMemberAccessRule,
                    identifier.GetLocation(),
                    method.Name,
                    identifier.Identifier.Text));
                return;
            }
        }
    }

    private static void AddShadowFieldNames(INamedTypeSymbol patchType, INamedTypeSymbol targetType, HashSet<string> injectedMemberNames) {
        foreach (IFieldSymbol field in patchType.GetMembers().OfType<IFieldSymbol>()) {
            if (field.IsImplicitlyDeclared ||
                field.IsConst ||
                field.GetAttributes().Any(IsInjectFieldAttribute)) {
                continue;
            }

            IFieldSymbol? targetField = targetType.GetMembers(field.Name).OfType<IFieldSymbol>().FirstOrDefault();
            if (targetField is null) {
                continue;
            }

            if (targetField.IsStatic == field.IsStatic && SymbolEqualityComparer.Default.Equals(targetField.Type, field.Type)) {
                injectedMemberNames.Add(field.Name);
            }
        }
    }

    private static bool IsShadowOrInjectedMemberAttribute(AttributeData attribute) {
        return IsInjectFieldAttribute(attribute) ||
               IsInjectPropertyAttribute(attribute) ||
               IsInjectMethodAttribute(attribute) ||
               IsShadowAttribute(attribute);
    }

    private static bool IsShadowAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "ShadowAttribute");
    }

    private static void ValidateInjectionParameter(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType,
        IParameterSymbol parameter,
        bool isWholeMethodAround,
        ParameterValidationState state) {
        if (IsControlHandleType(parameter.Type, out ITypeSymbol? controlHandleReturnType)) {
            ValidateControlHandleParameter(context, injection, target, targetType, controlHandleReturnType, isWholeMethodAround, state);
            return;
        }

        if (IsOperationType(parameter.Type)) {
            ValidateOperationParameter(context, injection, target, targetType, parameter, isWholeMethodAround, state);
            return;
        }

        // A [Capture] parameter binds an argument of the matched call, not a target parameter, so
        // the name and type checks below do not apply to it. CONCORD030 validates it instead.
        if (parameter.GetAttributes().Any(IsCaptureAttribute)) {
            return;
        }

        // A [Local] parameter binds a target local, not a target parameter. CONCORD036, CONCORD037
        // and CONCORD044 validate it instead.
        if (parameter.GetAttributes().Any(IsLocalAttribute) || IsLocalHandleType(parameter.Type)) {
            return;
        }

        if (isWholeMethodAround) {
            return;
        }

        IParameterSymbol? targetParameter = target.Parameters.FirstOrDefault(candidate => candidate.Name == parameter.Name);
        if (targetParameter is null || !SymbolEqualityComparer.Default.Equals(targetParameter.Type, parameter.Type)) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "injection method parameters must match target parameters by name and type");
            return;
        }

        // IParameterSymbol.Type strips the ref, so the type check above passes for `int x` against
        // `ref int x`. The argument slot is still a managed pointer, and assigning to the by-value
        // declaration compiles to starg against it - invalid IL the runtime only rejects at JIT.
        if (IsByRef(targetParameter.RefKind) != IsByRef(parameter.RefKind)) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                IsByRef(targetParameter.RefKind)
                    ? $"target parameter '{targetParameter.Name}' is passed by reference. Declare it '{RefKeyword(targetParameter.RefKind)}' on the injection too"
                    : $"target parameter '{targetParameter.Name}' is passed by value. Drop the byref modifier on the injection");
        }
    }

    private static bool IsByRef(RefKind refKind) {
        return refKind is RefKind.Ref or RefKind.Out or RefKind.RefReadOnly or RefKind.In;
    }

    private static string RefKeyword(RefKind refKind) {
        return refKind switch {
            RefKind.Out => "ref",
            RefKind.In or RefKind.RefReadOnly => "in",
            _ => "ref",
        };
    }

    private static void ValidateControlHandleParameter(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType,
        ITypeSymbol? controlHandleReturnType,
        bool isWholeMethodAround,
        ParameterValidationState state) {
        state.ControlHandleCount++;
        if (state.HasControlHandle) {
            ReportInvalidInjectionSignature(context, injection, target, targetType, "only one ControlHandle parameter is supported");
            return;
        }

        state.HasControlHandle = true;
        if (!isWholeMethodAround) {
            ValidateControlHandle(context, injection, target, targetType, controlHandleReturnType);
        }
    }

    private static void ValidateOperationParameter(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType,
        IParameterSymbol parameter,
        bool isWholeMethodAround,
        ParameterValidationState state) {
        state.OperationCount++;
        if (state.HasOperation) {
            ReportInvalidInjectionSignature(context, injection, target, targetType, "only one Operation parameter is supported");
            return;
        }

        state.HasOperation = true;
        state.OperationParameter = parameter;
        if (injection.TargetsInvoke && injection.AtValue == 3) {
            ValidateOperationShape(context, injection, parameter);
        } else if (!isWholeMethodAround) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "Operation parameters are only supported on call-site [Inject] declarations with At.Around");
        }
    }

    private static void ValidateInjectionReturnPosition(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType) {
        if (IsControlType(injection.Method.ReturnType)) {
            if (injection.TargetsInvoke || injection.AtValue != 0) {
                string position = injection.TargetsInvoke ? "invoke" : PositionName(injection).ToLowerInvariant();
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        ControlReturnPositionRule,
                        injection.Method.Locations.FirstOrDefault(),
                        injection.Method.Name,
                        position));
            }
        } else if (!injection.TargetsInvoke) {
            ValidateInjectionReturnType(context, injection, target, targetType);
        }
    }

    private static void ValidateInvokeCallSiteName(SymbolAnalysisContext context, InjectionInfo injection) {
        if (injection.InvokeDeclaringType is not INamedTypeSymbol invokeDeclaringType || injection.InvokeMethodName is null) {
            return;
        }

        ResolveAccessorName(invokeDeclaringType, injection.InvokeMethodName, injection.Method, false, out bool ambiguousAccessor);
        if (ambiguousAccessor) {
            ReportAmbiguousAccessorName(context, injection, invokeDeclaringType);
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

    private static void ValidateOperationShape(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        IParameterSymbol operationParameter) {
        if (injection.InvokeDeclaringType is not INamedTypeSymbol invokeDeclaringType || injection.InvokeMethodName is null) {
            return;
        }

        string? effectiveName = ResolveAccessorName(invokeDeclaringType, injection.InvokeMethodName, injection.Method, true, out bool ambiguousAccessor);
        if (ambiguousAccessor) {
            ReportAmbiguousAccessorName(context, injection, invokeDeclaringType);
            return;
        }

        if (effectiveName is null) {
            return;
        }

        ImmutableArray<IMethodSymbol> candidates = FindMethodCandidates(invokeDeclaringType, effectiveName)
            .Where(method => ParameterTypesMatch(injection.InvokeParameterTypes, method.Parameters))
            .ToImmutableArray();

        if (candidates.Length != 1) {
            return;
        }

        IMethodSymbol callSite = candidates[0];
        ImmutableArray<ITypeSymbol> parameterTypes = callSite.Parameters.Select(parameter => parameter.Type).ToImmutableArray();
        ITypeSymbol returnType = callSite.ReturnType;

        if (!OperationTypeMatchesExpected(operationParameter.Type, parameterTypes, returnType)) {
            string expected = ExpectedOperationTypeName(parameterTypes, returnType);
            context.ReportDiagnostic(Diagnostic.Create(
                OperationShapeMismatchRule,
                LocationOf(operationParameter),
                injection.Method.Name,
                operationParameter.Type.ToDisplayString(),
                callSite.Name,
                expected));
        }
    }

    private static void ValidateWholeMethodAroundSignature(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType,
        int operationCount,
        int controlHandleCount,
        IParameterSymbol? operationParameter) {
        if (operationCount == 0) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "whole-method At.Around injections must declare exactly one Operation parameter");
        } else if (controlHandleCount > 0) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "whole-method At.Around injections must declare exactly one Operation parameter and no ControlHandle parameters");
        } else if (operationCount == 1 && operationParameter is not null) {
            ValidateWholeMethodOperationShape(context, injection, target, operationParameter);

            if (injection.TargetsConstructor && !ContainsOperationInvoke(injection.Method, operationParameter)) {
                ReportInvalidInjectionSignature(
                    context,
                    injection,
                    target,
                    targetType,
                    "whole-method At.Around on a constructor never calls Invoke(...). A constructor Around must invoke the original constructor exactly once");
            }
        }

        if (target.MethodSymbol is not null) {
            ValidateWholeMethodAroundEligibility(context, injection, target, targetType, target.MethodSymbol);
        }
    }

    private static void ValidateWholeMethodOperationShape(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        IParameterSymbol operationParameter) {
        if (target.ReturnType is null) {
            return;
        }

        ImmutableArray<ITypeSymbol> parameterTypes = target.Parameters.Select(parameter => parameter.Type).ToImmutableArray();
        if (!OperationTypeMatchesExpected(operationParameter.Type, parameterTypes, target.ReturnType)) {
            string expected = ExpectedOperationTypeName(parameterTypes, target.ReturnType);
            context.ReportDiagnostic(Diagnostic.Create(
                OperationShapeMismatchRule,
                LocationOf(operationParameter),
                injection.Method.Name,
                operationParameter.Type.ToDisplayString(),
                target.Name,
                expected));
        }
    }

    private static void ValidateWholeMethodAroundEligibility(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        InjectionTarget target,
        INamedTypeSymbol targetType,
        IMethodSymbol targetMethod) {
        foreach (IParameterSymbol parameter in targetMethod.Parameters) {
            if (parameter.RefKind != RefKind.None) {
                ReportInvalidInjectionSignature(
                    context,
                    injection,
                    target,
                    targetType,
                    $"whole-method At.Around targets a byref parameter '{parameter.Name}'. Byref parameters are not supported by the Operation handle");
                return;
            }

            if (IsUnsupportedByValueShape(parameter.Type)) {
                ReportInvalidInjectionSignature(
                    context,
                    injection,
                    target,
                    targetType,
                    $"whole-method At.Around targets parameter '{parameter.Name}' of type '{parameter.Type.ToDisplayString()}', which is a pointer, function pointer, or byref-like type. These are not supported by the Operation handle");
                return;
            }
        }

        if (targetMethod.ReturnsByRef || targetMethod.ReturnsByRefReadonly) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "whole-method At.Around targets a method that returns by reference. Ref returns are not supported by the Operation handle");
            return;
        }

        if (IsUnsupportedByValueShape(targetMethod.ReturnType)) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                $"whole-method At.Around targets a method returning '{targetMethod.ReturnType.ToDisplayString()}', which is a pointer, function pointer, or byref-like type. These are not supported by the Operation handle");
            return;
        }

        if (targetMethod.IsAsync || IsIteratorMethod(targetMethod)) {
            ReportInvalidInjectionSignature(
                context,
                injection,
                target,
                targetType,
                "whole-method At.Around targets an async or iterator method. State-machine methods are not supported by the Operation handle");
        }
    }

    private static bool IsUnsupportedByValueShape(ITypeSymbol type) {
        return type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer || type.IsRefLikeType;
    }

    private static bool IsIteratorMethod(IMethodSymbol methodSymbol) {
        return methodSymbol.DeclaringSyntaxReferences.Any(syntaxReference => ContainsYield(syntaxReference.GetSyntax()));
    }

    private static bool ContainsYield(SyntaxNode node) {
        return node.DescendantNodes(descendIntoChildren: DescendIntoMethodBody).Any(descendant => descendant is YieldStatementSyntax);
    }

    private static bool ContainsOperationInvoke(IMethodSymbol method, IParameterSymbol operationParameter) {
        foreach (SyntaxReference syntaxReference in method.DeclaringSyntaxReferences) {
            foreach (SyntaxNode descendant in syntaxReference.GetSyntax().DescendantNodes(descendIntoChildren: DescendIntoMethodBody)) {
                if (descendant is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }) {
                    continue;
                }

                if (memberAccess.Name.Identifier.Text != "Invoke" || memberAccess.Expression is not IdentifierNameSyntax identifier) {
                    continue;
                }

                if (identifier.Identifier.Text == operationParameter.Name) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool DescendIntoMethodBody(SyntaxNode node) {
        return node is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax);
    }

    private static void ReportAmbiguousAccessorName(SymbolAnalysisContext context, InjectionInfo injection, INamedTypeSymbol declaringType) {
        string name = injection.InvokeMethodName!;
        context.ReportDiagnostic(Diagnostic.Create(
            AmbiguousAccessorNameRule,
            ArgumentLocation(injection.Attribute, "invokeDeclaringMethod", context.CancellationToken) ??
            LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
            declaringType.ToDisplayString() + "." + name,
            "get_" + name,
            "set_" + name));
    }

    private static ITypeSymbol? ValueInjectionType(InjectionInfo injection) {
        if (injection.HasConstant) {
            return injection.ConstantValue?.Type;
        }

        if (injection.Local is not null) {
            return injection.Local.LocalType;
        }

        return ResolveArgumentValueType(injection);
    }

    private static void ValidateValueInjectionSignature(
        SymbolAnalysisContext context,
        InjectionInfo injection) {
        if (injection.TargetsInvoke && injection.AtValue == 5) {
            ValidateInvokeCallSiteName(context, injection);
        }

        ITypeSymbol? valueType = ValueInjectionType(injection);

        // A local sibling reads another slot rather than carrying the replaced value, so it is not
        // part of the 'T M(T original)' shape. A bare LocalHandle<T> carries no [Local] and is still
        // one. The runtime's CONC039 counts the same way.
        ImmutableArray<IParameterSymbol> valueParameters = injection.Method.Parameters
            .Where(parameter => !parameter.GetAttributes().Any(IsLocalAttribute) && !IsLocalHandleType(parameter.Type))
            .ToImmutableArray();

        if (valueType is not null) {
            if (valueParameters.Length != 1 ||
                !SymbolEqualityComparer.Default.Equals(valueParameters[0].Type, valueType) ||
                !SymbolEqualityComparer.Default.Equals(injection.Method.ReturnType, valueType)) {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidValueInjectionShapeRule,
                    LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
                    injection.Method.Name,
                    valueType.ToDisplayString()));
            }
        } else if (valueParameters.Length != 1 ||
                   !SymbolEqualityComparer.Default.Equals(valueParameters[0].Type, injection.Method.ReturnType)) {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidValueInjectionShapeRule,
                LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
                injection.Method.Name,
                valueParameters.Length == 1 ? valueParameters[0].Type.ToDisplayString() : "T"));
        }

        if (injection.TargetsInvoke && injection.AtValue == 5 && injection.Arg == 0 && injection.Method.Parameters.Length == 1) {
            ValidateArgumentInference(context, injection);
        }
    }

    private static void ValidateArgumentInference(SymbolAnalysisContext context, InjectionInfo injection) {
        IMethodSymbol? callSite = ResolveUniqueCallSite(injection, out bool ambiguousAccessor);
        if (callSite is null || ambiguousAccessor) {
            return;
        }

        ITypeSymbol valueType = injection.Method.Parameters[0].Type;
        int matches = callSite.Parameters.Count(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, valueType));

        if (matches != 1) {
            context.ReportDiagnostic(Diagnostic.Create(
                AmbiguousArgumentInjectionRule,
                LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
                injection.Method.Name,
                callSite.Name,
                valueType.ToDisplayString()));
        }
    }

    private static ITypeSymbol? ResolveArgumentValueType(InjectionInfo injection) {
        if (injection.Arg == 0) {
            return null;
        }

        IMethodSymbol? callSite = ResolveUniqueCallSite(injection, out bool ambiguousAccessor);
        if (callSite is null || ambiguousAccessor) {
            return null;
        }

        ImmutableArray<IParameterSymbol> parameters = callSite.Parameters;
        return injection.Arg <= parameters.Length ? parameters[(int)(injection.Arg - 1)].Type : null;
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

            AnalyzeProperty(context, patchType, targetType, property, injectAttribute);
        }
    }

    private static void AnalyzeProperty(
        SymbolAnalysisContext context,
        INamedTypeSymbol patchType,
        INamedTypeSymbol targetType,
        IPropertySymbol property,
        AttributeData injectAttribute) {
        string targetName = TargetName(injectAttribute, property.Name);
        IPropertySymbol? targetProperty = FindMember(
            targetType,
            type => type.GetMembers(targetName).OfType<IPropertySymbol>().Where(member => ParametersMatch(member.Parameters, property.Parameters)));

        if (targetProperty is null) {
            if (MetadataMemberExists(context.Compilation, targetType, targetName, MetadataMemberKind.Property)) {
                return;
            }

            ReportMissing(context, property, injectAttribute, targetName, targetType);
            return;
        }

        AnalyzeMemberNameStyle(context, patchType, targetProperty, injectAttribute, "targetName");

        if (!SymbolEqualityComparer.Default.Equals(targetProperty.Type, property.Type) || targetProperty.IsStatic != property.IsStatic) {
            ReportMismatch(
                context,
                property,
                injectAttribute,
                targetName,
                targetType,
                "property type, index parameters, and static-ness must match exactly");
        }

        ReportMissingAccessors(context, property, targetProperty, injectAttribute, targetName, targetType);
    }

    private static void ReportMissingAccessors(
        SymbolAnalysisContext context,
        IPropertySymbol property,
        IPropertySymbol targetProperty,
        AttributeData injectAttribute,
        string targetName,
        INamedTypeSymbol targetType) {
        if (property.GetMethod is not null && targetProperty.GetMethod is null) {
            ReportMissing(context, property, injectAttribute, targetName + ".get", targetType);
        }

        if (property.SetMethod is not null && targetProperty.SetMethod is null) {
            ReportMissing(context, property, injectAttribute, targetName + ".set", targetType);
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

            AnalyzeMemberNameStyle(context, patchType, targetMethod, injectAttribute, "targetName");

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

    // Roslyn's default MetadataImportOptions.Public leaves private members of a referenced
    // assembly out of the symbol model, which is why the metadata fallback exists at all.
    // Reaching private fields is the whole point of [InjectField], so the type check has to read
    // the field's signature straight out of metadata rather than skipping validation.
    private static string? MetadataFieldTypeName(
        Compilation compilation,
        INamedTypeSymbol targetType,
        string targetName,
        out bool isStatic) {
        isStatic = false;
        string targetMetadataName = MetadataName(targetType);

        foreach (PortableExecutableReference reference in compilation.References.OfType<PortableExecutableReference>()) {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly ||
                !assembly.Identity.Equals(targetType.ContainingAssembly.Identity)) {
                continue;
            }

            if (reference.GetMetadata() is not AssemblyMetadata assemblyMetadata) {
                continue;
            }

            foreach (ModuleMetadata module in assemblyMetadata.GetModules()) {
                string? signature = ModuleFieldTypeName(module, targetMetadataName, targetName, out isStatic);
                if (signature is not null) {
                    return signature;
                }
            }
        }

        return null;
    }

    // Walks one module for the target type definition, then that type's fields for the named one,
    // decoding the field's type straight out of its metadata signature.
    private static string? ModuleFieldTypeName(ModuleMetadata module, string targetMetadataName, string targetName, out bool isStatic) {
        isStatic = false;
        MetadataReader reader = module.GetMetadataReader();

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions) {
            if (TypeDefinitionMetadataName(reader, handle) != targetMetadataName) {
                continue;
            }

            TypeDefinition definition = reader.GetTypeDefinition(handle);
            foreach (FieldDefinitionHandle fieldHandle in definition.GetFields()) {
                FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                if (reader.GetString(field.Name) != targetName) {
                    continue;
                }

                isStatic = (field.Attributes & System.Reflection.FieldAttributes.Static) != 0;
                return field.DecodeSignature(new MetadataTypeNameProvider(), null);
            }
        }

        return null;
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

    private static void ReportInvalidPatchOrdering(
        SymbolAnalysisContext context,
        INamedTypeSymbol patchType,
        AttributeData attribute,
        string reason) {
        string attributeName = IsPatchBeforeAttribute(attribute) ? "[PatchBefore]" : "[PatchAfter]";
        context.ReportDiagnostic(Diagnostic.Create(
            InvalidPatchOrderingRule,
            LocationOf(attribute, patchType, context.CancellationToken),
            attributeName,
            reason));
    }

    private static void ReportMissingInjectionTarget(
        SymbolAnalysisContext context,
        InjectionInfo injection,
        INamedTypeSymbol targetType) {
        context.ReportDiagnostic(Diagnostic.Create(
            MissingInjectionTargetRule,
            LocationOf(injection.Attribute, injection.Method, context.CancellationToken),
            injection.Method.Name,
            injection.TargetMemberName,
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
            injection.TargetMemberName,
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
            first.TargetMemberName,
            targetType.ToDisplayString(),
            PositionName(first)));
    }

    private static void ReportUnsupported(
        SymbolAnalysisContext context,
        ISymbol patchMember,
        AttributeData attribute,
        string reason) {
        context.ReportDiagnostic(Diagnostic.Create(
            UnsupportedDeclarationFormRule,
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
            !compilation.IsSymbolAccessibleWithin(targetType, patchType) ||
            !HasSubclassAccessibleConstructor(compilation, patchType, targetType)) {
            return false;
        }

        return IsObject(patchType.BaseType) || SymbolEqualityComparer.Default.Equals(patchType.BaseType, targetType);
    }

    private static bool HasSubclassAccessibleConstructor(Compilation compilation, INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
        foreach (IMethodSymbol ctor in targetType.InstanceConstructors) {
            if (ctor.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal) {
                return true;
            }

            if (ctor.DeclaredAccessibility == Accessibility.ProtectedAndInternal &&
                SymbolEqualityComparer.Default.Equals(ctor.ContainingAssembly, patchType.ContainingAssembly)) {
                return true;
            }

            if (compilation.IsSymbolAccessibleWithin(ctor, patchType)) {
                return true;
            }
        }

        return false;
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

    // localKey is built in TryGetInjectionInfo, which is a long way up this file. It is "*" for every
    // position but At.Local, where it separates two locals that differ only in type, access or selector.
    private static string DuplicateKey(
        string targetName,
        bool targetsConstructor,
        bool targetsInvoke,
        int atValue,
        uint by,
        ImmutableArray<ITypeSymbol>? parameterTypes,
        string localKey) {
        string parameters = parameterTypes.HasValue
            ? string.Join(",", parameterTypes.Value.Select(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            : "*";

        return targetName + "|" + targetsConstructor + "|" + targetsInvoke + "|" + atValue + "|" + by + "|" + parameters + "|" + localKey;
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
            named.ContainingNamespace.ToDisplayString() != ConcordNamespace) {
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

    private static bool IsControlType(ITypeSymbol? type) {
        return type is INamedTypeSymbol named
            && named.Name == "Control"
            && named.ContainingNamespace?.ToDisplayString() == ConcordNamespace;
    }

    private static bool IsOperationType(ITypeSymbol type) {
        if (type is not INamedTypeSymbol named ||
            named.ContainingNamespace.ToDisplayString() != ConcordNamespace) {
            return false;
        }

        if (named.Name == OperationTypeName) {
            return !named.IsGenericType || named.TypeArguments.Length is >= 1 and <= 9;
        }

        if (named.Name == "VoidOperation") {
            return named.IsGenericType && named.TypeArguments.Length is >= 1 and <= 8;
        }

        return false;
    }

    private static bool IsVoid(ITypeSymbol type) {
        return type.SpecialType == SpecialType.System_Void;
    }

    private static bool IsVoidOperationFamily(ITypeSymbol type) {
        return type is INamedTypeSymbol named &&
               named.ContainingNamespace.ToDisplayString() == ConcordNamespace &&
               named.Name == "VoidOperation" &&
               named.IsGenericType;
    }

    private static IParameterSymbol? FindOperationParameter(IMethodSymbol method) {
        return method.Parameters.FirstOrDefault(parameter => IsOperationType(parameter.Type));
    }

    private static string? ResolveAccessorName(
        INamedTypeSymbol declaringType,
        string name,
        IMethodSymbol? injectionMethod,
        bool allowOperationDisambiguation,
        out bool ambiguous) {
        ambiguous = false;

        if (declaringType.GetMembers(name).OfType<IMethodSymbol>().Any(candidate => candidate.MethodKind == MethodKind.Ordinary)) {
            return name;
        }

        IPropertySymbol? property = declaringType.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();
        if (property is null) {
            return name;
        }

        bool hasGetter = property.GetMethod is not null;
        bool hasSetter = property.SetMethod is not null;

        if (hasGetter && !hasSetter) {
            return "get_" + name;
        }

        if (hasSetter && !hasGetter) {
            return "set_" + name;
        }

        if (allowOperationDisambiguation && injectionMethod is not null) {
            IParameterSymbol? operationParameter = FindOperationParameter(injectionMethod);
            if (operationParameter is not null) {
                return IsVoidOperationFamily(operationParameter.Type) ? "set_" + name : "get_" + name;
            }
        }

        ambiguous = true;
        return null;
    }

    private static IMethodSymbol? ResolveUniqueCallSite(InjectionInfo injection, out bool ambiguousAccessor) {
        ambiguousAccessor = false;

        if (injection.InvokeDeclaringType is not INamedTypeSymbol invokeDeclaringType || injection.InvokeMethodName is null) {
            return null;
        }

        bool allowOperationDisambiguation = injection.AtValue == 3;
        string? effectiveName = ResolveAccessorName(
            invokeDeclaringType,
            injection.InvokeMethodName,
            injection.Method,
            allowOperationDisambiguation,
            out ambiguousAccessor);

        if (effectiveName is null) {
            return null;
        }

        ImmutableArray<IMethodSymbol> candidates = FindMethodCandidates(invokeDeclaringType, effectiveName)
            .Where(method => ParameterTypesMatch(injection.InvokeParameterTypes, method.Parameters))
            .ToImmutableArray();

        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static string ExpectedOperationTypeName(ImmutableArray<ITypeSymbol> parameterTypes, ITypeSymbol returnType) {
        bool isVoid = IsVoid(returnType);
        string[] displayNames = parameterTypes.Select(type => type.ToDisplayString()).ToArray();

        if (isVoid) {
            return displayNames.Length switch {
                0 => OperationTypeName,
                1 => VoidOperationPrefix + displayNames[0] + ">",
                2 => VoidOperationPrefix + string.Join(", ", displayNames) + ">",
                3 => VoidOperationPrefix + string.Join(", ", displayNames) + ">",
                4 => VoidOperationPrefix + string.Join(", ", displayNames) + ">",
                5 => VoidOperationPrefix + string.Join(", ", displayNames) + ">",
                6 => VoidOperationPrefix + string.Join(", ", displayNames) + ">",
                7 => VoidOperationPrefix + string.Join(", ", displayNames) + ">",
                8 => VoidOperationPrefix + string.Join(", ", displayNames) + ">",
                _ => "VoidOperation<...>",
            };
        }

        string[] withResult = displayNames.Concat(new[] { returnType.ToDisplayString() }).ToArray();
        return displayNames.Length switch {
            0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 => OperationTypeName + "<" + string.Join(", ", withResult) + ">",
            _ => "Operation<...>",
        };
    }

    private static bool OperationTypeMatchesExpected(ITypeSymbol declared, ImmutableArray<ITypeSymbol> parameterTypes, ITypeSymbol returnType) {
        if (declared is not INamedTypeSymbol named) {
            return false;
        }

        return IsVoid(returnType)
            ? VoidOperationTypeMatchesExpected(named, parameterTypes)
            : ValueOperationTypeMatchesExpected(named, parameterTypes, returnType);
    }

    private static bool VoidOperationTypeMatchesExpected(INamedTypeSymbol named, ImmutableArray<ITypeSymbol> parameterTypes) {
        if (parameterTypes.Length == 0) {
            return named.Name == OperationTypeName && !named.IsGenericType;
        }

        if (named.Name != "VoidOperation" || named.TypeArguments.Length != parameterTypes.Length) {
            return false;
        }

        return OperationTypeArgumentsMatch(named, parameterTypes);
    }

    private static bool ValueOperationTypeMatchesExpected(INamedTypeSymbol named, ImmutableArray<ITypeSymbol> parameterTypes, ITypeSymbol returnType) {
        if (named.Name != OperationTypeName || named.TypeArguments.Length != parameterTypes.Length + 1) {
            return false;
        }

        return OperationTypeArgumentsMatch(named, parameterTypes) &&
            SymbolEqualityComparer.Default.Equals(named.TypeArguments[parameterTypes.Length], returnType);
    }

    private static bool OperationTypeArgumentsMatch(INamedTypeSymbol named, ImmutableArray<ITypeSymbol> parameterTypes) {
        for (int i = 0; i < parameterTypes.Length; i++) {
            if (!SymbolEqualityComparer.Default.Equals(named.TypeArguments[i], parameterTypes[i])) {
                return false;
            }
        }

        return true;
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

    private static bool IsPatchOrderingAttribute(AttributeData attribute) {
        return IsPatchBeforeAttribute(attribute) || IsPatchAfterAttribute(attribute);
    }

    private static bool IsPatchBeforeAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "PatchBeforeAttribute");
    }

    private static bool IsPatchAfterAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "PatchAfterAttribute");
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

    private static bool IsEnumMemberAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "EnumMemberAttribute");
    }

    private static INamedTypeSymbol? GetExtendedEnumType(INamedTypeSymbol declaration) {
        for (INamedTypeSymbol? current = declaration.BaseType; current is not null; current = current.BaseType) {
            if (current.Name != "ExtendedEnum" ||
                current.Arity != 1 ||
                current.ContainingNamespace?.ToDisplayString() != ConcordNamespace) {
                continue;
            }

            return current.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    private static void AnalyzeExtendedEnumDeclaration(
        SymbolAnalysisContext context,
        INamedTypeSymbol declaration,
        INamedTypeSymbol enumType) {
        if (!declaration.GetAttributes().Any(IsPatchAttribute)) {
            return;
        }

        foreach (IMethodSymbol method in declaration.GetMembers().OfType<IMethodSymbol>()) {
            if (!method.GetAttributes().Any(IsInjectionAttribute)) {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                EnumDeclarationInjectionRule,
                method.Locations.FirstOrDefault() ?? Location.None,
                declaration.Name,
                method.Name));
        }

        List<IFieldSymbol> members = new List<IFieldSymbol>();

        foreach (IFieldSymbol field in declaration.GetMembers().OfType<IFieldSymbol>()) {
            bool typed = SymbolEqualityComparer.Default.Equals(field.Type, enumType);
            bool tagged = field.GetAttributes().Any(IsEnumMemberAttribute);

            if (!typed || !field.IsStatic) {
                if (tagged) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidEnumMemberFieldRule,
                        field.Locations.FirstOrDefault() ?? Location.None,
                        declaration.Name + "." + field.Name,
                        enumType.Name));
                }

                continue;
            }

            members.Add(field);

            if (!field.IsConst && HasInitializer(field)) {
                context.ReportDiagnostic(Diagnostic.Create(
                    NonConstEnumMemberInitializerRule,
                    field.Locations.FirstOrDefault() ?? Location.None,
                    declaration.Name + "." + field.Name));
            }
        }
    }

    private static bool HasInitializer(IFieldSymbol field) {
        foreach (SyntaxReference reference in field.DeclaringSyntaxReferences) {
            if (reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null }) {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeEnumMemberIds(CompilationStartAnalysisContext context) {
        ConcurrentBag<(string Id, string Owner, Location Location)> declared = [];

        context.RegisterSymbolAction(
            symbolContext => CollectEnumMemberIds(symbolContext, declared),
            SymbolKind.NamedType);

        context.RegisterOperationAction(ReportEnumMemberReadBeforeApply, OperationKind.FieldReference);

        context.RegisterCompilationEndAction(endContext => ReportDuplicateEnumMemberIds(endContext, declared));
    }

    private static void ReportEnumMemberReadBeforeApply(OperationAnalysisContext context) {
        if (context.Operation is not IFieldReferenceOperation reference ||
            context.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.StaticConstructor } constructor) {
            return;
        }

        INamedTypeSymbol declaration = constructor.ContainingType;
        if (!declaration.GetAttributes().Any(IsPatchAttribute) ||
            GetExtendedEnumType(declaration) is not INamedTypeSymbol enumType) {
            return;
        }

        IFieldSymbol field = reference.Field;
        if (!field.IsStatic ||
            field.IsConst ||
            !SymbolEqualityComparer.Default.Equals(field.ContainingType, declaration) ||
            !SymbolEqualityComparer.Default.Equals(field.Type, enumType)) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EnumMemberReadBeforeApplyRule,
            reference.Syntax.GetLocation(),
            declaration.Name + "." + field.Name));
    }

    private static void CollectEnumMemberIds(
        SymbolAnalysisContext context,
        ConcurrentBag<(string Id, string Owner, Location Location)> declared) {
        if (context.Symbol is not INamedTypeSymbol declaration ||
            declaration.TypeKind != TypeKind.Class ||
            !declaration.GetAttributes().Any(IsPatchAttribute)) {
            return;
        }

        if (GetExtendedEnumType(declaration) is not INamedTypeSymbol enumType) {
            return;
        }

        foreach (IFieldSymbol field in declaration.GetMembers().OfType<IFieldSymbol>()) {
            if (!field.IsStatic || !SymbolEqualityComparer.Default.Equals(field.Type, enumType)) {
                continue;
            }

            string id = ReadEnumMemberId(field) ?? declaration.ToDisplayString() + "." + field.Name;
            declared.Add((id, declaration.Name + "." + field.Name, field.Locations.FirstOrDefault() ?? Location.None));
        }
    }

    private static string? ReadEnumMemberId(IFieldSymbol field) {
        foreach (AttributeData attribute in field.GetAttributes()) {
            if (!IsEnumMemberAttribute(attribute) || attribute.ConstructorArguments.Length != 1) {
                continue;
            }

            return attribute.ConstructorArguments[0].Value as string;
        }

        return null;
    }

    private static void ReportDuplicateEnumMemberIds(
        CompilationAnalysisContext context,
        ConcurrentBag<(string Id, string Owner, Location Location)> declared) {
        List<(string Id, string Owner, Location Location)> ordered = declared
            .OrderBy(entry => entry.Location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Location.SourceSpan.Start)
            .ToList();

        Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string id, string owner, Location location) in ordered) {
            if (seen.TryGetValue(id, out string? first)) {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateEnumMemberIdRule, location, id, first));
                continue;
            }

            seen.Add(id, owner);
        }
    }

    private static bool IsInjectNewAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "InjectNewAttribute");
    }

    private static bool IsInjectionAttribute(AttributeData attribute) {
        return IsInjectAttribute(attribute) || IsInjectNewAttribute(attribute);
    }

    private static bool IsCaptureAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "CaptureAttribute");
    }

    private static bool IsLocalAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "LocalAttribute");
    }

    private static bool IsSliceAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "SliceAttribute");
    }

    private static bool IsInjectInstanceAttribute(AttributeData attribute) {
        return IsConcordAttribute(attribute, "InjectInstanceAttribute");
    }

    private static bool IsConcordAttribute(AttributeData attribute, string name) {
        INamedTypeSymbol? attributeClass = attribute.AttributeClass;
        if (attributeClass is null) {
            return false;
        }

        return attributeClass.Name == name &&
               attributeClass.ContainingNamespace.ToDisplayString() == ConcordNamespace;
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107", Justification = "Immutable data carrier for a resolved injection. Every parameter maps to a distinct read-only property, so bundling would only add indirection.")]
        public InjectionInfo(
            IMethodSymbol method,
            AttributeData attribute,
            string targetName,
            bool targetsConstructor,
            bool targetsInvoke,
            int atValue,
            uint by,
            ImmutableArray<ITypeSymbol>? parameterTypes,
            string duplicateKey,
            bool hasConstant,
            TypedConstant? constantValue,
            ITypeSymbol? invokeDeclaringType,
            string? invokeMethodName,
            ImmutableArray<ITypeSymbol>? invokeParameterTypes,
            uint arg) {
            Method = method;
            Attribute = attribute;
            TargetMemberName = targetName;
            TargetsConstructor = targetsConstructor;
            TargetsInvoke = targetsInvoke;
            AtValue = atValue;
            By = by;
            ParameterTypes = parameterTypes;
            DuplicateKeyValue = duplicateKey;
            HasConstant = hasConstant;
            ConstantValue = constantValue;
            InvokeDeclaringType = invokeDeclaringType;
            InvokeMethodName = invokeMethodName;
            InvokeParameterTypes = invokeParameterTypes;
            Arg = arg;
        }

        public IMethodSymbol Method { get; }

        public AttributeData Attribute { get; }

        public string TargetMemberName { get; }

        public bool TargetsConstructor { get; }

        public bool TargetsInvoke { get; }

        public int AtValue { get; }

        public uint By { get; }

        public ImmutableArray<ITypeSymbol>? ParameterTypes { get; }

        public string DuplicateKeyValue { get; }

        public bool HasConstant { get; }

        public TypedConstant? ConstantValue { get; }

        public ITypeSymbol? InvokeDeclaringType { get; }

        public string? InvokeMethodName { get; }

        public ImmutableArray<ITypeSymbol>? InvokeParameterTypes { get; }

        public uint Arg { get; }

        /// <summary>
        ///     The At.Local position this declaration named, or null when it is not a local injection.
        /// </summary>
        public LocalPositionInfo? Local { get; set; }
    }

    private sealed class LocalPositionInfo {
        public LocalPositionInfo(ITypeSymbol? localType, uint ordinal, int index, string? name) {
            LocalType = localType;
            Ordinal = ordinal;
            Index = index;
            Name = name;
        }

        public ITypeSymbol? LocalType { get; }

        public uint Ordinal { get; }

        public int Index { get; }

        public string? Name { get; }
    }

    private sealed class InjectionTarget {
        public InjectionTarget(
            string name,
            bool isStatic,
            ITypeSymbol? returnType,
            ImmutableArray<IParameterSymbol> parameters,
            bool signatureValidated,
            IMethodSymbol? methodSymbol = null) {
            Name = name;
            IsStatic = isStatic;
            ReturnType = returnType;
            Parameters = parameters;
            SignatureValidated = signatureValidated;
            MethodSymbol = methodSymbol;
        }

        public string Name { get; }

        public bool IsStatic { get; }

        public ITypeSymbol? ReturnType { get; }

        public ImmutableArray<IParameterSymbol> Parameters { get; }

        public bool SignatureValidated { get; }

        public IMethodSymbol? MethodSymbol { get; }
    }

    // Produces the same Namespace.Outer+Inner shape as MetadataName so the two sides compare
    // directly. Shapes it cannot name confidently yield null, and the caller then skips the
    // check rather than reporting a mismatch it is not sure about.
    private sealed class MetadataTypeNameProvider : ISignatureTypeProvider<string?, object?> {
        public string? GetPrimitiveType(PrimitiveTypeCode typeCode) {
            return typeCode switch {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.Void => "System.Void",
                PrimitiveTypeCode.TypedReference => "System.TypedReference",
                _ => null,
            };
        }

        public string? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) {
            return TypeDefinitionMetadataName(reader, handle);
        }

        public string? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) {
            TypeReference reference = reader.GetTypeReference(handle);
            string name = reader.GetString(reference.Name);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference) {
                string? declaring = GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, 0);
                return declaring is null ? null : declaring + "+" + name;
            }

            string ns = reader.GetString(reference.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        public string? GetSZArrayType(string? elementType) {
            return elementType is null ? null : elementType + "[]";
        }

        public string? GetArrayType(string? elementType, ArrayShape shape) {
            return elementType is null ? null : elementType + "[" + new string(',', shape.Rank - 1) + "]";
        }

        public string? GetByReferenceType(string? elementType) {
            return elementType is null ? null : elementType + "&";
        }

        public string? GetPointerType(string? elementType) {
            return elementType is null ? null : elementType + "*";
        }

        public string? GetGenericInstantiation(string? genericType, ImmutableArray<string?> typeArguments) {
            if (genericType is null) {
                return null;
            }

            foreach (string? argument in typeArguments) {
                if (argument is null) {
                    return null;
                }
            }

            return genericType + "[" + string.Join(",", typeArguments) + "]";
        }

        public string? GetPinnedType(string? elementType) {
            return elementType;
        }

        public string? GetModifiedType(string? modifier, string? unmodifiedType, bool isRequired) {
            return unmodifiedType;
        }

        // A field signature never carries these, and naming one anyway would risk reporting a
        // mismatch against a shape this provider cannot actually describe.
        public string? GetFunctionPointerType(MethodSignature<string?> signature) {
            return null;
        }

        public string? GetGenericMethodParameter(object? genericContext, int index) {
            return null;
        }

        public string? GetGenericTypeParameter(object? genericContext, int index) {
            return null;
        }

        public string? GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
    }

    private sealed class ParameterValidationState {
        public bool HasControlHandle { get; set; }

        public bool HasOperation { get; set; }

        public int ControlHandleCount { get; set; }

        public int OperationCount { get; set; }

        public IParameterSymbol? OperationParameter { get; set; }
    }

    private sealed class InjectionDeclaration {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107", Justification = "Immutable data carrier for a resolved injection position. Every parameter maps to a distinct read-only property, so bundling would only add indirection.")]
        public InjectionDeclaration(
            IMethodSymbol method,
            bool targetsCallSite,
            bool targetsNewObj,
            string positionName,
            bool targetsConstructor,
            string targetMemberName,
            ImmutableArray<ITypeSymbol>? parameterTypes,
            ITypeSymbol? callSiteType,
            string? callSiteMember,
            ImmutableArray<ITypeSymbol>? callSiteParameterTypes) {
            Method = method;
            TargetsCallSite = targetsCallSite;
            TargetsNewObj = targetsNewObj;
            AtName = positionName;
            TargetsConstructor = targetsConstructor;
            TargetMemberName = targetMemberName;
            ParameterTypes = parameterTypes;
            CallSiteType = callSiteType;
            CallSiteMember = callSiteMember;
            CallSiteParameterTypes = callSiteParameterTypes;
        }

        public IMethodSymbol Method { get; }

        public bool TargetsCallSite { get; }

        public bool TargetsNewObj { get; }

        public string AtName { get; }

        public bool TargetsConstructor { get; }

        public string TargetMemberName { get; }

        public ImmutableArray<ITypeSymbol>? ParameterTypes { get; }

        public ITypeSymbol? CallSiteType { get; }

        public string? CallSiteMember { get; }

        public ImmutableArray<ITypeSymbol>? CallSiteParameterTypes { get; }
    }

    private sealed class InjectionTargetIdentity {
        public InjectionTargetIdentity(string key, string display) {
            Key = key;
            Display = display;
        }

        public string Key { get; }

        public string Display { get; }
    }

    private sealed class StateCall {
        public StateCall(IMethodSymbol method, bool isWrite, ITypeSymbol stateType, Location location) {
            Method = method;
            IsWrite = isWrite;
            StateType = stateType;
            Location = location;
        }

        public IMethodSymbol Method { get; }

        public bool IsWrite { get; }

        public ITypeSymbol StateType { get; }

        public Location Location { get; }
    }

    private sealed class StateSlot {
        private readonly string targetDisplay;
        private ITypeSymbol? stateType;
        private StateCall? firstRead;
        private bool written;

        public StateSlot(string targetDisplay) {
            this.targetDisplay = targetDisplay;
        }

        public void Observe(SymbolAnalysisContext context, INamedTypeSymbol patchType, StateCall call) {
            if (stateType is null) {
                stateType = call.StateType;
            } else if (!SymbolEqualityComparer.Default.Equals(stateType, call.StateType)) {
                context.ReportDiagnostic(Diagnostic.Create(
                    ConflictingStateTypeRule,
                    call.Location,
                    patchType.Name,
                    stateType.ToDisplayString(),
                    call.StateType.ToDisplayString(),
                    targetDisplay));
            }

            if (call.IsWrite) {
                written = true;
            } else if (firstRead is null) {
                firstRead = call;
            }
        }

        public void ReportUnwritten(SymbolAnalysisContext context, INamedTypeSymbol patchType) {
            if (written || firstRead is null) {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                UnwrittenStateSlotRule,
                firstRead.Location,
                patchType.Name,
                firstRead.StateType.ToDisplayString(),
                targetDisplay));
        }
    }

    // Gathers every ControlHandle state call across one patch declaration's injection methods, then
    // reports once the whole type has been analyzed. Collection order follows Roslyn's scheduling,
    // so the calls are sorted by source position before the slot rules run.
    private sealed class StateSlotCollector {
        private readonly INamedTypeSymbol patchType;
        private readonly INamedTypeSymbol targetType;
        private readonly List<StateCall> calls = new List<StateCall>();

        public StateSlotCollector(INamedTypeSymbol patchType, INamedTypeSymbol targetType) {
            this.patchType = patchType;
            this.targetType = targetType;
        }

        public void Collect(SyntaxNodeAnalysisContext context) {
            if (context.ContainingSymbol is not IMethodSymbol method ||
                method.MethodKind != MethodKind.Ordinary ||
                !SymbolEqualityComparer.Default.Equals(method.ContainingType, patchType) ||
                !method.GetAttributes().Any(IsInjectionAttribute)) {
                return;
            }

            if (context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not IMethodSymbol call ||
                call.TypeArguments.Length != 1 ||
                !IsControlHandleType(call.ContainingType, out _)) {
                return;
            }

            bool isWrite = call.Name == "SetState";
            if (!isWrite && call.Name != "GetState") {
                return;
            }

            StateCall state = new StateCall(method, isWrite, call.TypeArguments[0], context.Node.GetLocation());
            lock (calls) {
                calls.Add(state);
            }
        }

        public void Report(SymbolAnalysisContext context) {
            List<StateCall> ordered;
            lock (calls) {
                if (calls.Count == 0) {
                    return;
                }

                ordered = new List<StateCall>(calls);
            }

            ordered.Sort(CompareBySourcePosition);

            Dictionary<string, StateSlot> slots = new Dictionary<string, StateSlot>(StringComparer.Ordinal);
            List<StateSlot> order = new List<StateSlot>();
            foreach (StateCall call in ordered) {
                InjectionDeclaration? declaration = TryGetInjectionDeclaration(call.Method);
                if (declaration is null) {
                    continue;
                }

                InjectionTargetIdentity identity = TargetIdentity(declaration, targetType);
                if (!slots.TryGetValue(identity.Key, out StateSlot? slot)) {
                    slot = new StateSlot(identity.Display);
                    slots[identity.Key] = slot;
                    order.Add(slot);
                }

                slot.Observe(context, patchType, call);
            }

            foreach (StateSlot slot in order) {
                slot.ReportUnwritten(context, patchType);
            }
        }

        // A state slot is scoped per patch declaration per target method, matching how
        // AllocateStateLocals keys its declared-type map inside a single target's injection list.
        // The constructor branch matches parameter types the way ResolveInjectionTarget does, so an
        // absent parameterTypes selects the parameterless constructor rather than every constructor;
        // otherwise two injections that name the same constructor land in two slots.
        private static InjectionTargetIdentity TargetIdentity(InjectionDeclaration declaration, INamedTypeSymbol targetType) {
            ImmutableArray<IMethodSymbol> candidates = declaration.TargetsConstructor
                ? targetType.InstanceConstructors
                    .Where(constructor => ConstructorParameterTypesMatch(declaration.ParameterTypes, constructor.Parameters))
                    .ToImmutableArray()
                : FindMethodCandidates(targetType, declaration.TargetMemberName)
                    .Where(candidate => ParameterTypesMatch(declaration.ParameterTypes, candidate.Parameters))
                    .ToImmutableArray();

            if (candidates.Length == 1) {
                return new InjectionTargetIdentity(
                    candidates[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    candidates[0].ToDisplayString());
            }

            string parameters = declaration.ParameterTypes.HasValue
                ? string.Join(",", declaration.ParameterTypes.Value.Select(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                : "*";
            return new InjectionTargetIdentity(
                declaration.TargetMemberName + "|" + parameters,
                targetType.ToDisplayString() + "." + declaration.TargetMemberName);
        }

        private static int CompareBySourcePosition(StateCall left, StateCall right) {
            int byPath = string.CompareOrdinal(
                left.Location.SourceTree?.FilePath ?? string.Empty,
                right.Location.SourceTree?.FilePath ?? string.Empty);
            return byPath != 0 ? byPath : left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start);
        }
    }
}
