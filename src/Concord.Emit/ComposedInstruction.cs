using System.Reflection;

namespace Concord.Emit;

/// <summary>One instruction of a <see cref="ComposedBody" />, paired with where in the target it came from.</summary>
public readonly struct ComposedInstruction {
    /// <summary>
    ///     Initializes a new instance of the <see cref="ComposedInstruction" /> struct.
    /// </summary>
    /// <param name="instruction">The instruction itself.</param>
    /// <param name="originOffset">The IL offset in the target this was copied from, or null.</param>
    /// <param name="originInjection">The injection that produced this, or null.</param>
    public ComposedInstruction(CodeInstruction instruction, int? originOffset, MethodBase? originInjection) {
        Instruction = instruction;
        OriginOffset = originOffset;
        OriginInjection = originInjection;
    }

    /// <summary>
    ///     The instruction in the form a transpiler sees, its operand already resolved to a CLR type:
    ///     <see cref="Label" />, <c>Label[]</c>, <see cref="LocalRef" />, <c>Type</c>, <c>FieldInfo</c>, <c>MethodBase</c>, or a constant.
    /// </summary>
    public CodeInstruction Instruction { get; }

    /// <summary>
    ///     The IL offset in the original target body this was copied from, or <see langword="null" /> for injected IL.
    ///     Null rather than a sentinel, so an instruction that really came from offset 0 cannot read as having no origin.
    /// </summary>
    public int? OriginOffset { get; }

    /// <summary>
    ///     The injection method whose body produced this instruction, null for target or transpiler IL. Its declaring
    ///     type's assembly names the mod a frame belongs to. Never set together with <see cref="OriginOffset" />.
    /// </summary>
    public MethodBase? OriginInjection { get; }
}
