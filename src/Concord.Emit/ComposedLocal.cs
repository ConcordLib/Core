namespace Concord.Emit;

/// <summary>
///     One local slot of a <see cref="ComposedBody" />. Locals cross as types, not tokens: a signature blob rejects a
///     <c>TypeSpec</c> where it wants a <c>TypeDefOrRef</c>, so the consumer encodes constructed types structurally.
/// </summary>
public readonly struct ComposedLocal {
    /// <summary>
    ///     Initializes a new instance of the <see cref="ComposedLocal" /> struct.
    /// </summary>
    /// <param name="type">The slot's type, with any pin already unwrapped.</param>
    /// <param name="isPinned">Whether the slot is pinned.</param>
    public ComposedLocal(Type type, bool isPinned) {
        Type = type;
        IsPinned = isPinned;
    }

    /// <summary>The declared type of the slot, with any pinned modifier stripped off it.</summary>
    public Type Type { get; }

    /// <summary>Whether the slot is pinned. Separate because <see cref="System.Type" /> cannot express the <c>PINNED</c> modifier.</summary>
    public bool IsPinned { get; }
}
