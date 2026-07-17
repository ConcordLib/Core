namespace Concord.Emit;

/// <summary>
/// An IL method body in language-neutral form.
/// </summary>
public sealed class NeutralBody {
    /// <summary>
    /// A sentinel label id marking the end of the method body.
    /// </summary>
    public const int EndOfBodyLabelId = -1;

    /// <summary>
    /// Creates a neutral body.
    /// </summary>
    /// <param name="instructions">The list of instructions in the body.</param>
    /// <param name="locals">The list of local variables in the body.</param>
    /// <param name="initLocals">Whether locals are initialized to zero.</param>
    /// <param name="regionEvents">The list of exception handling region events.</param>
    public NeutralBody(List<NeutralInstruction> instructions, List<NeutralLocal> locals, bool initLocals, List<NeutralRegionEvent> regionEvents) {
        Instructions = instructions;
        Locals = locals;
        InitLocals = initLocals;
        RegionEvents = regionEvents;
    }

    /// <summary>
    /// The list of instructions in the body.
    /// </summary>
    public List<NeutralInstruction> Instructions { get; }

    /// <summary>
    /// The list of local variables in the body.
    /// </summary>
    public List<NeutralLocal> Locals { get; }

    /// <summary>
    /// Whether locals are initialized to zero.
    /// </summary>
    public bool InitLocals { get; }

    /// <summary>
    /// The list of exception handling region events.
    /// </summary>
    public List<NeutralRegionEvent> RegionEvents { get; }
}
