namespace Concord.Emit;

/// <summary>
/// A local variable in language-neutral form.
/// </summary>
public sealed class NeutralLocal {
    /// <summary>
    /// Creates a local variable.
    /// </summary>
    /// <param name="id">The local variable id.</param>
    /// <param name="type">The local variable type.</param>
    /// <param name="pinned">Whether the local is pinned in memory.</param>
    public NeutralLocal(int id, Type type, bool pinned) : this(id, type, pinned, false) {
    }

    /// <summary>
    /// Creates a local variable.
    /// </summary>
    /// <param name="id">The local variable id.</param>
    /// <param name="type">The local variable type.</param>
    /// <param name="pinned">Whether the local is pinned in memory.</param>
    /// <param name="ilgenOwned">Whether the local is owned by IL generation.</param>
    public NeutralLocal(int id, Type type, bool pinned, bool ilgenOwned) {
        Id = id;
        Type = type;
        Pinned = pinned;
        IlgenOwned = ilgenOwned;
    }

    /// <summary>
    /// Gets the local variable id.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Gets the local variable type.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets whether the local is pinned in memory.
    /// </summary>
    public bool Pinned { get; }

    /// <summary>
    /// Gets whether the local is owned by IL generation.
    /// </summary>
    public bool IlgenOwned { get; }
}
