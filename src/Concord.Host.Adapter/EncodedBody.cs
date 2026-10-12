namespace Concord.Host.Adapter;

/// <summary>
///     A composed body after encoding: the ECMA-335 bytes the runtime is handed, plus the offsets a caller needs to
///     map a reported IL offset back to the instruction that produced it.
/// </summary>
public sealed class EncodedBody {
    /// <summary>Initializes a new instance.</summary>
    /// <param name="bytes">The encoded method body, header included.</param>
    /// <param name="instructionOffsets">The byte offset of each instruction, in composed order.</param>
    /// <param name="maxStack">The <c>maxstack</c> written into the header.</param>
    /// <param name="codeSize">The length of the IL stream, header excluded.</param>
    public EncodedBody(byte[] bytes, IReadOnlyList<int> instructionOffsets, int maxStack, int codeSize) {
        Bytes = bytes;
        InstructionOffsets = instructionOffsets;
        MaxStack = maxStack;
        CodeSize = codeSize;
    }

    /// <summary>The encoded body, header then IL stream, laid out the way the runtime reads it.</summary>
    public byte[] Bytes { get; }

    /// <summary>Byte offset of each instruction into the IL stream, one entry per composed instruction, same order.</summary>
    public IReadOnlyList<int> InstructionOffsets { get; }

    /// <summary>The <c>maxstack</c> declared in the header.</summary>
    public int MaxStack { get; }

    /// <summary>Length of the IL stream in bytes, header excluded. One past the last valid IL offset.</summary>
    public int CodeSize { get; }
}
