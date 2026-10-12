using System.Reflection;
using System.Runtime.CompilerServices;

namespace Concord.Emit;

// Keyed on the instruction object, never stored on it: Cecil recomputes Instruction.Offset on every
// write, and a weak table does not grow across the recompose each patch add and remove triggers.
internal static class InstructionOrigins {
    public const int None = -1;

    private static readonly ConditionalWeakTable<object, Origin> Table = new ConditionalWeakTable<object, Origin>();

    // Off by default; the detour backend never reads origins and recording costs an entry per instruction.
    public static bool Enabled { get; set; }

    public static void Record(object instruction, int originalOffset) {
        Set(instruction, new Origin(originalOffset, null));
    }

    // A spliced spine copy already carries a target offset and is still target IL, so the offset
    // wins. An instruction never holds both.
    public static void RecordInjection(IEnumerable<object> instructions, MethodBase injection) {
        if (!Enabled) {
            return;
        }

        foreach (object instruction in instructions) {
            if (OffsetOf(instruction) == None) {
                Set(instruction, new Origin(None, injection));
            }
        }
    }

    public static void CarryOver(object source, object copy) {
        if (!Enabled || !Table.TryGetValue(source, out Origin? origin)) {
            return;
        }

        Set(copy, origin);
    }

    public static int OffsetOf(object instruction) {
        return Table.TryGetValue(instruction, out Origin? origin) ? origin.Offset : None;
    }

    public static MethodBase? InjectionOf(object instruction) {
        return Table.TryGetValue(instruction, out Origin? origin) ? origin.Injection : null;
    }

    public static int[] Describe(IEnumerable<object> instructions) {
        List<int> offsets = new List<int>();
        foreach (object instruction in instructions) {
            offsets.Add(OffsetOf(instruction));
        }

        return offsets.ToArray();
    }

    private static void Set(object instruction, Origin origin) {
        if (!Enabled) {
            return;
        }

        // Unlocked: every key is an Instruction allocated in the calling frame, so two composing
        // threads never share one. The Remove covers the single-thread re-record.
        Table.Remove(instruction);
        Table.Add(instruction, origin);
    }

    private sealed class Origin {
        public Origin(int offset, MethodBase? injection) {
            Offset = offset;
            Injection = injection;
        }

        public int Offset { get; }

        public MethodBase? Injection { get; }
    }
}
