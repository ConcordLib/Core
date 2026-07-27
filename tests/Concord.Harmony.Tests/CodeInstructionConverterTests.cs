using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

using ExceptionBlock = HarmonyLib.ExceptionBlock;
using ExceptionBlockType = HarmonyLib.ExceptionBlockType;
using Label = System.Reflection.Emit.Label;

public class CodeInstructionConverterTests
{
    private static ILGenerator NewGenerator()
    {
        DynamicMethod method = new DynamicMethod("m", typeof(void), Type.EmptyTypes);
        return method.GetILGenerator();
    }

    [Fact]
    public void BasicStreamRoundTrip_PreservesOpcodeOperandAndLabelIdentity()
    {
        ILGenerator generator = NewGenerator();
        Label label = generator.DefineLabel();
        LocalBuilder local = generator.DeclareLocal(typeof(int));

        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)5),
            new CodeInstruction(OpCodes.Stloc_S, local),
            new CodeInstruction(OpCodes.Ldloc_0),
            new CodeInstruction(OpCodes.Br, label),
            new CodeInstruction(OpCodes.Nop).WithLabels(label),
            new CodeInstruction(OpCodes.Ret),
        };

        NeutralBody neutral = CodeInstructionConverter.ToNeutral(stream, out HarmonyStreamContext context);
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromNeutral(neutral, context, generator);

        Assert.Equal(stream.Count, outgoing.Count);
        Assert.Equal(OpCodes.Ldc_I4, outgoing[0].opcode);
        Assert.Equal(5, outgoing[0].operand);
        Assert.Equal(OpCodes.Stloc_S, outgoing[1].opcode);
        Assert.Same(local, outgoing[1].operand);
        Assert.Equal(OpCodes.Ldloc_S, outgoing[2].opcode);
        Assert.Same(local, outgoing[2].operand);
        Assert.Equal(OpCodes.Br, outgoing[3].opcode);
        Assert.Equal(label, outgoing[3].operand);
        Assert.Single(outgoing[4].labels);
        Assert.Equal(label, outgoing[4].labels[0]);
    }

    [Fact]
    public void MultiLabelInstruction_BothLabelsSurvive()
    {
        ILGenerator generator = NewGenerator();
        Label first = generator.DefineLabel();
        Label second = generator.DefineLabel();

        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Br, first),
            new CodeInstruction(OpCodes.Br, second),
            new CodeInstruction(OpCodes.Nop).WithLabels(first, second),
            new CodeInstruction(OpCodes.Ret),
        };

        NeutralBody neutral = CodeInstructionConverter.ToNeutral(stream, out HarmonyStreamContext context);
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromNeutral(neutral, context, generator);

        Assert.Equal(2, outgoing[2].labels.Count);
        Assert.Contains(first, outgoing[2].labels);
        Assert.Contains(second, outgoing[2].labels);
    }

    [Fact]
    public void ExceptionBlocks_TryCatchRoundTrips()
    {
        ILGenerator generator = NewGenerator();

        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Nop).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock)),
            new CodeInstruction(OpCodes.Pop).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(Exception))),
            new CodeInstruction(OpCodes.Nop).WithBlocks(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock)),
            new CodeInstruction(OpCodes.Ret),
        };

        NeutralBody neutral = CodeInstructionConverter.ToNeutral(stream, out HarmonyStreamContext context);
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromNeutral(neutral, context, generator);

        Assert.Single(outgoing[0].blocks);
        Assert.Equal(ExceptionBlockType.BeginExceptionBlock, outgoing[0].blocks[0].blockType);
        Assert.Single(outgoing[1].blocks);
        Assert.Equal(ExceptionBlockType.BeginCatchBlock, outgoing[1].blocks[0].blockType);
        Assert.Equal(typeof(Exception), outgoing[1].blocks[0].catchType);
        Assert.Single(outgoing[2].blocks);
        Assert.Equal(ExceptionBlockType.EndExceptionBlock, outgoing[2].blocks[0].blockType);
    }

    [Fact]
    public void ExceptionBlocks_NestedTryOpening_DoubleBeginExceptionBlockMarkersSurvive()
    {
        ILGenerator generator = NewGenerator();

        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Nop).WithBlocks(
                new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock),
                new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock)),
            new CodeInstruction(OpCodes.Nop).WithBlocks(
                new ExceptionBlock(ExceptionBlockType.EndExceptionBlock),
                new ExceptionBlock(ExceptionBlockType.EndExceptionBlock)),
            new CodeInstruction(OpCodes.Ret),
        };

        NeutralBody neutral = CodeInstructionConverter.ToNeutral(stream, out HarmonyStreamContext context);
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromNeutral(neutral, context, generator);

        Assert.Equal(2, outgoing[0].blocks.Count);
        Assert.All(outgoing[0].blocks, block => Assert.Equal(ExceptionBlockType.BeginExceptionBlock, block.blockType));
    }

    [Fact]
    public void FilterBlock_ToNeutralThrows()
    {
        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Nop).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginExceptFilterBlock)),
            new CodeInstruction(OpCodes.Ret),
        };

        Assert.Throws<NeutralConversionException>(() => CodeInstructionConverter.ToNeutral(stream, out _));
    }

    [Fact]
    public void FaultBlock_ToNeutralThrows()
    {
        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Nop).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginFaultBlock)),
            new CodeInstruction(OpCodes.Ret),
        };

        Assert.Throws<NeutralConversionException>(() => CodeInstructionConverter.ToNeutral(stream, out _));
    }

    [Fact]
    public void ArgumentSlotOver255_ToNeutralSucceeds_FromNeutralThrowsShortFormRange()
    {
        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Ldarg, 300),
            new CodeInstruction(OpCodes.Ret),
        };

        NeutralBody neutral = CodeInstructionConverter.ToNeutral(stream, out HarmonyStreamContext context);

        Assert.Equal(NeutralOperandKind.Argument, neutral.Instructions[0].Operand.Kind);
        Assert.Equal(300, neutral.Instructions[0].Operand.AsArgumentSlot());

        ILGenerator generator = NewGenerator();
        NeutralConversionException exception = Assert.Throws<NeutralConversionException>(
            () => CodeInstructionConverter.FromNeutral(neutral, context, generator));
        Assert.Contains("exceeds the short-form range", exception.Message);
    }

    [Fact]
    public void UsedButUnmarkedLabel_FromNeutralThrows()
    {
        NeutralInstruction branch = new NeutralInstruction("br", NeutralOperand.OfLabel(0));
        NeutralInstruction ret = new NeutralInstruction("ret", NeutralOperand.None);
        NeutralBody body = new NeutralBody(
            new List<NeutralInstruction> { branch, ret },
            new List<NeutralLocal>(),
            true,
            new List<NeutralRegionEvent>());
        HarmonyStreamContext context = new HarmonyStreamContext();

        ILGenerator generator = NewGenerator();
        NeutralConversionException exception = Assert.Throws<NeutralConversionException>(
            () => CodeInstructionConverter.FromNeutral(body, context, generator));
        Assert.Contains("did not survive", exception.Message);
    }

    [Fact]
    public void PlaceholderLocalInvariant_CompactLocalWithoutLocalBuilder_RoundTripsWithoutDeclaringNewLocal()
    {
        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Ldloc_0),
            new CodeInstruction(OpCodes.Stloc_0),
            new CodeInstruction(OpCodes.Ret),
        };

        NeutralBody neutral = CodeInstructionConverter.ToNeutral(stream, out HarmonyStreamContext context);

        NeutralLocal slotZero = neutral.Locals.Find(local => local.Id == 0);
        Assert.NotNull(slotZero);
        Assert.Equal(typeof(object), slotZero.Type);
        Assert.True(slotZero.IlgenOwned);

        ILGenerator generator = NewGenerator();
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromNeutral(neutral, context, generator);

        Assert.Equal(OpCodes.Ldloc_0, outgoing[0].opcode);
        Assert.Null(outgoing[0].operand);
        Assert.Equal(OpCodes.Stloc_0, outgoing[1].opcode);
        Assert.Null(outgoing[1].operand);
    }
}
