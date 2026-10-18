using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public static class ControlFlowTranspilers {
    public static IEnumerable<CodeInstruction> LeaveBecomesBranch(IEnumerable<CodeInstruction> instructions) {
        bool rewritten = false;
        foreach (CodeInstruction instruction in instructions) {
            if (!rewritten && (instruction.opcode == OpCodes.Leave || instruction.opcode == OpCodes.Leave_S)) {
                instruction.opcode = OpCodes.Br;
                rewritten = true;
            }

            yield return instruction;
        }
    }

    public static IEnumerable<CodeInstruction> DoubleTheArgument(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            yield return instruction;
            if (instruction.opcode == OpCodes.Ldarg_0) {
                yield return new CodeInstruction(OpCodes.Ldc_I4_2);
                yield return new CodeInstruction(OpCodes.Mul);
            }
        }
    }
}

public sealed class IllegalControlFlowTests {
    [Fact]
    public void BranchOutOfATryInsteadOfLeave_IsRefusedWithConc174() {
        ConcordEmitException failure = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(Target(), [Transpile(nameof(ControlFlowTranspilers.LeaveBecomesBranch))]));

        Assert.Equal("CONC174", failure.Code);
        Assert.Contains("LeaveBecomesBranch", failure.Message);
    }

    [Fact]
    public void BranchOutOfATry_EvictsOnlyTheTranspilerThatMadeIt() {
        ComposeResult composed = WrapperComposer.Compose(
            Target(),
            [
                Transpile(nameof(ControlFlowTranspilers.LeaveBecomesBranch)),
                Transpile(nameof(ControlFlowTranspilers.DoubleTheArgument)),
            ]);

        RejectedInjection dropped = Assert.Single(composed.Rejected);
        Assert.Equal("CONC174", dropped.Code);
        Assert.Contains("LeaveBecomesBranch", dropped.Message);

        Func<int, int> run = composed.Wrapper.CreateDelegate<Func<int, int>>();
        Assert.Equal(14, run(7));
    }

    private static MethodBase Target() {
        return typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Finally))!;
    }

    private static Injection Transpile(string name) {
        return new Injection(typeof(ControlFlowTranspilers).GetMethod(name)!, new InjectAt.Transpiler(false), "test." + name, 0);
    }
}
