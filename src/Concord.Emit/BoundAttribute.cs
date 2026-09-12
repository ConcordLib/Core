namespace Concord;

/// <summary>
///     Marks an injection parameter whose value is bound per registration and emitted as a literal into
///     the composed wrapper, so one injection method can carry a different value for every target.
/// </summary>
/// <remarks>
///     Supply the value through <c>Injection.BoundArguments</c>, keyed by parameter name. Only values IL
///     can load as a literal are allowed: the integer types, <see cref="bool" />, <see cref="char" />,
///     <see cref="float" />, <see cref="double" />, <see cref="string" />, an enum, a
///     <see cref="System.Type" />, or null.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class BoundAttribute : Attribute;
