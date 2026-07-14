using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Matches inlined literal constants inside a copied method spine for <see cref="InjectAt.Constant" /> lowering.
/// </summary>
internal static class ConstantMatcher {
    /// <summary>
    ///     Finds every instruction in <paramref name="spine" /> that loads a literal equal to <paramref name="value" />.
    /// </summary>
    /// <param name="spine">The wrapper's copied instruction spine to search.</param>
    /// <param name="value">The literal to match. Supported kinds: int, long, float, double, string.</param>
    /// <remarks>Float and double comparisons use exact <see cref="object.Equals(object)" /> representation; NaN is unsupported.</remarks>
    internal static List<Instruction> FindMatches(IReadOnlyList<Instruction> spine, object value) {
        return spine.Where(i => Matches(i, value)).ToList();
    }

    private static bool Matches(Instruction instruction, object value) {
        return value switch {
            int i => MatchesInt(instruction, i),
            long l => instruction.OpCode == OpCodes.Ldc_I8 && (long)instruction.Operand == l,
            float f => instruction.OpCode == OpCodes.Ldc_R4 && ((float)instruction.Operand).Equals(f),
            double d => instruction.OpCode == OpCodes.Ldc_R8 && ((double)instruction.Operand).Equals(d),
            string s => instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == s,
            _ => throw new ConcordEmitException(
                "CONC039",
                $"Constant injections support int, long, float, double, and string; got '{value.GetType().Name}'."),
        };
    }

    private static bool MatchesInt(Instruction instruction, int value) {
        if (instruction.OpCode == OpCodes.Ldc_I4) {
            return (int)instruction.Operand == value;
        }

        if (instruction.OpCode == OpCodes.Ldc_I4_S) {
            return (sbyte)instruction.Operand == value;
        }

        if (instruction.OpCode == OpCodes.Ldc_I4_M1) {
            return value == -1;
        }

        if (instruction.OpCode.Code >= Code.Ldc_I4_0 && instruction.OpCode.Code <= Code.Ldc_I4_8) {
            return value == instruction.OpCode.Code - Code.Ldc_I4_0;
        }

        return false;
    }
}
