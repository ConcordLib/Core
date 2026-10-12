using System.Reflection;
using System.Reflection.Emit;

namespace Concord.Host.Adapter;

/// <summary>The deepest evaluation stack a composed body reaches, walked path by path: pops come off, pushes go on, a branch carries its depth to every target and to the fallthrough.</summary>
public readonly struct StackBound {
    private const int Ceiling = ushort.MaxValue + 1;

    private StackBound(int bound, bool usesLocalloc) {
        Bound = bound;
        UsesLocalloc = usesLocalloc;
    }

    /// <summary>The bound, as an <see cref="int" /> so a body too deep for any method header can still be reported.</summary>
    public int Bound { get; }

    /// <summary>Whether the composed body contains a <c>localloc</c> instruction.</summary>
    public bool UsesLocalloc { get; }

    /// <summary>Whether <see cref="Bound" /> fits the 16-bit maxstack field of a fat method header.</summary>
    public bool FitsHeader => Bound <= ushort.MaxValue;

    /// <summary>Whether <see cref="Bound" /> is within a tiny header's maxstack limit of 8; code size and local count are checked elsewhere.</summary>
    public bool FitsTinyHeader => Bound <= 8;

    /// <summary><see cref="Bound" /> narrowed to the header field's width; check <see cref="FitsHeader" /> first on any path that must not throw.</summary>
    /// <exception cref="InvalidOperationException">The bound is larger than <see cref="ushort.MaxValue" />, so no method header can carry it.</exception>
    public ushort MaxStack => FitsHeader
        ? (ushort)Bound
        : throw new InvalidOperationException(
            "composed body needs a maxstack of " + Bound + ", which does not fit the 16-bit method header field");

    /// <summary>Walks every path through a composed body and returns the deepest evaluation stack any of them reaches.</summary>
    /// <param name="body">The composed body to measure.</param>
    /// <returns>The bound for <paramref name="body" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body" /> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">An instruction has a push or pop behaviour with no fixed count.</exception>
    public static StackBound Compute(ComposedBody body) {
        ArgumentNullException.ThrowIfNull(body);

        bool usesLocalloc = false;
        foreach (ComposedInstruction composed in body.Instructions) {
            if (composed.Instruction.opcode == OpCodes.Localloc) {
                usesLocalloc = true;
            }
        }

        Walk walk = new Walk(body.Instructions);
        walk.Seed(0, 0);
        foreach (ComposedClause clause in body.Clauses) {
            walk.Seed(clause.HandlerStart, clause.Kind is ClauseKind.Catch or ClauseKind.Filter ? 1 : 0);
            if (clause.Kind == ClauseKind.Filter) {
                walk.Seed(clause.FilterStart, 1);
            }
        }

        return new StackBound(walk.Run(), usesLocalloc);
    }

    private static int PushCount(OpCode opcode, object? operand) {
        return opcode.StackBehaviourPush switch {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 => 1,
            StackBehaviour.Pushi => 1,
            StackBehaviour.Pushi8 => 1,
            StackBehaviour.Pushr4 => 1,
            StackBehaviour.Pushr8 => 1,
            StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Varpush => CallPushCount(operand),
            _ => throw new NotSupportedException(
                opcode.Name + " has push behaviour " + opcode.StackBehaviourPush + ", which has no push count"),
        };
    }

    private static int CallPushCount(object? operand) {
        return operand switch {
            MethodInfo method => method.ReturnType == typeof(void) ? 0 : 1,
            ConstructorInfo => 0,
            _ => 1,
        };
    }

    private static int PopCount(OpCode opcode, object? operand) {
        return opcode.StackBehaviourPop switch {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 => 1,
            StackBehaviour.Popi => 1,
            StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 => 2,
            StackBehaviour.Popi_pop1 => 2,
            StackBehaviour.Popi_popi => 2,
            StackBehaviour.Popi_popi8 => 2,
            StackBehaviour.Popi_popr4 => 2,
            StackBehaviour.Popi_popr8 => 2,
            StackBehaviour.Popref_pop1 => 2,
            StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi => 3,
            StackBehaviour.Popref_popi_pop1 => 3,
            StackBehaviour.Popref_popi_popi => 3,
            StackBehaviour.Popref_popi_popi8 => 3,
            StackBehaviour.Popref_popi_popr4 => 3,
            StackBehaviour.Popref_popi_popr8 => 3,
            StackBehaviour.Popref_popi_popref => 3,
            StackBehaviour.Varpop => CallPopCount(opcode, operand),
            _ => throw new NotSupportedException(
                opcode.Name + " has pop behaviour " + opcode.StackBehaviourPop + ", which has no pop count"),
        };
    }

    private static int CallPopCount(OpCode opcode, object? operand) {
        if (operand is not MethodBase call) {
            return 0;
        }

        int arguments = call.GetParameters().Length;
        if (opcode == OpCodes.Newobj) {
            return arguments;
        }

        return call.IsStatic ? arguments : arguments + 1;
    }

    private sealed class Walk {
        private readonly IReadOnlyList<ComposedInstruction> stream;
        private readonly Dictionary<Label, int> targets = [];
        private readonly int[] entry;
        private readonly Queue<int> pending = new Queue<int>();
        private int deepest;

        internal Walk(IReadOnlyList<ComposedInstruction> stream) {
            this.stream = stream;
            entry = new int[stream.Count];
            for (int index = 0; index < entry.Length; index++) {
                entry[index] = -1;
            }

            for (int index = 0; index < stream.Count; index++) {
                foreach (Label label in stream[index].Instruction.labels) {
                    targets.TryAdd(label, index);
                }
            }
        }

        internal void Seed(int index, int depth) {
            int capped = depth > Ceiling ? Ceiling : depth;
            if (index < 0 || index >= entry.Length || capped <= entry[index]) {
                return;
            }

            entry[index] = capped;
            if (capped > deepest) {
                deepest = capped;
            }

            pending.Enqueue(index);
        }

        internal int Run() {
            while (pending.Count > 0) {
                Step(pending.Dequeue());
            }

            return deepest;
        }

        private void Step(int index) {
            CodeInstruction instruction = stream[index].Instruction;
            OpCode opcode = instruction.opcode;
            bool empties = opcode == OpCodes.Leave || opcode == OpCodes.Leave_S;
            int drained = empties ? 0 : Math.Max(0, entry[index] - PopCount(opcode, instruction.operand));
            int after = drained + PushCount(opcode, instruction.operand);
            if (after > deepest) {
                deepest = after > Ceiling ? Ceiling : after;
            }

            SeedTargets(instruction.operand, after);
            if (empties || opcode.FlowControl is FlowControl.Branch or FlowControl.Return or FlowControl.Throw) {
                return;
            }

            Seed(index + 1, after);
        }

        private void SeedTargets(object? operand, int depth) {
            if (operand is Label label && targets.TryGetValue(label, out int at)) {
                Seed(at, depth);
                return;
            }

            if (operand is not Label[] arms) {
                return;
            }

            foreach (Label arm in arms) {
                if (targets.TryGetValue(arm, out int to)) {
                    Seed(to, depth);
                }
            }
        }
    }
}
