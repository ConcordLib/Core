namespace Concord.Emit;

/// <summary>
///     A range of a target body, expressed as two member-access anchors. An injection that carries a
///     range matches and counts only inside it.
/// </summary>
/// <param name="FromType">The declaring type of the opening anchor, or <see langword="null" /> to start at the body head.</param>
/// <param name="FromMember">The member name of the opening anchor.</param>
/// <param name="FromBy">The 1-based occurrence of the opening anchor, counted across the whole body.</param>
/// <param name="ToType">The declaring type of the closing anchor, or <see langword="null" /> to end at the body tail.</param>
/// <param name="ToMember">The member name of the closing anchor.</param>
/// <param name="ToBy">The 1-based occurrence of the closing anchor, counted across the whole body.</param>
public sealed record SliceRange(
    Type? FromType,
    string? FromMember,
    uint FromBy,
    Type? ToType,
    string? ToMember,
    uint ToBy);
