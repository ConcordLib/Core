using System.Reflection;
using Concord.Emit.Tests.ForeignTargets;
using Xunit;

namespace Concord.Emit.Tests;

public static class ArrayCountingInjections {
    public static int Runs;

    public static void Count() {
        Runs++;
    }
}

public sealed class MultiDimArrayCallTests {
    [Theory]
    [InlineData(nameof(ForeignArrayTargets.SumCorners))]
    [InlineData(nameof(ForeignArrayTargets.FillThenRead))]
    [InlineData(nameof(ForeignArrayTargets.BumpThroughReference))]
    public void ARankTwoArrayAccess_IsRewrittenToAnEncodableCall(string targetName) {
        ComposeResult result = ComposeWithCount(targetName);

        Assert.Null(result.BodyRejection);
        Assert.NotNull(result.Body);
        foreach (ComposedInstruction composed in result.Body!.Instructions) {
            if (composed.Instruction.operand is MethodBase called) {
                Assert.False(
                    called.DeclaringType is { IsArray: true } owner && owner.GetArrayRank() > 1,
                    $"'{called.DeclaringType!.Name}.{called.Name}' survived the rewrite");
            }
        }
    }

    [Fact]
    public void ARewrittenRead_KeepsTheTargetsResult() {
        ComposeResult result = ComposeWithCount(nameof(ForeignArrayTargets.SumCorners));

        Assert.Equal(5, result.Wrapper.Invoke(null, [new[,] { { 2, 0 }, { 0, 3 } }]));
    }

    [Fact]
    public void ARewrittenAllocationAndWrite_KeepTheTargetsResult() {
        ComposeResult result = ComposeWithCount(nameof(ForeignArrayTargets.FillThenRead));

        Assert.Equal(9, result.Wrapper.Invoke(null, [3, 3]));
    }

    [Fact]
    public void ARewrittenAddress_StillWritesThroughToTheArray() {
        ComposeResult result = ComposeWithCount(nameof(ForeignArrayTargets.BumpThroughReference));
        int[,] grid = new int[2, 2];

        Assert.Equal(1, result.Wrapper.Invoke(null, [grid]));
        Assert.Equal(1, grid[0, 1]);
    }

    private static ComposeResult ComposeWithCount(string targetName) {
        MethodBase target = typeof(ForeignArrayTargets).GetMethod(targetName)!;
        Injection injection = new Injection(
            typeof(ArrayCountingInjections).GetMethod(nameof(ArrayCountingInjections.Count))!,
            new InjectAt.Head(),
            "array-count",
            0) {
            ForeignOrigin = true,
        };

        WrapperComposer.CaptureComposedBody = true;
        try {
            return WrapperComposer.Compose(target, [injection]);
        } finally {
            WrapperComposer.CaptureComposedBody = false;
        }
    }
}
