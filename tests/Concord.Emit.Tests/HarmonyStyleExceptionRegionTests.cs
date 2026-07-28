using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public static class EndOfBodyFinallyProbe {
    public static int RanCount;

    public static void MarkRan() {
        RanCount++;
    }
}

// Probes the technique a Concord.Harmony rewrite depends on for importing a caller's own
// transpiler stream: Harmony places EndExceptionBlock on a handler's LAST instruction
// (HarmonyLib.MethodBodyReader.ParseExceptions does GetInstruction(HandlerOffset + HandlerLength
// - 1, isEndOfInstruction: true)), while Concord places it on the instruction AFTER the region
// (CecilCodeConverter.AttachExceptionBlocks). A caller building a stream from scratch also has no
// way to express "this handler runs to the literal end of the body" the way
// TranspilerContext.EndOfBodyBlocks does internally, since that set is keyed on ExceptionBlock
// object identity minted by CecilCodeConverter.ToInstructions itself.
//
// The technique under test: append one sacrificial Nop to the imported stream so every End
// marker has an i+1 instruction to shift onto, shift every marker forward by one, compose, then
// strip the filler back out on the way back. Entirely bridge-local - no production code touched.
public sealed class HarmonyStyleExceptionRegionTests {
    [Fact]
    public void MidBodyCatch_HarmonyEndPlacement_ShiftsOntoNextInstructionAndExecutes() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.MultiCatch))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        LocalRef result = context.DeclareLocal(typeof(int));
        Label compute = context.DefineLabel();
        Label epilogue = context.DefineLabel();

        CodeInstruction computeLdarg = new CodeInstruction(OpCodes.Ldarg_0);
        computeLdarg.labels.Add(compute);
        CodeInstruction handlerStart = new CodeInstruction(OpCodes.Pop);
        CodeInstruction handlerLastInstruction = new CodeInstruction(OpCodes.Leave_S, epilogue);
        CodeInstruction epilogueLdloc = new CodeInstruction(OpCodes.Ldloc, result);
        epilogueLdloc.labels.Add(epilogue);

        List<CodeInstruction> harmonyStyle = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Brtrue_S, compute),
            new CodeInstruction(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!),
            new CodeInstruction(OpCodes.Throw),
            computeLdarg,
            new CodeInstruction(OpCodes.Ldc_I4_2),
            new CodeInstruction(OpCodes.Mul),
            new CodeInstruction(OpCodes.Stloc, result),
            new CodeInstruction(OpCodes.Leave_S, epilogue),
            handlerStart,
            new CodeInstruction(OpCodes.Ldc_I4_M1),
            new CodeInstruction(OpCodes.Stloc, result),
            handlerLastInstruction,
            epilogueLdloc,
            new CodeInstruction(OpCodes.Ret),
        ];

        harmonyStyle[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        handlerStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(InvalidOperationException)));
        handlerLastInstruction.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

        ApplyHarmonyEndPlacement(harmonyStyle);

        Assert.Empty(handlerLastInstruction.blocks);
        Assert.Contains(epilogueLdloc.blocks, block => block.blockType == ExceptionBlockType.EndExceptionBlock);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, harmonyStyle, [], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        Assert.Equal(6, generated.Invoke(null, [3]));
        Assert.Equal(-1, generated.Invoke(null, [0]));
    }

    // The interesting case: MonoMod's own DynamicMethodDefinition.Generate() cannot even JIT a
    // body whose HandlerEnd is genuinely null (see
    // CecilCodeConverterRoundTripTests.RoundTrip_HandlerRunsToEndOfBody_PreservesNullHandlerEnd),
    // so faithfully reproducing "runs to end of body" would be a dead end regardless of whether a
    // caller could express it. Landing the marker on a real trailing instruction sidesteps that
    // limitation entirely - not just the object-identity gap in EndOfBodyBlocks.
    [Fact]
    public void EndOfBodyCatch_HarmonyEndPlacement_LandsOnSacrificialNopAndExecutes() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.MultiCatch))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);

        CodeInstruction tryStart = new CodeInstruction(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!);
        CodeInstruction handlerStart = new CodeInstruction(OpCodes.Pop);
        CodeInstruction throwCatch = new CodeInstruction(OpCodes.Throw);

        List<CodeInstruction> harmonyStyle = [
            tryStart,
            new CodeInstruction(OpCodes.Throw),
            handlerStart,
            new CodeInstruction(OpCodes.Newobj, typeof(ArgumentException).GetConstructor(Type.EmptyTypes)!),
            throwCatch,
        ];

        tryStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        handlerStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(InvalidOperationException)));
        throwCatch.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

        CodeInstruction sacrificialNop = ApplyHarmonyEndPlacement(harmonyStyle);

        Assert.Empty(throwCatch.blocks);
        Assert.Contains(sacrificialNop.blocks, block => block.blockType == ExceptionBlockType.EndExceptionBlock);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, harmonyStyle, [], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(() => generated.Invoke(null, [0]));
        Assert.IsType<ArgumentException>(thrown.InnerException);
    }

    [Fact]
    public void EndOfBodyFinally_HarmonyEndPlacement_LandsOnSacrificialNopAndExecutes() {
        MethodBase target = typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.Target))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);

        CodeInstruction tryStart = new CodeInstruction(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!);
        CodeInstruction handlerStart = new CodeInstruction(OpCodes.Call, typeof(EndOfBodyFinallyProbe).GetMethod(nameof(EndOfBodyFinallyProbe.MarkRan))!);
        CodeInstruction endFinally = new CodeInstruction(OpCodes.Endfinally);

        List<CodeInstruction> harmonyStyle = [
            tryStart,
            new CodeInstruction(OpCodes.Throw),
            handlerStart,
            endFinally,
        ];

        tryStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        handlerStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock));
        endFinally.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

        CodeInstruction sacrificialNop = ApplyHarmonyEndPlacement(harmonyStyle);

        Assert.Empty(endFinally.blocks);
        Assert.Contains(sacrificialNop.blocks, block => block.blockType == ExceptionBlockType.EndExceptionBlock);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, harmonyStyle, [], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        EndOfBodyFinallyProbe.RanCount = 0;
        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(() => generated.Invoke(null, [0]));
        Assert.IsType<InvalidOperationException>(thrown.InnerException);
        Assert.Equal(1, EndOfBodyFinallyProbe.RanCount);
    }

    [Fact]
    public void MidBodyCatch_WithHeadInjectionShiftingPositions_StillExecutesCorrectly() {
        MethodBase target = typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.Target))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        LocalRef result = context.DeclareLocal(typeof(int));
        Label compute = context.DefineLabel();
        Label epilogue = context.DefineLabel();

        CodeInstruction computeLdarg = new CodeInstruction(OpCodes.Ldarg_0);
        computeLdarg.labels.Add(compute);
        CodeInstruction handlerStart = new CodeInstruction(OpCodes.Pop);
        CodeInstruction handlerLastInstruction = new CodeInstruction(OpCodes.Leave_S, epilogue);
        CodeInstruction epilogueLdloc = new CodeInstruction(OpCodes.Ldloc, result);
        epilogueLdloc.labels.Add(epilogue);

        List<CodeInstruction> harmonyStyle = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Brtrue_S, compute),
            new CodeInstruction(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!),
            new CodeInstruction(OpCodes.Throw),
            computeLdarg,
            new CodeInstruction(OpCodes.Ldc_I4_2),
            new CodeInstruction(OpCodes.Mul),
            new CodeInstruction(OpCodes.Stloc, result),
            new CodeInstruction(OpCodes.Leave_S, epilogue),
            handlerStart,
            new CodeInstruction(OpCodes.Ldc_I4_M1),
            new CodeInstruction(OpCodes.Stloc, result),
            handlerLastInstruction,
            epilogueLdloc,
            new CodeInstruction(OpCodes.Ret),
        ];

        harmonyStyle[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        handlerStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(InvalidOperationException)));
        handlerLastInstruction.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

        ApplyHarmonyEndPlacement(harmonyStyle);

        Injection head = new Injection(
            typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.HeadInjection))!,
            new InjectAt.Head(),
            "test",
            0);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, harmonyStyle, [head], context);

        // The Head injection inserts its own body ahead of the guard/spine, so every instruction
        // in our region - including the sacrificial Nop and wherever the End marker landed - now
        // sits at a materially different absolute index than it did in harmonyStyle. The
        // shift technique never depended on absolute indices (it only ever consulted i+1
        // adjacency in the pre-composition list), so this proves that held.
        Assert.True(composed.Count > harmonyStyle.Count, "Expected the Head injection to insert instructions ahead of the region.");

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        BodyTransformerTargets.HeadObserved = 0;
        Assert.Equal(6, generated.Invoke(null, [3]));
        Assert.Equal(1, BodyTransformerTargets.HeadObserved);
        Assert.Equal(-1, generated.Invoke(null, [0]));
        Assert.Equal(2, BodyTransformerTargets.HeadObserved);
    }

    [Fact]
    public void EndOfBodyCatch_StrippingSacrificialNopAfterward_StillConvertsAndExecutes() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.MultiCatch))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);

        CodeInstruction tryStart = new CodeInstruction(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!);
        CodeInstruction handlerStart = new CodeInstruction(OpCodes.Pop);
        CodeInstruction throwCatch = new CodeInstruction(OpCodes.Throw);

        List<CodeInstruction> harmonyStyle = [
            tryStart,
            new CodeInstruction(OpCodes.Throw),
            handlerStart,
            new CodeInstruction(OpCodes.Newobj, typeof(ArgumentException).GetConstructor(Type.EmptyTypes)!),
            throwCatch,
        ];

        tryStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        handlerStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(InvalidOperationException)));
        throwCatch.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

        ApplyHarmonyEndPlacement(harmonyStyle);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, harmonyStyle, [], context);

        // TransformStream's Cecil round trip produces entirely new CodeInstruction objects, so
        // the filler cannot be found by reference on the way back out - only structurally, as the
        // sole instruction carrying an EndExceptionBlock marker. StripSacrificialNop below
        // exercises exactly that lookup, which is what a real bridge would also be limited to.
        int beforeCount = composed.Count;
        StripSacrificialNop(composed);
        Assert.Equal(beforeCount - 1, composed.Count);
        Assert.Single(composed, instruction => instruction.blocks.Exists(block => block.blockType == ExceptionBlockType.EndExceptionBlock));

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(() => generated.Invoke(null, [0]));
        Assert.IsType<ArgumentException>(thrown.InnerException);
    }

    // Simulates the bridge importing a Harmony-style stream: appends one sacrificial Nop to the
    // end, then shifts every EndExceptionBlock marker forward by one instruction so it lands on
    // Concord's own "instruction after the region" convention. Mid-body handlers land on their
    // existing next real instruction; a handler whose last instruction is the stream's own last
    // instruction lands on the freshly appended Nop.
    //
    // Walks backward so a marker just shifted onto i+1 is never revisited as "current" later in
    // the same pass - a forward walk would treat that freshly-landed marker as though Harmony had
    // placed it there and shift it a second time.
    private static CodeInstruction ApplyHarmonyEndPlacement(List<CodeInstruction> harmonyStyle) {
        CodeInstruction sacrificialNop = new CodeInstruction(OpCodes.Nop);
        harmonyStyle.Add(sacrificialNop);

        for (int i = harmonyStyle.Count - 2; i >= 0; i--) {
            CodeInstruction current = harmonyStyle[i];
            List<ExceptionBlock> ends = new List<ExceptionBlock>();
            foreach (ExceptionBlock block in current.blocks) {
                if (block.blockType == ExceptionBlockType.EndExceptionBlock) {
                    ends.Add(block);
                }
            }

            foreach (ExceptionBlock end in ends) {
                current.blocks.Remove(end);
                harmonyStyle[i + 1].blocks.Add(end);
            }
        }

        return sacrificialNop;
    }

    // Simulates stripping the bridge's sacrificial Nop back out of a composed stream on the way
    // out. The filler is found structurally - the sole plain Nop carrying an EndExceptionBlock
    // marker - and its marker is re-homed onto the instruction that now follows it so the result
    // still respects Concord's own placement convention.
    private static void StripSacrificialNop(List<CodeInstruction> composed) {
        int index = composed.FindIndex(instruction =>
            instruction.opcode == OpCodes.Nop &&
            instruction.operand is null &&
            instruction.blocks.Exists(block => block.blockType == ExceptionBlockType.EndExceptionBlock));

        if (index < 0) {
            return;
        }

        if (index == composed.Count - 1) {
            throw new InvalidOperationException("Sacrificial Nop is the last instruction in the composed stream; nothing to re-home its markers onto.");
        }

        CodeInstruction filler = composed[index];
        CodeInstruction next = composed[index + 1];
        foreach (ExceptionBlock block in filler.blocks) {
            next.blocks.Add(block);
        }

        foreach (Label label in filler.labels) {
            next.labels.Add(label);
        }

        composed.RemoveAt(index);
    }
}
