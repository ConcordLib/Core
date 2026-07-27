using System.Reflection.Emit;

namespace Concord;

/// <summary>
///     One IL instruction in an author-facing transpiler stream. Member names are lowercase to match
///     Harmony so migrating transpiler bodies compile unchanged; do not rename them to house style.
/// </summary>
public sealed class CodeInstruction {
    /// <summary>Creates an instruction.</summary>
    /// <param name="opcode">The opcode.</param>
    /// <param name="operand">The operand, or <see langword="null" /> when the opcode takes none.</param>
    public CodeInstruction(OpCode opcode, object? operand = null) {
        this.opcode = opcode;
        this.operand = operand;
        labels = new List<Label>();
        blocks = new List<ExceptionBlock>();
    }

    /// <summary>Creates a copy of another instruction, including its labels and blocks.</summary>
    /// <param name="other">The instruction to copy.</param>
    public CodeInstruction(CodeInstruction other) {
        opcode = other.opcode;
        operand = other.operand;
        labels = new List<Label>(other.labels);
        blocks = new List<ExceptionBlock>(other.blocks);
    }

    /// <summary>The opcode.</summary>
#pragma warning disable SA1300
    public OpCode opcode { get; set; }
#pragma warning restore SA1300

    /// <summary>The operand, or <see langword="null" /> when the opcode takes none.</summary>
#pragma warning disable SA1300
    public object? operand { get; set; }
#pragma warning restore SA1300

    /// <summary>The labels attached to this instruction as a branch target.</summary>
#pragma warning disable SA1300
    public List<Label> labels { get; }
#pragma warning restore SA1300

    /// <summary>The exception-handling boundaries that open or close at this instruction.</summary>
#pragma warning disable SA1300
    public List<ExceptionBlock> blocks { get; }
#pragma warning restore SA1300

    /// <summary>Tests whether this instruction has the given opcode and operand.</summary>
    /// <param name="match">The opcode to match.</param>
    /// <param name="matchOperand">The operand to match, or <see langword="null" /> to match any operand.</param>
    /// <returns><see langword="true" /> when the instruction matches.</returns>
    /// <remarks>
    ///     Numeric operands compare by value across box types, so matching <c>4</c> finds an operand
    ///     boxed as <see cref="byte" />, <see cref="int" />, or <see cref="long" /> alike. Short-form
    ///     opcodes carry a <see cref="byte" /> operand, and requiring authors to know that is a silent
    ///     -false trap. Integral and floating-point operands never cross-match.
    /// </remarks>
    public bool Is(OpCode match, object? matchOperand) {
        if (opcode != match) {
            return false;
        }

        if (matchOperand is null) {
            return true;
        }

        return OperandsEqual(matchOperand, operand);
    }

    /// <summary>Tests whether this instruction loads an argument, optionally a specific slot.</summary>
    /// <param name="n">The argument slot to match, or <see langword="null" /> to match any.</param>
    /// <returns><see langword="true" /> when the instruction loads the requested argument.</returns>
    public bool IsLdarg(int? n = null) {
        int slot;
        if (opcode == OpCodes.Ldarg_0) {
            slot = 0;
        } else if (opcode == OpCodes.Ldarg_1) {
            slot = 1;
        } else if (opcode == OpCodes.Ldarg_2) {
            slot = 2;
        } else if (opcode == OpCodes.Ldarg_3) {
            slot = 3;
        } else if (opcode == OpCodes.Ldarg_S || opcode == OpCodes.Ldarg) {
            if (operand is null) {
                return false;
            }

            slot = Convert.ToInt32(operand);
        } else {
            return false;
        }

        return n is null || n.Value == slot;
    }

    /// <inheritdoc />
    public override string ToString() {
        return operand is null ? opcode.Name ?? string.Empty : opcode.Name + " " + operand;
    }

    private static bool OperandsEqual(object matchOperand, object? operand) {
        if (operand is null) {
            return false;
        }

        Type matchType = matchOperand.GetType();
        Type operandType = operand.GetType();

        if (IsIntegralType(matchType) && IsIntegralType(operandType)) {
            return IntegralEquals(matchOperand, operand);
        }

        if (matchOperand.Equals(operand)) {
            return true;
        }

        if (IsFloatingPointType(matchType) && IsFloatingPointType(operandType)) {
            double matchValue = Convert.ToDouble(matchOperand);
            double operandValue = Convert.ToDouble(operand);
            return matchValue == operandValue;
        }

        return false;
    }

    private static bool IntegralEquals(object left, object right) {
        bool leftNegative = IsNegative(left);
        bool rightNegative = IsNegative(right);
        if (leftNegative != rightNegative) {
            return false;
        }

        if (leftNegative) {
            return Convert.ToInt64(left) == Convert.ToInt64(right);
        }

        return Convert.ToUInt64(left) == Convert.ToUInt64(right);
    }

    private static bool IsNegative(object value) {
        if (value is sbyte sb) { return sb < 0; }
        if (value is short s) { return s < 0; }
        if (value is int i) { return i < 0; }
        if (value is long l) { return l < 0; }
        return false;
    }

    private static bool IsIntegralType(Type type) {
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong);
    }

    private static bool IsFloatingPointType(Type type) {
        return type == typeof(float) || type == typeof(double);
    }
}
