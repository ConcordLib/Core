using System.Diagnostics.CodeAnalysis;

namespace Concord;

/// <summary>
///     Base type for a patch declaration that adds members to an existing enum. A declaration whose
///     base is this type holds member fields, not injections.
/// </summary>
/// <typeparam name="TEnum">The enum the declaration extends.</typeparam>
/// <remarks>
///     New members do not reach a <c>switch</c> in code that is already compiled, and mod code cannot
///     switch on them either, because a C# case label needs a compile-time constant. Use <c>if</c>.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "The base type is a marker; its type argument is what Concord and the analyzer read.")]
[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "ExtendedEnumRegistry reads TEnum at runtime and the analyzer reads it symbolically.")]
public abstract class ExtendedEnum<TEnum>
    where TEnum : struct, Enum {
}
