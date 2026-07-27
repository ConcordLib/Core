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
}
