using Concord.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public class NeutralModelTests {
    [Fact]
    public void OperandUnionRoundTripsArgumentSlot() {
        NeutralOperand operand = NeutralOperand.OfArgument(2);
        Assert.Equal(NeutralOperandKind.Argument, operand.Kind);
        Assert.Equal(2, operand.AsArgumentSlot());
    }

    [Fact]
    public void RegionEventEndOfBodyUsesSentinelLabelId() {
        NeutralRegionEvent end = new NeutralRegionEvent(NeutralRegionEventKind.EndRegion, NeutralBody.EndOfBodyLabelId, null);
        Assert.Equal(-1, end.PositionLabelId);
        Assert.Null(end.CatchType);
    }

    [Fact]
    public void LocalCarriesPinnedAndOwnership() {
        NeutralLocal local = new NeutralLocal(0, typeof(int), false, true);
        Assert.True(local.IlgenOwned);
        Assert.False(local.Pinned);
    }

    [Fact]
    public void InstructionStartsWithNoLabels() {
        NeutralInstruction instruction = new NeutralInstruction("nop", NeutralOperand.None);
        Assert.Equal("nop", instruction.OpcodeName);
        Assert.Empty(instruction.Labels);
    }
}
