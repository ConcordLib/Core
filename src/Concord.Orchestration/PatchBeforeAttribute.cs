namespace Concord;

/// <summary>
///     Orders this patch declaration before patches owned by the named type or owner.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PatchBeforeAttribute : Attribute {
    /// <summary>
    ///     Creates an ordering constraint for a referenced patch type.
    /// </summary>
    /// <param name="patchType">The patch declaration that should run after this declaration.</param>
    public PatchBeforeAttribute(Type patchType) {
        Owner = patchType.FullName!;
    }

    /// <summary>
    ///     Creates an ordering constraint for a patch owner string.
    /// </summary>
    /// <param name="owner">The owner that should run after this declaration.</param>
    public PatchBeforeAttribute(string owner) {
        Owner = owner;
    }

    /// <summary>
    ///     The owner that should run after this declaration.
    /// </summary>
    public string Owner { get; }
}
