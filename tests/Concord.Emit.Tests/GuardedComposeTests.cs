using System.Collections.Generic;
using System.Reflection;
using Concord.Emit.Tests.ForeignTargets;
using Xunit;

namespace Concord.Emit.Tests;

public static class GuardedTargets {
    public static int Guarded(int a, int b) {
        int total = a;
        try {
            total += b;
        } finally {
            total += 3;
        }

        return total;
    }
}

public static class GuardedInjections {
    public static void Head(ControlHandle ch, int a, int b) {
        _ = ch;
        _ = a + b;
    }

    public static void Observe() {
    }

    public static IEnumerable<CodeInstruction> Passthrough(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            yield return instruction;
        }
    }
}

public sealed class GuardedComposeTests {
    [Fact]
    public void TranspilerOnAGuardedTarget_StillDescribesABody() {
        MethodBase target = typeof(GuardedTargets).GetMethod(nameof(GuardedTargets.Guarded))!;
        Injection transpiler = new Injection(
            typeof(GuardedInjections).GetMethod(nameof(GuardedInjections.Passthrough))!,
            new InjectAt.Transpiler(false),
            "guarded-transpiler",
            0);

        WrapperComposer.CaptureComposedBody = true;
        try {
            ComposeResult result = WrapperComposer.Compose(target, [transpiler]);
            Assert.Null(result.BodyRejection);
            Assert.NotNull(result.Body);
        } finally {
            WrapperComposer.CaptureComposedBody = false;
        }
    }

    [Fact]
    public void HeadOnAGuardedTarget_StillDescribesABody() {
        MethodBase target = typeof(GuardedTargets).GetMethod(nameof(GuardedTargets.Guarded))!;
        Injection head = new Injection(
            typeof(GuardedInjections).GetMethod(nameof(GuardedInjections.Head))!,
            new InjectAt.Head(),
            "guarded-head",
            0);

        WrapperComposer.CaptureComposedBody = true;
        try {
            ComposeResult result = WrapperComposer.Compose(target, [head]);
            Assert.Null(result.BodyRejection);
            Assert.NotNull(result.Body);
        } finally {
            WrapperComposer.CaptureComposedBody = false;
        }
    }

    [Fact]
    public void ReturnThenFinalTranspiler_KeepsAConditionalEdgeIntoTheReturnTail() {
        MethodBase target = typeof(ForeignReturnTailTargets)
            .GetMethod(nameof(ForeignReturnTailTargets.GuardedLoopThenReturn))!;
        MethodInfo observe = typeof(GuardedInjections).GetMethod(nameof(GuardedInjections.Observe))!;
        Injection onReturn = new Injection(observe, new InjectAt.Return(0), "return-tail", 0);
        Injection finalTranspiler = new Injection(
            typeof(GuardedInjections).GetMethod(nameof(GuardedInjections.Passthrough))!,
            new InjectAt.Transpiler(true),
            "return-tail-transpiler",
            0);

        ComposeResult result = WrapperComposer.Compose(target, [onReturn, finalTranspiler]);

        Assert.Empty(result.Rejected);
        Assert.Equal("x", result.Wrapper.Invoke(null, [null!, false]));
        Assert.Equal("x\n\na", result.Wrapper.Invoke(null, [new List<string?> { null, "a" }, false]));
    }
}
