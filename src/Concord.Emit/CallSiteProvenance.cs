using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Locates where each argument of a matched call finished pushing, so an injection can spill that
///     value without depending on which local produced it.
/// </summary>
internal static class CallSiteProvenance {
    /// <summary>
    ///     Index in <paramref name="spine" /> of the last instruction that contributes to argument
    ///     <paramref name="argumentIndex" /> of the call at <paramref name="callIndex" />, or -1 when
    ///     the boundary cannot be determined.
    /// </summary>
    /// <remarks>
    ///     Stack depths are measured relative to the depth the call itself sees, so the instruction
    ///     before the call sits at 0. The call pops one slot per argument, plus one more for a
    ///     receiver. Argument slots stack upwards in declaration order, so the top of argument K sits
    ///     at <c>slotsBelowArgument + 1 - poppedSlots</c>. The receiver contributes to both of those
    ///     terms and cancels: an instance call and a static call of the same arity answer identically.
    ///     No instruction pushes more than one net slot, so a backward walk cannot step over that
    ///     height. The first instruction whose exit depth equals it is the boundary. A branch target
    ///     between the boundary and the call would let control reach the call without passing the
    ///     boundary, which would skip a spill placed there, so those sites report no boundary.
    /// </remarks>
    internal static int FindArgumentPushEnd(
        IReadOnlyList<Instruction> spine,
        int callIndex,
        int argumentIndex,
        int argumentCount,
        bool hasThis,
        MethodDefinition body) {
        if (callIndex <= 0 || callIndex >= spine.Count || argumentIndex < 0 || argumentIndex >= argumentCount) {
            return -1;
        }

        int receiverSlots = hasThis ? 1 : 0;
        int poppedSlots = receiverSlots + argumentCount;
        int slotsBelowArgument = receiverSlots + argumentIndex;
        int wanted = slotsBelowArgument + 1 - poppedSlots;

        if (IsBranchTarget(spine, spine[callIndex])) {
            return -1;
        }

        int exitDepth = 0;
        for (int i = callIndex - 1; i >= 0; i--) {
            if (exitDepth == wanted) {
                return i;
            }

            if (IsBranchTarget(spine, spine[i])) {
                return -1;
            }

            exitDepth += IlDump.PopCount(spine[i], body) - IlDump.PushCount(spine[i]);
        }

        return -1;
    }

    private static bool IsBranchTarget(IReadOnlyList<Instruction> spine, Instruction candidate) {
        foreach (Instruction instruction in spine) {
            if (ReferenceEquals(instruction.Operand, candidate)) {
                return true;
            }

            if (instruction.Operand is not Instruction[] targets) {
                continue;
            }

            foreach (Instruction target in targets) {
                if (ReferenceEquals(target, candidate)) {
                    return true;
                }
            }
        }

        return false;
    }
}
