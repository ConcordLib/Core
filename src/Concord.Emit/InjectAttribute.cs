using Concord.Emit;

namespace Concord;

/// <summary>
///     Marks an injection method as an injection for a named target method.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InjectAttribute : Attribute {
    private readonly Type? invokeDeclaringType;
    private readonly string? invokeMethod;
    private readonly At invokeShift;
    private readonly uint by;
    private readonly Type[]? invokeParameterTypes;

    /// <summary>
    ///     Initializes a new injection declaration at the given position.
    /// </summary>
    /// <param name="at">The injection position: <see cref="At.Head" />, <see cref="At.Return" />, <see cref="At.Tail" />, or <see cref="At.Around" />.</param>
    /// <param name="method">The target method name. Private names are validated by Concord tooling.</param>
    /// <param name="by">
    ///     For <see cref="At.Return" />, the 1-based occurrence of the return to target, or <c>0</c>
    ///     for every return. Ignored for other positions.
    /// </param>
    /// <param name="parameterTypes">
    ///     When non-<see langword="null" />, selects a specific overload of <paramref name="method" /> by
    ///     parameter types, allowing ambiguous overloads to be disambiguated.
    /// </param>
    public InjectAttribute(At at, string method, uint by = 0, Type[]? parameterTypes = null) {
        At = at;
        Method = method;
        this.by = by;
        ParameterTypes = parameterTypes;
        TargetsConstructor = false;
    }

    /// <summary>
    ///     Initializes a new injection declaration targeting the instance constructor of the patch target.
    /// </summary>
    /// <param name="at">The injection position: <see cref="At.Head" />, <see cref="At.Return" />, <see cref="At.Tail" />, or <see cref="At.Around" />.</param>
    /// <param name="parameterTypes">
    ///     The parameter types of the constructor overload to target, or <see langword="null" /> to select
    ///     the parameterless constructor. Only instance constructors are supported.
    /// </param>
    public InjectAttribute(At at, Type[]? parameterTypes = null) {
        At = at;
        Method = null;
        ParameterTypes = parameterTypes;
        TargetsConstructor = true;
    }

    /// <summary>
    ///     Initializes a new injection declaration at an invoke-splice position, targeting a call inside
    ///     the target method.
    /// </summary>
    /// <param name="method">The target method name. Private names are validated by Concord tooling.</param>
    /// <param name="invokeDeclaringType">The type that declares the call-site method to match.</param>
    /// <param name="invokeDeclaringMethod">The name of the call-site method to match.</param>
    /// <param name="shift">
    ///     Where the injection method runs relative to the matched call: <see cref="At.Head" /> before the
    ///     call, <see cref="At.Tail" /> after the call, or <see cref="At.Around" /> wrapping
    ///     the call (the injection method receives an <see cref="Operation" /> family handle matching the call's shape).
    /// </param>
    /// <param name="by">The 1-based occurrence of the matched call to target, or <c>0</c> for every matching call.</param>
    /// <param name="targetParameterTypes">
    ///     When non-<see langword="null" />, selects a specific overload of <paramref name="method" /> on the
    ///     patch target by parameter types.
    /// </param>
    /// <param name="invokeParameterTypes">
    ///     When non-<see langword="null" />, only call sites whose parameter types match this array are
    ///     considered when matching <paramref name="invokeDeclaringMethod" />.
    /// </param>
    public InjectAttribute(string method, Type invokeDeclaringType, string invokeDeclaringMethod, At shift, uint by = 0, Type[]? targetParameterTypes = null, Type[]? invokeParameterTypes = null) {
        Method = method;
        At = shift;
        this.invokeDeclaringType = invokeDeclaringType;
        this.invokeMethod = invokeDeclaringMethod;
        this.invokeShift = shift;
        this.by = by;
        ParameterTypes = targetParameterTypes;
        this.invokeParameterTypes = invokeParameterTypes;
        TargetsConstructor = false;
    }

    /// <summary>
    ///     Gets the target method name this injection attaches to, or <see langword="null" /> when
    ///     <see cref="TargetsConstructor" /> is <see langword="true" />.
    /// </summary>
    public string? Method { get; }

    /// <summary>
    ///     Gets the public injection-position value supplied to the declaration.
    /// </summary>
    public At At { get; }

    /// <summary>
    ///     Gets the parameter types used to disambiguate the target method overload or constructor overload, or
    ///     <see langword="null" /> when no disambiguation is needed.
    /// </summary>
    public Type[]? ParameterTypes { get; }

    /// <summary>
    ///     Gets a value indicating whether this injection targets the declaring base type's instance constructor
    ///     rather than a named method.
    /// </summary>
    public bool TargetsConstructor { get; }

    /// <summary>
    ///     Gets or sets the ordering priority for this injection when multiple injections target the same method.
    ///     Higher priority injections execute later (more outer) in tail chains. Defaults to 0.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    ///     Gets the <see cref="InjectAt" /> corresponding to this injection's position.
    ///     Returns <see cref="InjectAt.Invoke" /> when the invoke constructor was used;
    ///     otherwise maps from <see cref="At" />.
    /// </summary>
    internal InjectAt ResolvedAt {
        get {
            if (invokeDeclaringType is not null) {
                return new InjectAt.Invoke(invokeDeclaringType, invokeMethod!, invokeShift, by, invokeParameterTypes);
            }

            return At switch {
                Concord.At.Return => new InjectAt.Return(by),
                Concord.At.Tail => new InjectAt.Tail(),
                Concord.At.Around => new InjectAt.Around(),
                _ => new InjectAt.Head(),
            };
        }
    }

    /// <summary>
    ///     Gets the priority value as resolved from the <see cref="Priority" /> property.
    /// </summary>
    internal int ResolvedPriority => Priority;
}
