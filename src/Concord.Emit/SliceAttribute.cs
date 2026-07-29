using Concord.Emit;

namespace Concord;

/// <summary>
///     Bounds an invoke or construction search to the code between two member-access anchors.
///     The <c>by</c> ordinal on the injection then counts matches inside that range only.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SliceAttribute : Attribute {
    /// <summary>
    ///     Declares a range that starts after the opening anchor and ends before the closing anchor.
    ///     Leave both an anchor's type and its member null to extend to that end of the body; naming
    ///     one without the other is an error.
    /// </summary>
    /// <param name="fromType">The declaring type of the opening anchor.</param>
    /// <param name="fromMember">The member name of the opening anchor.</param>
    /// <param name="fromBy">The 1-based occurrence of the opening anchor, counted across the whole body.</param>
    /// <param name="toType">The declaring type of the closing anchor.</param>
    /// <param name="toMember">The member name of the closing anchor.</param>
    /// <param name="toBy">The 1-based occurrence of the closing anchor, counted across the whole body.</param>
    public SliceAttribute(
        Type? fromType = null,
        string? fromMember = null,
        uint fromBy = 1,
        Type? toType = null,
        string? toMember = null,
        uint toBy = 1) {
        FromType = fromType;
        FromMember = fromMember;
        FromBy = fromBy;
        ToType = toType;
        ToMember = toMember;
        ToBy = toBy;
    }

    /// <summary>The declaring type of the opening anchor.</summary>
    public Type? FromType { get; }

    /// <summary>The member name of the opening anchor.</summary>
    public string? FromMember { get; }

    /// <summary>The 1-based occurrence of the opening anchor.</summary>
    public uint FromBy { get; }

    /// <summary>The declaring type of the closing anchor.</summary>
    public Type? ToType { get; }

    /// <summary>The member name of the closing anchor.</summary>
    public string? ToMember { get; }

    /// <summary>The 1-based occurrence of the closing anchor.</summary>
    public uint ToBy { get; }

    /// <summary>
    ///     The <see cref="SliceRange" /> corresponding to this declaration.
    /// </summary>
    internal SliceRange ResolvedRange => new SliceRange(FromType, FromMember, FromBy, ToType, ToMember, ToBy);
}
