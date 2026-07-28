using System.Reflection.Emit;

namespace Concord.Emit.Tests;

public static class StreamTargets {
    public static int HeadObserved;

    public static int Target(int value) {
        return value + 1;
    }

    public static void HeadInjection() {
        HeadObserved++;
    }

    public static int Priced() {
        return 5;
    }

    public static IEnumerable<CodeInstruction> FiveToTen(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.opcode == OpCodes.Ldc_I4_5 || instruction.Is(OpCodes.Ldc_I4, 5)) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 10);
            } else {
                yield return instruction;
            }
        }
    }
}
