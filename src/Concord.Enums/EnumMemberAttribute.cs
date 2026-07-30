namespace Concord;

/// <summary>
///     Overrides the persisted id of an extended enum member. Use it after renaming a field, so the
///     member keeps the integer it already has in saved data.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class EnumMemberAttribute : Attribute {
    /// <summary>
    ///     Initializes a new id override.
    /// </summary>
    /// <param name="id">The persisted id. It must stay stable for the life of the mod.</param>
    public EnumMemberAttribute(string id) {
        Id = id;
    }

    /// <summary>
    ///     The persisted id this member is stored under.
    /// </summary>
    public string Id { get; }
}
