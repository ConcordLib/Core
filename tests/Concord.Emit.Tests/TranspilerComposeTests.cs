using System.Reflection;
using System.Reflection.Emit;
using Concord;
using Xunit;

namespace Concord.Emit.Tests;

public static class PriceTranspilers {
    public static IEnumerable<CodeInstruction> FiveToTen(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.opcode == OpCodes.Ldc_I4_5) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 10);
            } else {
                yield return instruction;
            }
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

public static class PricePlusOne {
    public static void Bump(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 1;
    }
}

public sealed class TranspilerComposeTests {
    private static Injection Transpiler(string name, int priority = 0, bool final = false) {
        MethodBase method = typeof(PriceTranspilers).GetMethod(name)!;
        return new Injection(method, new InjectAt.Transpiler(final), "test-" + name, priority);
    }

    [Fact]
    public void Transpiler_RewritesOriginalBody() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        ComposeResult result = WrapperComposer.Compose(target, [Transpiler(nameof(PriceTranspilers.FiveToTen))]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(10, run());
    }

    [Fact]
    public void Transpilers_ChainInOrder() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        ComposeResult result = WrapperComposer.Compose(target, [
            Transpiler(nameof(PriceTranspilers.FiveToTen)),
            Transpiler(nameof(PriceTranspilers.TenToTwenty)),
        ]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(20, run());
    }

    [Fact]
    public void Transpiler_ComposesWithDeclarativeReturnInjection() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase bump = typeof(PricePlusOne).GetMethod(nameof(PricePlusOne.Bump))!;
        ComposeResult result = WrapperComposer.Compose(target, [
            Transpiler(nameof(PriceTranspilers.FiveToTen)),
            new Injection(bump, new InjectAt.Return(0), "test-return", 0),
        ]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(11, run());
    }

    [Fact]
    public void NoTranspiler_BodyIsUnchanged() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(typeof(PricePlusOne).GetMethod(nameof(PricePlusOne.Bump))!, new InjectAt.Return(0), "test-return", 0),
        ]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(6, run());
    }
}
