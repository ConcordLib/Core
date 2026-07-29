using Concord.Emit;

namespace Concord;

/// <summary>
///     Marks an injection method as targeting an object construction inside a target method.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InjectNewAttribute : Attribute {
    private readonly uint by;
    private readonly Type[]? invokeParameterTypes;
    private readonly uint arg;

    /// <summary>
    ///     Initializes a new construction-site injection declaration on a named target method.
    /// </summary>
    /// <param name="method">The target method name. Private names are validated by Concord tooling.</param>
    /// <param name="constructedType">The type whose construction the injection targets.</param>
    /// <param name="shift">Where the injection method runs relative to the construction.</param>
    /// <param name="by">The 1-based occurrence to target, or <c>0</c> for every match.</param>
    /// <param name="targetParameterTypes">Selects one overload of <paramref name="method" /> by parameter types.</param>
    /// <param name="invokeParameterTypes">Restricts matches to one constructor overload by parameter types.</param>
    /// <param name="arg">For <see cref="At.Argument" />, the 1-based argument to rewrite, or <c>0</c> to infer by unique type match.</param>
    public InjectNewAttribute(string method, Type constructedType, At shift, uint by = 0, Type[]? targetParameterTypes = null, Type[]? invokeParameterTypes = null, uint arg = 0) {
        Method = method;
        ConstructedType = constructedType;
        At = shift;
        this.by = by;
        ParameterTypes = targetParameterTypes;
        this.invokeParameterTypes = invokeParameterTypes;
        this.arg = arg;
        TargetsConstructor = false;
    }

    /// <summary>
    ///     Initializes a new construction-site injection declaration on the patch target's instance constructor.
    /// </summary>
    /// <param name="constructedType">The type whose construction the injection targets.</param>
    /// <param name="shift">Where the injection method runs relative to the construction.</param>
    /// <param name="by">The 1-based occurrence to target, or <c>0</c> for every match.</param>
    /// <param name="targetParameterTypes">The parameter types of the target constructor overload.</param>
    /// <param name="invokeParameterTypes">Restricts matches to one constructor overload by parameter types.</param>
    /// <param name="arg">For <see cref="At.Argument" />, the 1-based argument to rewrite, or <c>0</c> to infer by unique type match.</param>
    public InjectNewAttribute(Type constructedType, At shift, uint by = 0, Type[]? targetParameterTypes = null, Type[]? invokeParameterTypes = null, uint arg = 0) {
        Method = null;
        ConstructedType = constructedType;
        At = shift;
        this.by = by;
        ParameterTypes = targetParameterTypes;
        this.invokeParameterTypes = invokeParameterTypes;
        this.arg = arg;
        TargetsConstructor = true;
    }

    /// <summary>
    ///     The target method name this injection attaches to, or <see langword="null" /> when
    ///     <see cref="TargetsConstructor" /> is <see langword="true" />.
    /// </summary>
    public string? Method { get; }

    /// <summary>
    ///     The type whose construction the injection targets.
    /// </summary>
    public Type ConstructedType { get; }

    /// <summary>
    ///     The injection position supplied to the declaration.
    /// </summary>
    public At At { get; }

    /// <summary>
    ///     The parameter types used to disambiguate the target method or constructor overload.
    /// </summary>
    public Type[]? ParameterTypes { get; }

    /// <summary>
    ///     Whether this injection targets the declaring base type's instance constructor.
    /// </summary>
    public bool TargetsConstructor { get; }

    /// <summary>
    ///     The ordering priority for this injection when several target the same method.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///     Which body this injection attaches to when the target is an async or iterator method.
    /// </summary>
    public PatchBody Body { get; set; } = PatchBody.Declared;

    /// <summary>
    ///     The <see cref="InjectAt" /> corresponding to this declaration.
    /// </summary>
    internal InjectAt ResolvedAt => new InjectAt.NewObj(ConstructedType, At, by, invokeParameterTypes, arg);

    /// <summary>
    ///     The priority value as resolved from the <see cref="Priority" /> property.
    /// </summary>
    internal int ResolvedPriority => Priority;
}
