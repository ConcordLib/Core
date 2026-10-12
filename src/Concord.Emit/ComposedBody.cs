namespace Concord.Emit;

/// <summary>
///     A composed method body described with no Cecil type in it, so a detour backend in a separate assembly can encode it.
///     Maxstack, dynamic-stack-allocation and final byte offsets are absent by design: the consumer derives all three.
/// </summary>
public sealed class ComposedBody {
    /// <summary>
    ///     Initializes a new instance of the <see cref="ComposedBody" /> class.
    /// </summary>
    /// <param name="instructions">The instruction stream in emission order.</param>
    /// <param name="locals">The local slots, in slot order.</param>
    /// <param name="clauses">The exception handling clauses.</param>
    /// <param name="initLocals">Whether the body zero-initializes its locals.</param>
    public ComposedBody(
        IReadOnlyList<ComposedInstruction> instructions,
        IReadOnlyList<ComposedLocal> locals,
        IReadOnlyList<ComposedClause> clauses,
        bool initLocals) {
        Instructions = instructions;
        Locals = locals;
        Clauses = clauses;
        InitLocals = initLocals;
    }

    /// <summary>
    ///     The instruction stream in emission order. Branch targets are <see cref="Label" />s matched to the
    ///     <see cref="CodeInstruction.labels" /> of the instruction they point at, never byte offsets.
    /// </summary>
    public IReadOnlyList<ComposedInstruction> Instructions { get; }

    /// <summary>The local slots in slot order, so slot <c>n</c> is <c>Locals[n]</c> and is what a <see cref="LocalRef" /> names.</summary>
    public IReadOnlyList<ComposedLocal> Locals { get; }

    /// <summary>The exception-handling regions, resolved to indices into <see cref="Instructions" />.</summary>
    public IReadOnlyList<ComposedClause> Clauses { get; }

    /// <summary>Whether the header's <c>init</c> flag is set, zero-initializing the local slots.</summary>
    public bool InitLocals { get; }
}
