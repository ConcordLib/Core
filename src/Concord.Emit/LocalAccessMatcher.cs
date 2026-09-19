using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Matches reads and writes of one local slot inside a copied method spine for
///     <see cref="InjectAt.Local" /> lowering.
/// </summary>
internal static class LocalAccessMatcher {
    /// <summary>
    ///     Finds every instruction in <paramref name="spine" /> that performs <paramref name="access" /> on
    ///     <paramref name="slot" />.
    /// </summary>
    /// <param name="spine">The wrapper's copied instruction spine to search, already narrowed by any slice.</param>
    /// <param name="slot">The wrapper local the injection selected.</param>
    /// <param name="access">Whether to match writes to the slot or reads of it.</param>
    /// <remarks>
    ///     <c>ldloca</c> is never a read: it hands out an address the body can write through later, so a
    ///     splice there would replace a value nobody has produced yet.
    /// </remarks>
    internal static List<Instruction> FindMatches(IReadOnlyList<Instruction> spine, VariableDefinition slot, LocalAccess access) {
        List<Instruction> matches = new List<Instruction>();
        for (int i = 0; i < spine.Count; i++) {
            if (Matches(spine[i], slot, access)) {
                matches.Add(spine[i]);
            }
        }

        return matches;
    }

    /// <summary>
    ///     Whether any instruction in <paramref name="spine" /> takes the address of
    ///     <paramref name="slot" /> and hands it straight to an instruction that writes through it.
    /// </summary>
    /// <param name="spine">The wrapper's whole copied instruction spine, never narrowed by a slice.</param>
    /// <param name="slot">The wrapper local the injection selected.</param>
    /// <remarks>
    ///     Only <c>stind</c>, <c>stobj</c>, <c>cpobj</c> and <c>initobj</c> count. An <c>ldloca</c> handed to a call
    ///     is left alone, because nearly every instance call on a struct local is one and almost none
    ///     of them mutate. That leaves a gap: a mutating instance method writes through the address
    ///     with no <c>stloc</c> and no <c>stobj</c>, so a store injection cannot see it and reports
    ///     only the assignments it can. Classifying the call is not possible here, since
    ///     <c>IsReadOnlyAttribute</c> sits on the type rather than the member and the net472 corlib
    ///     predates it entirely.
    ///     <para>
    ///         An <c>out</c> or <c>ref</c> argument is the same gap and the easier one to hit.
    ///         <c>int n = 0; int.TryParse(s, out n);</c> emits one real <c>stloc</c> for the
    ///         initializer and then writes <c>n</c> again through the address the call received, so a
    ///         <see cref="LocalAccess.Store" /> injection on that slot fires once and misses the write
    ///         the author cares about.
    ///     </para>
    /// </remarks>
    internal static bool HasIndirectWrite(IReadOnlyList<Instruction> spine, VariableDefinition slot) {
        for (int i = 0; i < spine.Count; i++) {
            Code code = spine[i].OpCode.Code;
            if ((code == Code.Ldloca || code == Code.Ldloca_S) && OperandIs(spine[i], slot) && ConsumerWrites(spine, i)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="instruction" /> reads <paramref name="slot" />, by the same rule
    ///     <see cref="FindMatches" /> uses for <see cref="LocalAccess.Load" />.
    /// </summary>
    /// <param name="instruction">The instruction to test.</param>
    /// <param name="slot">The wrapper local the injection selected.</param>
    internal static bool IsLoad(Instruction instruction, VariableDefinition slot) {
        return MatchesLoad(instruction, slot);
    }

    /// <summary>
    ///     Whether <paramref name="instruction" /> writes <paramref name="slot" />, by the same rule
    ///     <see cref="FindMatches" /> uses for <see cref="LocalAccess.Store" />.
    /// </summary>
    /// <param name="instruction">The instruction to test.</param>
    /// <param name="slot">The wrapper local the injection selected.</param>
    internal static bool IsStore(Instruction instruction, VariableDefinition slot) {
        return MatchesStore(instruction, slot);
    }

    // Walks forward until something pops the address the ldloca at addressIndex pushed. A branch or
    // an exit before that means the address outlives one straight-line run, which is not a shape
    // this can read, so it says no rather than guessing.
    private static bool ConsumerWrites(IReadOnlyList<Instruction> spine, int addressIndex) {
        int depth = 1;
        for (int i = addressIndex + 1; i < spine.Count; i++) {
            Instruction instruction = spine[i];
            FlowControl flow = instruction.OpCode.FlowControl;
            if (flow is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw) {
                return false;
            }

            int pops = IlDump.PopCount(instruction);
            if (pops >= depth) {
                // A 'dup' lands here too, and says no: it copies the address rather than writing
                // through it, so whatever consumes the copy is past what this can follow.
                return IsIndirectWrite(instruction.OpCode.Code);
            }

            depth += IlDump.PushCount(instruction) - pops;
        }

        return false;
    }

    private static bool IsIndirectWrite(Code code) {
        return code is Code.Stobj or Code.Cpobj or Code.Initobj or Code.Stind_I or Code.Stind_I1
            or Code.Stind_I2 or Code.Stind_I4 or Code.Stind_I8 or Code.Stind_R4 or Code.Stind_R8
            or Code.Stind_Ref;
    }

    private static bool Matches(Instruction instruction, VariableDefinition slot, LocalAccess access) {
        return access == LocalAccess.Store ? MatchesStore(instruction, slot) : MatchesLoad(instruction, slot);
    }

    private static bool MatchesStore(Instruction instruction, VariableDefinition slot) {
        Code code = instruction.OpCode.Code;
        if (code == Code.Stloc || code == Code.Stloc_S) {
            return OperandIs(instruction, slot);
        }

        return code >= Code.Stloc_0 && code <= Code.Stloc_3 && slot.Index == code - Code.Stloc_0;
    }

    private static bool MatchesLoad(Instruction instruction, VariableDefinition slot) {
        Code code = instruction.OpCode.Code;
        if (code == Code.Ldloc || code == Code.Ldloc_S) {
            return OperandIs(instruction, slot);
        }

        return code >= Code.Ldloc_0 && code <= Code.Ldloc_3 && slot.Index == code - Code.Ldloc_0;
    }

    private static bool OperandIs(Instruction instruction, VariableDefinition slot) {
        return instruction.Operand is VariableReference reference && reference.Index == slot.Index;
    }
}
