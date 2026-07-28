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

// Exercises CodeInstructionConverter.ToConcord/FromConcord directly against the
// Concord.CodeInstruction model the harmony-port rewrite (P6-P8) replaced the old neutral IL
// model with. This is the converter's own unit contract - operand/label/local mapping and
// exception marker placement - independent of whether Harmony can actually emit the result. Full
// behavioral coverage against the live Harmony bridge (try/catch/finally/fault surviving an
// actual patch) lives in ExceptionRegionCoexistenceTests.cs.
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

    // Pure data-level check: the converter itself can carry a filter marker through
    // ToConcord/FromConcord unchanged. This does NOT mean a `when` filter clause survives an
    // actual Harmony transpile - that is a confirmed Harmony 2.4.2 bug (MethodBodyReader.ParseExceptions
    // never hands a transpiler the filter handler's own HandlerStart marker), tracked by the
    // correctly-skipped ExceptionRegionCoexistenceTests.ExceptionFilter_... test. Do not un-skip
    // that test based on this one passing.
    [Fact]
    public void FilterBlock_MarkerRoundTripsThroughTheConverterDataModel()
    {
        ILGenerator generator = NewGenerator();

        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Nop).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginExceptFilterBlock)),
            new CodeInstruction(OpCodes.Ret),
        };

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out HarmonyStreamContext harmonyContext);

        Assert.Single(concord[0].blocks);
        Assert.Equal(Concord.ExceptionBlockType.BeginExceptFilterBlock, concord[0].blocks[0].blockType);

        List<CodeInstruction> outgoing = CodeInstructionConverter.FromConcord(concord, harmonyContext, generator);

        Assert.Single(outgoing[0].blocks);
        Assert.Equal(ExceptionBlockType.BeginExceptFilterBlock, outgoing[0].blocks[0].blockType);
    }

    [Fact]
    public void FaultBlock_RoundTrips()
    {
        ILGenerator generator = NewGenerator();

        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Nop).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock)),
            new CodeInstruction(OpCodes.Pop).WithBlocks(new ExceptionBlock(ExceptionBlockType.BeginFaultBlock)),
            new CodeInstruction(OpCodes.Nop).WithBlocks(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock)),
            new CodeInstruction(OpCodes.Ret),
        };

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out HarmonyStreamContext harmonyContext);

        Assert.Single(concord[1].blocks);
        Assert.Equal(Concord.ExceptionBlockType.BeginFaultBlock, concord[1].blocks[0].blockType);

        List<CodeInstruction> outgoing = CodeInstructionConverter.FromConcord(concord, harmonyContext, generator);

        Assert.Single(outgoing[0].blocks);
        Assert.Equal(ExceptionBlockType.BeginExceptionBlock, outgoing[0].blocks[0].blockType);
        Assert.Single(outgoing[1].blocks);
        Assert.Equal(ExceptionBlockType.BeginFaultBlock, outgoing[1].blocks[0].blockType);
        Assert.Single(outgoing[2].blocks);
        Assert.Equal(ExceptionBlockType.EndExceptionBlock, outgoing[2].blocks[0].blockType);
    }

    // Harmony's MethodBodyReader hands a transpiler the long-form Ldarg/Starg argument index as a
    // raw `short` (OperandType.InlineVar), used for any argument slot the one-byte short form
    // (Ldarg_S, OperandType.ShortInlineVar) cannot address. The old neutral IL model canonicalised
    // every argument load down to a byte-sized short-form slot and threw once the index exceeded
    // 255; CodeInstructionConverter carries the opcode through unchanged (see
    // CecilCodeConverter.CreateInstruction's IsArgumentOperand/ResolveParameter path, which
    // resolves ldarg/starg against the target's real parameter list rather than forcing Ldarg_S),
    // so a slot above 255 now round-trips instead of throwing.
    [Fact]
    public void ArgumentSlotOver255_RoundTripsWithoutTheOldShortFormCeiling()
    {
        ILGenerator generator = NewGenerator();

        List<CodeInstruction> stream = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Ldarg, (short)300),
            new CodeInstruction(OpCodes.Ret),
        };

        Concord.ITranspilerContext context = NewStreamContext();
        List<Concord.CodeInstruction> concord = CodeInstructionConverter.ToConcord(stream, context, out HarmonyStreamContext harmonyContext);

        Assert.Equal(OpCodes.Ldarg, concord[0].opcode);
        Assert.Equal(300, concord[0].operand);

        List<CodeInstruction> outgoing = CodeInstructionConverter.FromConcord(concord, harmonyContext, generator);

        Assert.Equal(OpCodes.Ldarg, outgoing[0].opcode);
        Assert.Equal(300, outgoing[0].operand);
    }

    // Mirrors WrapperComposer.TransformStream's own contract for a caller label whose target did
    // not survive composition (see TransformStreamTests.TransformStream_CallerLabelWhoseTargetDidNotSurviveComposition_IsDropped):
    // FromConcord no longer treats a Concord.Label with no known Harmony counterpart as an error.
    // ResolveLabel mints a fresh Harmony label the first time it sees one and reuses that same
    // label for every later reference, rather than throwing.
    [Fact]
    public void UsedButUnmarkedLabel_FromConcordMintsAndCachesAFreshLabel()
    {
        ILGenerator generator = NewGenerator();
        Concord.ITranspilerContext context = NewStreamContext();
        Concord.Label unmarked = context.DefineLabel();

        List<Concord.CodeInstruction> composed = new List<Concord.CodeInstruction>
        {
            new Concord.CodeInstruction(OpCodes.Brtrue, unmarked),
            new Concord.CodeInstruction(OpCodes.Br, unmarked),
            new Concord.CodeInstruction(OpCodes.Ret),
        };

        HarmonyStreamContext harmonyContext = new HarmonyStreamContext();
        List<CodeInstruction> outgoing = CodeInstructionConverter.FromConcord(composed, harmonyContext, generator);

        Label first = Assert.IsType<Label>(outgoing[0].operand);
        Label second = Assert.IsType<Label>(outgoing[1].operand);
        Assert.Equal(first, second);
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
