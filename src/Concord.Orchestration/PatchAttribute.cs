namespace Concord;

/// <summary>
///     Marks a type as a Concord patch declaration. Only types carrying this attribute are scanned by
///     <see cref="Orchestration.PatchDeclarationScanner" />: their <see cref="Concord.InjectAttribute" /> methods become
///     patches and their declared fields become attached properties. A declaration may carry zero
///     <see cref="Concord.InjectAttribute" /> methods (an attached-data-only declaration is valid).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PatchAttribute : Attribute {
    /// <summary>
    ///     Initializes a new declaration whose target base type is inferred from the declaration type's own base type.
    /// </summary>
    public PatchAttribute() { }

    /// <summary>
    ///     Initializes a new declaration whose target base type is given explicitly. Use this to target a
    ///     sealed or otherwise non-subclassable target type from a declaration that does not inherit from it.
    /// </summary>
    /// <param name="target">The target type the declaration patches.</param>
    public PatchAttribute(Type target) {
        Target = target;
    }

    /// <summary>
    ///     Initializes a new declaration whose target base type is resolved at runtime by full assembly-qualified
    ///     or namespace-qualified type name. Use this to target internal or inaccessible engine types where
    ///     <c>typeof(T)</c> cannot be written at the call site.
    /// </summary>
    /// <param name="targetTypeName">
    ///     The fully-qualified type name to resolve. Resolved first via <see cref="Type.GetType(string)" />, then
    ///     by scanning all assemblies in <see cref="AppDomain.CurrentDomain" />.
    /// </param>
    public PatchAttribute(string targetTypeName) {
        TargetTypeName = targetTypeName;
    }

    /// <summary>
    ///     Gets the explicit target base type, or <see langword="null" /> when inferred from the declaration type's base type.
    /// </summary>
    public Type? Target { get; }

    /// <summary>
    ///     Gets the fully-qualified name of the target type for late-bound resolution, or <see langword="null" />
    ///     when the target is given as a <see cref="Type" /> directly or inferred from the base class.
    /// </summary>
    public string? TargetTypeName { get; }
}
