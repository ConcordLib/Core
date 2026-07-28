using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

using CodeInstruction = HarmonyLib.CodeInstruction;
using ExceptionBlock = HarmonyLib.ExceptionBlock;
using ExceptionBlockType = HarmonyLib.ExceptionBlockType;
using Label = System.Reflection.Emit.Label;

// Kept minimally compiling against the new Concord.CodeInstruction-based converter introduced by
// the harmony-port rewrite (P6). A full rewrite that exercises the new model properly - including
// positive-path coverage for filter/fault blocks and the removed short-form argument ceiling - is
// tracked separately (harmony-port P8); several tests below are placeholders until then.
public class CodeInstructionConverterTests
{
    private static ILGenerator NewGenerator()
    {
        DynamicMethod method = new DynamicMethod("m", typeof(void), Type.EmptyTypes);
        return method.GetILGenerator();
    }

    private static Concord.ITranspilerContext NewStreamContext()
    {
        MethodBase target = typeof(CodeInstructionConverterTests).GetMethod(nameof(NewGenerator), BindingFlags.NonPublic | BindingFlags.Static)!;
        return WrapperComposer.CreateStreamContext(target);
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

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out HarmonyStreamContext harmonyContext);
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromConcord(concord, harmonyContext, generator);

        Assert.Equal(stream.Count, outgoing.Count);
        Assert.Equal(OpCodes.Ldc_I4_S, outgoing[0].opcode);
        Assert.Equal((sbyte)5, outgoing[0].operand);
        Assert.Equal(OpCodes.Stloc_S, outgoing[1].opcode);
        Assert.Same(local, outgoing[1].operand);
        Assert.Equal(OpCodes.Ldloc_0, outgoing[2].opcode);
        Assert.Null(outgoing[2].operand);
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

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out HarmonyStreamContext harmonyContext);
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromConcord(concord, harmonyContext, generator);

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

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out HarmonyStreamContext harmonyContext);
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromConcord(concord, harmonyContext, generator);

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

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out HarmonyStreamContext harmonyContext);
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromConcord(concord, harmonyContext, generator);

        Assert.Equal(2, outgoing[0].blocks.Count);
        Assert.All(outgoing[0].blocks, block => Assert.Equal(ExceptionBlockType.BeginExceptionBlock, block.blockType));
    }

    [Fact]
    public void FilterBlock_ToConcordSucceeds()
    {
        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Nop).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginExceptFilterBlock)),
            new CodeInstruction(OpCodes.Ret),
        };

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out _);

        Assert.Single(concord[0].blocks);
        Assert.Equal(Concord.ExceptionBlockType.BeginExceptFilterBlock, concord[0].blocks[0].blockType);
    }

    [Fact]
    public void FaultBlock_ToConcordSucceeds()
    {
        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Nop).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginFaultBlock)),
            new CodeInstruction(OpCodes.Ret),
        };

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out _);

        Assert.Single(concord[0].blocks);
        Assert.Equal(Concord.ExceptionBlockType.BeginFaultBlock, concord[0].blocks[0].blockType);
    }

    [Fact(Skip = "The new model removes the old short-form-only argument ceiling (CecilCodeConverter.CreateInstruction resolves ldarg/starg against the target's real parameter count, not a forced byte-sized Ldarg_S). A positive-path replacement belongs in harmony-port P8.")]
    public void ArgumentSlotOver255_ToConcordSucceeds_FromConcordThrowsShortFormRange()
    {
    }

    [Fact(Skip = "FromConcord no longer throws for an unmarked label target: WrapperComposer.TransformStream's own contract is to silently drop a caller label whose target did not survive composition (see TransformStreamTests.TransformStream_CallerLabelWhoseTargetDidNotSurviveComposition_IsDropped), and ResolveLabel mirrors that by minting a fresh label rather than throwing. A replacement covering the new contract belongs in harmony-port P8.")]
    public void UsedButUnmarkedLabel_FromConcordThrows()
    {
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

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out HarmonyStreamContext harmonyContext);

        ILGenerator generator = NewGenerator();
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromConcord(concord, harmonyContext, generator);

        Assert.Equal(OpCodes.Ldloc_0, outgoing[0].opcode);
        Assert.Null(outgoing[0].operand);
        Assert.Equal(OpCodes.Stloc_0, outgoing[1].opcode);
        Assert.Null(outgoing[1].operand);
    }
}
