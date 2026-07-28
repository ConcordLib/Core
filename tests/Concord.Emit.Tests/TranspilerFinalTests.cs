using System.Reflection;
using System.Reflection.Emit;
using Concord;
using Xunit;

namespace Concord.Emit.Tests;

public static class FinalTranspilers {
    public static int Calls;
    public static bool ObservedHasReturnStore;

    public static IEnumerable<CodeInstruction> CountAndPassThrough(IEnumerable<CodeInstruction> instructions) {
        Calls++;
        CodeInstruction? previous = null;
        foreach (CodeInstruction instruction in instructions) {
            if (previous is not null
                && previous.opcode == OpCodes.Ldc_I4_1
                && instruction.opcode == OpCodes.Stloc
                && instruction.operand is LocalRef local
                && local.Type == typeof(bool)) {
                ObservedHasReturnStore = true;
            }

            previous = instruction;
            yield return instruction;
        }
    }

    public static IEnumerable<CodeInstruction> TenToTwenty(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.Is(OpCodes.Ldc_I4, 10)) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 20);
            } else {
                yield return instruction;
            }
        }
    }
}

public sealed class TranspilerFinalTests {
    [Fact]
    public void FinalTranspiler_RunsAfterComposition() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase pre = typeof(PriceTranspilers).GetMethod(nameof(PriceTranspilers.FiveToTen))!;
        MethodBase post = typeof(FinalTranspilers).GetMethod(nameof(FinalTranspilers.TenToTwenty))!;

        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(pre, new InjectAt.Transpiler(false), "test-pre", 0),
            new Injection(post, new InjectAt.Transpiler(true), "test-post", 0),
        ]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(20, run());
    }

    [Fact]
    public void FinalTranspiler_SeesComposedBody() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase bump = typeof(PricePlusOne).GetMethod(nameof(PricePlusOne.Bump))!;
        MethodBase post = typeof(FinalTranspilers).GetMethod(nameof(FinalTranspilers.CountAndPassThrough))!;

        FinalTranspilers.Calls = 0;
        FinalTranspilers.ObservedHasReturnStore = false;
        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(bump, new InjectAt.Return(0), "test-return", 0),
            new Injection(post, new InjectAt.Transpiler(true), "test-post", 0),
        ]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(1, FinalTranspilers.Calls);
        Assert.True(
            FinalTranspilers.ObservedHasReturnStore,
            "final transpiler should see the composed body's 'HasReturn := true' store (Ldc_I4_1 then Stloc on a bool " +
            "local), emitted only because the Return injection above sets ch.ReturnValue - proving it observed the " +
            "assembled wrapper, not the raw two-instruction 'return 5;' target body. This assertion depends on the " +
            "Return injection staying in this scenario; removing it removes the signal.");
        Assert.Equal(6, run());
    }

    [Fact]
    public void FinalTranspiler_RunsStandaloneWithNoOtherInjections() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase post = typeof(PriceTranspilers).GetMethod(nameof(PriceTranspilers.FiveToTen))!;

        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(post, new InjectAt.Transpiler(true), "test-post", 0),
        ]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(10, run());
    }
}
