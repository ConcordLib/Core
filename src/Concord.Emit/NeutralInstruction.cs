namespace Concord.Emit;

/// <summary>
/// An IL instruction in language-neutral form.
/// </summary>
public sealed class NeutralInstruction {
    /// <summary>
    /// Creates a neutral instruction.
    /// </summary>
    /// <param name="opcodeName">The name of the opcode (e.g., "nop", "ldarg.0").</param>
    /// <param name="operand">The instruction operand.</param>
    public NeutralInstruction(string opcodeName, NeutralOperand operand) {
        OpcodeName = opcodeName;
        Operand = operand;
        Labels = new List<int>();
    }

    /// <summary>
    /// Gets the opcode name.
    /// </summary>
    public string OpcodeName { get; }

    /// <summary>
    /// Gets the instruction operand.
    /// </summary>
    public NeutralOperand Operand { get; }

    /// <summary>
    /// Gets the list of label ids that precede this instruction.
    /// </summary>
    public List<int> Labels { get; }
}
