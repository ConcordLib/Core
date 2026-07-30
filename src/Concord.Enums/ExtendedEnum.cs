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
public abstract class ExtendedEnum<TEnum>
    where TEnum : struct, Enum {
}
