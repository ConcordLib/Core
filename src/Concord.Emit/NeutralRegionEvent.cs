namespace Concord.Emit;

/// <summary>
/// An exception handling region event in language-neutral form.
/// </summary>
public sealed class NeutralRegionEvent {
    /// <summary>
    /// Creates a region event.
    /// </summary>
    /// <param name="kind">The kind of region event.</param>
    /// <param name="positionLabelId">The label id marking the event position.</param>
    /// <param name="catchType">The catch type, if this is a catch event; null otherwise.</param>
    public NeutralRegionEvent(NeutralRegionEventKind kind, int positionLabelId, Type? catchType) {
        Kind = kind;
        PositionLabelId = positionLabelId;
        CatchType = catchType;
    }

    /// <summary>
    /// Gets the kind of region event.
    /// </summary>
    public NeutralRegionEventKind Kind { get; }

    /// <summary>
    /// Gets the label id marking the event position.
    /// </summary>
    public int PositionLabelId { get; }

    /// <summary>
    /// Gets the catch type, if this is a catch event; null otherwise.
    /// </summary>
    public Type? CatchType { get; }
}
