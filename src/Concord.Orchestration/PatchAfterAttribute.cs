namespace Concord;

/// <summary>
///     Orders this patch declaration after patches owned by the named type or owner.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PatchAfterAttribute : Attribute {
    /// <summary>
    ///     Creates an ordering constraint for a referenced patch type.
    /// </summary>
    /// <param name="patchType">The patch declaration that should run before this declaration.</param>
    public PatchAfterAttribute(Type patchType) {
        Owner = patchType.FullName!;
    }

    /// <summary>
    ///     Creates an ordering constraint for a patch owner string.
    /// </summary>
    /// <param name="owner">The owner that should run before this declaration.</param>
    public PatchAfterAttribute(string owner) {
        Owner = owner;
    }

    /// <summary>
    ///     The owner that should run before this declaration.
    /// </summary>
    public string Owner { get; }
}
