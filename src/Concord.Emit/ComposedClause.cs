namespace Concord.Emit;

/// <summary>The kind of exception-handling region a <see cref="ComposedClause" /> describes.</summary>
public enum ClauseKind {
    /// <summary>A typed catch handler, named by <see cref="ComposedClause.CatchType" />.</summary>
    Catch,

    /// <summary>A filtered handler, whose filter begins at <see cref="ComposedClause.FilterStart" />.</summary>
    Filter,

    /// <summary>A finally handler, entered on both normal and exceptional exit from the try.</summary>
    Finally,

    /// <summary>A fault handler, entered on exceptional exit only.</summary>
    Fault,
}

/// <summary>
///     One exception-handling region of a <see cref="ComposedBody" />, resolved to instruction indices rather than byte
///     offsets, which are not known until the consumer encodes. Every end index is exclusive.
/// </summary>
public readonly struct ComposedClause {
    /// <summary>
    ///     Initializes a new instance of the <see cref="ComposedClause" /> struct.
    /// </summary>
    /// <param name="kind">Which kind of handler this is.</param>
    /// <param name="tryStart">The first instruction index of the protected region.</param>
    /// <param name="tryEnd">One past the last instruction index of the protected region.</param>
    /// <param name="handlerStart">The first instruction index of the handler.</param>
    /// <param name="handlerEnd">One past the last instruction index of the handler.</param>
    /// <param name="catchType">The caught type on a catch clause, otherwise null.</param>
    /// <param name="filterStart">The first instruction index of the filter, or -1.</param>
    public ComposedClause(ClauseKind kind, int tryStart, int tryEnd, int handlerStart, int handlerEnd, Type? catchType, int filterStart) {
        Kind = kind;
        TryStart = tryStart;
        TryEnd = tryEnd;
        HandlerStart = handlerStart;
        HandlerEnd = handlerEnd;
        CatchType = catchType;
        FilterStart = filterStart;
    }

    /// <summary>Which of the four region kinds this is, and so which of the kind-specific members carry a value.</summary>
    public ClauseKind Kind { get; }

    /// <summary>The first instruction of the protected region, inclusive.</summary>
    public int TryStart { get; }

    /// <summary>One past the last instruction of the protected region.</summary>
    public int TryEnd { get; }

    /// <summary>The first instruction of the handler, inclusive. For a filter this is where the filter hands over.</summary>
    public int HandlerStart { get; }

    /// <summary>
    ///     One past the last instruction of the handler. Equal to the instruction count when the handler runs to the
    ///     literal end of the body, which the consumer resolves like any other boundary by marking a label after the last instruction.
    /// </summary>
    public int HandlerEnd { get; }

    /// <summary>The caught type when <see cref="Kind" /> is <see cref="ClauseKind.Catch" />, otherwise <see langword="null" />.</summary>
    public Type? CatchType { get; }

    /// <summary>
    ///     The first instruction of the filter when <see cref="Kind" /> is <see cref="ClauseKind.Filter" />, otherwise -1.
    ///     The filter runs up to <see cref="HandlerStart" />.
    /// </summary>
    public int FilterStart { get; }
}
