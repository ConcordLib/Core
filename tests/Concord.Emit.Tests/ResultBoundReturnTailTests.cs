using System.Collections.Generic;
using System.Reflection;
using Concord.Emit.Tests.ForeignTargets;
using Xunit;

namespace Concord.Emit.Tests;

public static class ResultBoundInjections {
    public static void WithTargetArgument(ref string result, bool extra) {
        result = result + ":" + extra;
    }

    public static void GuardedWithTargetArgument(ref float result, int scale) {
        try {
            result = result + (1f / scale);
        } catch (System.DivideByZeroException) {
        }
    }
}

public sealed class ResultBoundReturnTailTests {
    [Fact]
    public void ResultBoundPostfixOnAGuardedTarget_DescribesABody() {
        MethodBase target = typeof(ForeignReturnTailTargets)
            .GetMethod(nameof(ForeignReturnTailTargets.GuardedLoopThenReturn))!;
        Injection injection = new Injection(
            typeof(ResultBoundInjections).GetMethod(nameof(ResultBoundInjections.WithTargetArgument))!,
            new InjectAt.Return(0),
            "result-bound",
            0) {
            ForeignOrigin = true,
            ResultArgIndex = 0,
        };

        WrapperComposer.CaptureComposedBody = true;
        try {
            ComposeResult result = WrapperComposer.Compose(target, [injection]);

            Assert.Empty(result.Rejected);
            Assert.Null(result.BodyRejection);
            Assert.Equal("x:False", result.Wrapper.Invoke(null, [null!, false]));
            Assert.Equal("x\n\na:False", result.Wrapper.Invoke(null, [new List<string?> { null, "a" }, false]));
        } finally {
            WrapperComposer.CaptureComposedBody = false;
        }
    }

    [Fact]
    public void GuardedResultBoundPostfixOnAnUnguardedTarget_DescribesABody() {
        MethodBase target = typeof(ForeignReturnTailTargets)
            .GetMethod(nameof(ForeignReturnTailTargets.ScaleAndReturn))!;
        Injection injection = new Injection(
            typeof(ResultBoundInjections).GetMethod(nameof(ResultBoundInjections.GuardedWithTargetArgument))!,
            new InjectAt.Return(0),
            "guarded-result-bound",
            0) {
            ForeignOrigin = true,
            ResultArgIndex = 0,
        };

        WrapperComposer.CaptureComposedBody = true;
        try {
            ComposeResult result = WrapperComposer.Compose(target, [injection]);

            Assert.Empty(result.Rejected);
            Assert.Null(result.BodyRejection);
            Assert.Equal(4.5f, result.Wrapper.Invoke(null, [2]));
        } finally {
            WrapperComposer.CaptureComposedBody = false;
        }
    }
}
