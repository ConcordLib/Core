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
    private readonly uint arg;
    private readonly object? constant;

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
    ///     Initializes a new injection declaration targeting an inlined literal constant in the target body.
    /// </summary>
    /// <param name="method">The target method name. Private names are validated by Concord tooling.</param>
    /// <param name="constant">The literal to match. Supported kinds: int, long, float, double, string.</param>
    /// <param name="at">The injection position. Must be <see cref="At.Constant" />.</param>
    /// <param name="by">The 1-based occurrence of the constant to target, or <c>0</c> for every match.</param>
    /// <param name="parameterTypes">
    ///     When non-<see langword="null" />, selects a specific overload of <paramref name="method" /> by
    ///     parameter types, allowing ambiguous overloads to be disambiguated.
    /// </param>
    public InjectAttribute(string method, object constant, At at, uint by = 0, Type[]? parameterTypes = null) {
        Method = method;
        this.constant = constant;
        At = at;
        this.by = by;
        ParameterTypes = parameterTypes;
        TargetsConstructor = false;
    }

    /// <summary>
    ///     Initializes a new injection declaration targeting an inlined literal constant in the patch target's
    ///     instance constructor.
    /// </summary>
    /// <param name="constant">The literal to match. Supported kinds: int, long, float, double, string.</param>
    /// <param name="at">The injection position. Must be <see cref="At.Constant" />.</param>
    /// <param name="by">The 1-based occurrence of the constant to target, or <c>0</c> for every match.</param>
    /// <param name="parameterTypes">
    ///     The parameter types of the constructor overload to target, or <see langword="null" /> to select
    ///     the parameterless constructor. Only instance constructors are supported.
    /// </param>
    public InjectAttribute(object constant, At at, uint by = 0, Type[]? parameterTypes = null) {
        Method = null;
        this.constant = constant;
        At = at;
        this.by = by;
        ParameterTypes = parameterTypes;
        TargetsConstructor = true;
    }

    /// <summary>
    ///     Initializes a new injection declaration at an invoke-splice position, targeting a member access
    ///     inside the target method.
    /// </summary>
    /// <param name="method">The target method name. Private names are validated by Concord tooling.</param>
    /// <param name="invokeDeclaringType">The type that declares the member access to match.</param>
    /// <param name="invokeDeclaringMethod">The method, property, or field name to match.</param>
    /// <param name="shift">
    ///     Where the injection method runs relative to the matched access. Head and Tail support method calls,
    ///     property calls, and field reads. Around and Argument support method and property calls only.
    /// </param>
    /// <param name="by">The 1-based occurrence of the matched access to target, or <c>0</c> for every match.</param>
    /// <param name="targetParameterTypes">
    ///     When non-<see langword="null" />, selects a specific overload of <paramref name="method" /> on the
    ///     patch target by parameter types.
    /// </param>
    /// <param name="invokeParameterTypes">
    ///     For method or property calls, restricts matches by parameter types. Leave this <see langword="null" />
    ///     or empty when matching a field read.
    /// </param>
    /// <param name="arg">
    ///     For <see cref="At.Argument" />, the 1-based argument to rewrite, or <c>0</c> to infer by unique
    ///     type match. Ignored for other shifts.
    /// </param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107", Justification = "Public attribute constructor; the parameters are the injection's authored call-site shape and cannot be bundled without breaking the API.")]
    public InjectAttribute(string method, Type invokeDeclaringType, string invokeDeclaringMethod, At shift, uint by = 0, Type[]? targetParameterTypes = null, Type[]? invokeParameterTypes = null, uint arg = 0) {
        Method = method;
        At = shift;
        this.invokeDeclaringType = invokeDeclaringType;
        this.invokeMethod = invokeDeclaringMethod;
        this.invokeShift = shift;
        this.by = by;
        ParameterTypes = targetParameterTypes;
        this.invokeParameterTypes = invokeParameterTypes;
        this.arg = arg;
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
    public int Priority { get; set; }

    /// <summary>
    ///     Gets a value indicating whether this declaration was created through one of the constant-targeting
    ///     constructors.
    /// </summary>
    internal bool HasConstant => constant is not null;

    /// <summary>
    ///     Gets the <see cref="InjectAt" /> corresponding to this injection's position.
    ///     Returns <see cref="InjectAt.Constant" /> when a constant-targeting constructor was used,
    ///     <see cref="InjectAt.Invoke" /> when the invoke constructor was used, otherwise maps from <see cref="At" />.
    /// </summary>
    internal InjectAt ResolvedAt {
        get {
            if (constant is not null) {
                return new InjectAt.Constant(constant, by);
            }

            if (invokeDeclaringType is not null) {
                return new InjectAt.Invoke(invokeDeclaringType, invokeMethod!, invokeShift, by, invokeParameterTypes, arg);
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
