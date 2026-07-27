using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class CodeInstructionTests {
    [Fact]
    public void Labels_WithSameId_AreEqual() {
        Label a = LabelFactory.Create(3);
        Label b = LabelFactory.Create(3);
        Label c = LabelFactory.Create(4);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void LocalRef_CarriesItsType() {
        LocalRef local = LocalRefFactory.Create(2, typeof(string));

        Assert.Equal(typeof(string), local.Type);
    }

    [Fact]
    public void ExceptionBlock_CatchBlock_CarriesCatchType() {
        ExceptionBlock block = new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(InvalidOperationException));

        Assert.Equal(ExceptionBlockType.BeginCatchBlock, block.blockType);
        Assert.Equal(typeof(InvalidOperationException), block.catchType);
    }

    [Fact]
    public void Is_MatchesOpcodeAndOperand() {
        CodeInstruction instruction = new CodeInstruction(OpCodes.Ldc_I4, 5);

        Assert.True(instruction.Is(OpCodes.Ldc_I4, 5));
        Assert.False(instruction.Is(OpCodes.Ldc_I4, 6));
        Assert.False(instruction.Is(OpCodes.Ldc_I8, 5));
    }

    [Fact]
    public void Is_WithNullOperand_MatchesOpcodeOnly() {
        CodeInstruction instruction = new CodeInstruction(OpCodes.Ldc_I4, 5);

        Assert.True(instruction.Is(OpCodes.Ldc_I4, null));
    }

    [Fact]
    public void CopyConstructor_ClonesLabelsAndBlocks() {
        CodeInstruction source = new CodeInstruction(OpCodes.Nop);
        source.labels.Add(LabelFactory.Create(1));
        source.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock));

        CodeInstruction copy = new CodeInstruction(source);
        copy.labels.Add(LabelFactory.Create(2));

        Assert.Single(source.labels);
        Assert.Equal(2, copy.labels.Count);
        Assert.Single(copy.blocks);
    }

    [Fact]
    public void IsLdarg_MatchesShortAndLongForms() {
        Assert.True(new CodeInstruction(OpCodes.Ldarg_0).IsLdarg(0));
        Assert.True(new CodeInstruction(OpCodes.Ldarg_S, (byte)4).IsLdarg(4));
        Assert.True(new CodeInstruction(OpCodes.Ldarg_2).IsLdarg());
        Assert.False(new CodeInstruction(OpCodes.Ldloc_0).IsLdarg());
    }

    [Fact]
    public void Is_WithByteOperand_MatchesIntOperandValue() {
        CodeInstruction instruction = new CodeInstruction(OpCodes.Ldarg_S, (byte)4);

        Assert.True(instruction.Is(OpCodes.Ldarg_S, 4));
    }

    [Fact]
    public void Is_NormalizesAcrossIntegralTypes() {
        CodeInstruction instruction = new CodeInstruction(OpCodes.Ldc_I4, (int)5);

        Assert.True(instruction.Is(OpCodes.Ldc_I4, (byte)5));
        Assert.True(instruction.Is(OpCodes.Ldc_I4, (short)5));
        Assert.True(instruction.Is(OpCodes.Ldc_I4, 5L));
        Assert.True(instruction.Is(OpCodes.Ldc_I4, 5));
    }

    [Fact]
    public void Is_DoesNotCrossIntegralAndFloatingPoint() {
        CodeInstruction instructionI4 = new CodeInstruction(OpCodes.Ldc_I4, 5);
        CodeInstruction instructionR4 = new CodeInstruction(OpCodes.Ldc_R4, 5f);

        Assert.False(instructionI4.Is(OpCodes.Ldc_I4, 5f));
        Assert.False(instructionR4.Is(OpCodes.Ldc_R4, (int)5));
    }

    [Fact]
    public void Is_NormalizesFloatingPointTypes() {
        CodeInstruction instruction = new CodeInstruction(OpCodes.Ldc_R4, 5f);

        Assert.True(instruction.Is(OpCodes.Ldc_R4, 5.0));
        Assert.True(instruction.Is(OpCodes.Ldc_R4, 5.0d));
    }

    [Fact]
    public void Is_StringOperandStillMatchesByValue() {
        CodeInstruction instruction = new CodeInstruction(OpCodes.Ldstr, "hello");

        Assert.True(instruction.Is(OpCodes.Ldstr, "hello"));
        Assert.False(instruction.Is(OpCodes.Ldstr, "world"));
    }

    [Fact]
    public void Is_UlongMaxValueDoesNotThrow() {
        CodeInstruction instruction = new CodeInstruction(OpCodes.Ldc_I8, ulong.MaxValue);

        Assert.False(instruction.Is(OpCodes.Ldc_I8, -1L));
        Assert.False(instruction.Is(OpCodes.Ldc_I8, ulong.MaxValue));
    }
}
