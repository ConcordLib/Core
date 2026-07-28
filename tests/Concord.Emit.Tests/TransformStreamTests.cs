using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class TransformStreamTests {
    [Fact]
    public void TransformStream_ComposesHeadInjectionOntoSuppliedStream() {
        MethodBase target = typeof(StreamTargets).GetMethod(nameof(StreamTargets.Target))!;
        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Add),
            new CodeInstruction(OpCodes.Ret),
        ];
        Injection head = new Injection(
            typeof(StreamTargets).GetMethod(nameof(StreamTargets.HeadInjection))!,
            new InjectAt.Head(),
            "test",
            0);

        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [head], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        StreamTargets.HeadObserved = 0;
        object? result = generated.Invoke(null, [41]);
        Assert.Equal(42, result);
        Assert.Equal(1, StreamTargets.HeadObserved);
    }

    [Fact]
    public void TransformStream_PartitionsTranspilerInjectionInsteadOfRejecting() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldc_I4_5),
            new CodeInstruction(OpCodes.Ret),
        ];
        Injection transpiler = new Injection(
            typeof(PriceTranspilers).GetMethod(nameof(PriceTranspilers.FiveToTen))!,
            new InjectAt.Transpiler(false),
            "test",
            0);

        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [transpiler], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        Assert.Equal(10, generated.Invoke(null, null));
    }

    [Fact]
    public void TransformStream_ComposesATranspilerAlongsideADeclarativeInjection() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldc_I4_5),
            new CodeInstruction(OpCodes.Ret),
        ];
        Injection transpiler = new Injection(
            typeof(PriceTranspilers).GetMethod(nameof(PriceTranspilers.FiveToTen))!,
            new InjectAt.Transpiler(false),
            "test-transpiler",
            0);
        Injection head = new Injection(
            typeof(StreamTargets).GetMethod(nameof(StreamTargets.HeadInjection))!,
            new InjectAt.Head(),
            "test-head",
            0);

        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [transpiler, head], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        StreamTargets.HeadObserved = 0;
        Assert.Equal(10, generated.Invoke(null, null));
        Assert.Equal(1, StreamTargets.HeadObserved);
    }

    [Fact]
    public void TransformStream_ExceptionRegionRoundTripsAndExecutes() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.MultiCatch))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        LocalRef result = context.DeclareLocal(typeof(int));
        Label compute = context.DefineLabel();
        Label epilogue = context.DefineLabel();

        CodeInstruction computeLdarg = new CodeInstruction(OpCodes.Ldarg_0);
        computeLdarg.labels.Add(compute);
        CodeInstruction handlerPop = new CodeInstruction(OpCodes.Pop);
        CodeInstruction epilogueLdloc = new CodeInstruction(OpCodes.Ldloc, result);
        epilogueLdloc.labels.Add(epilogue);

        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Brtrue_S, compute),
            new CodeInstruction(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!),
            new CodeInstruction(OpCodes.Throw),
            computeLdarg,
            new CodeInstruction(OpCodes.Ldc_I4_2),
            new CodeInstruction(OpCodes.Mul),
            new CodeInstruction(OpCodes.Stloc, result),
            new CodeInstruction(OpCodes.Leave_S, epilogue),
            handlerPop,
            new CodeInstruction(OpCodes.Ldc_I4_M1),
            new CodeInstruction(OpCodes.Stloc, result),
            new CodeInstruction(OpCodes.Leave_S, epilogue),
            epilogueLdloc,
            new CodeInstruction(OpCodes.Ret),
        ];

        source[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        handlerPop.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(InvalidOperationException)));
        epilogueLdloc.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        Assert.Equal(6, generated.Invoke(null, [3]));
        Assert.Equal(-1, generated.Invoke(null, [0]));
    }

    [Fact]
    public void TransformStream_DeclaredLocalsLandAtExpectedIndices() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        LocalRef first = context.DeclareLocal(typeof(int));
        LocalRef second = context.DeclareLocal(typeof(int));

        Assert.Equal(0, first.Index);
        Assert.Equal(1, second.Index);

        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldc_I4, 100),
            new CodeInstruction(OpCodes.Stloc, first),
            new CodeInstruction(OpCodes.Ldc_I4, 1),
            new CodeInstruction(OpCodes.Stloc, second),
            new CodeInstruction(OpCodes.Ldloc, first),
            new CodeInstruction(OpCodes.Ldloc, second),
            new CodeInstruction(OpCodes.Sub),
            new CodeInstruction(OpCodes.Ret),
        ];

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        Assert.Equal(99, generated.Invoke(null, null));
    }

    [Fact]
    public void TransformStream_ContextNotFromCreateStreamContext_ThrowsConc116() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldc_I4_5),
            new CodeInstruction(OpCodes.Ret),
        ];
        ForeignTranspilerContext foreign = new ForeignTranspilerContext();

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.TransformStream(target, source, [], foreign));
        Assert.Equal("CONC116", ex.Code);
    }

    private sealed class ForeignTranspilerContext : ITranspilerContext {
        public MethodBase Original => typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;

        public Label DefineLabel() {
            return default;
        }

        public LocalRef DeclareLocal(Type type) {
            return default;
        }
    }

    [Fact]
    public void TransformStream_PreservesCallerLabelId_DirectBranch() {
        MethodBase target = typeof(StreamTargets).GetMethod(nameof(StreamTargets.Target))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);

        // Mint and discard one id first so skip is NOT id 0. A context-fresh read that assigns ids
        // purely by encounter order (the bug) would still hand back id 0 for the first branch target
        // it sees - which, with only one label in play, is skip's own target - masking the bug by
        // coincidence. Burning id 0 here makes a wrong, order-derived id observably differ from skip.
        _ = context.DefineLabel();
        Label skip = context.DefineLabel();

        CodeInstruction skipTarget = new CodeInstruction(OpCodes.Ldarg_0);
        skipTarget.labels.Add(skip);

        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Brtrue_S, skip),
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Ret),
            skipTarget,
            new CodeInstruction(OpCodes.Ret),
        ];

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [], context);

        AssertEachLabelAttachedToExactlyOneInstruction(composed);

        CodeInstruction branch = FindOpcode(composed, OpCodes.Brtrue_S);
        Label resolved = Assert.IsType<Label>(branch.operand);
        Assert.Equal(skip, resolved);
        Assert.Equal(OpCodes.Ldarg_0, FindLabeled(composed, resolved).opcode);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        Assert.Equal(7, generated.Invoke(null, [7]));
        Assert.Equal(0, generated.Invoke(null, [0]));
    }

    [Fact]
    public void TransformStream_PreservesCallerLabelId_WhenCompositionInsertsBetweenBranchAndTarget() {
        MethodBase target = typeof(StreamTargets).GetMethod(nameof(StreamTargets.Target))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);

        // See TransformStream_PreservesCallerLabelId_DirectBranch for why this burns id 0 first.
        _ = context.DefineLabel();
        Label skip = context.DefineLabel();

        CodeInstruction skipTarget = new CodeInstruction(OpCodes.Ldarg_0);
        skipTarget.labels.Add(skip);

        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Brtrue_S, skip),
            new CodeInstruction(OpCodes.Ldc_I4, 55),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Ret),
            skipTarget,
            new CodeInstruction(OpCodes.Ret),
        ];
        Injection marker = new Injection(
            typeof(LabelProvenanceInjections).GetMethod(nameof(LabelProvenanceInjections.Passthrough))!,
            new InjectAt.Constant(55, 0),
            "marker",
            0);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [marker], context);

        AssertEachLabelAttachedToExactlyOneInstruction(composed);

        CodeInstruction branch = FindOpcode(composed, OpCodes.Brtrue_S);
        Label resolved = Assert.IsType<Label>(branch.operand);
        Assert.Equal(skip, resolved);
        Assert.Equal(OpCodes.Ldarg_0, FindLabeled(composed, resolved).opcode);

        LabelProvenanceInjections.MarkerCalls = 0;
        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        Assert.Equal(0, generated.Invoke(null, [0]));
        Assert.Equal(1, LabelProvenanceInjections.MarkerCalls);
        Assert.Equal(7, generated.Invoke(null, [7]));
    }

    [Fact]
    public void TransformStream_NewLabelsIntroducedByCompositionDoNotCollideWithCallerIds() {
        MethodBase target = typeof(StreamTargets).GetMethod(nameof(StreamTargets.Target))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        Label skip = context.DefineLabel();

        CodeInstruction skipTarget = new CodeInstruction(OpCodes.Ldarg_0);
        skipTarget.labels.Add(skip);

        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Brtrue_S, skip),
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Ret),
            skipTarget,
            new CodeInstruction(OpCodes.Ret),
        ];
        Injection head = new Injection(
            typeof(StreamTargets).GetMethod(nameof(StreamTargets.HeadInjection))!,
            new InjectAt.Head(),
            "test",
            0);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [head], context);

        AssertEachLabelAttachedToExactlyOneInstruction(composed);

        CodeInstruction branch = FindOpcode(composed, OpCodes.Brtrue_S);
        Label resolvedSkip = Assert.IsType<Label>(branch.operand);
        Assert.Equal(skip, resolvedSkip);

        bool foundOtherLabel = false;
        foreach (CodeInstruction instruction in composed) {
            foreach (Label label in instruction.labels) {
                if (!label.Equals(skip)) {
                    foundOtherLabel = true;
                }
            }
        }

        Assert.True(foundOtherLabel, "Expected the Head injection's guard branch to introduce a fresh label distinct from the caller's.");

        StreamTargets.HeadObserved = 0;
        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        Assert.Equal(0, generated.Invoke(null, [0]));
        Assert.Equal(1, StreamTargets.HeadObserved);
    }

    // Contract: a caller label whose target instruction did not survive composition is dropped -
    // it appears on no instruction in the output - rather than throwing or silently resolving to
    // the wrong target. A whole-method Around injection is the cleanest way to force this: it
    // clones the original spine into per-copy bodies and never places the original instructions
    // (including anything the caller labeled) into the composed result.
    [Fact]
    public void TransformStream_CallerLabelWhoseTargetDidNotSurviveComposition_IsDropped() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        Label orphan = context.DefineLabel();

        CodeInstruction orphanTarget = new CodeInstruction(OpCodes.Nop);
        orphanTarget.labels.Add(orphan);

        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldc_I4_5),
            orphanTarget,
            new CodeInstruction(OpCodes.Ret),
        ];
        Injection around = new Injection(
            typeof(OrphanedLabelAroundInjection).GetMethod(nameof(OrphanedLabelAroundInjection.Around))!,
            new InjectAt.Around(),
            "around",
            0);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [around], context);

        foreach (CodeInstruction instruction in composed) {
            Assert.DoesNotContain(orphan, instruction.labels);
        }

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        Assert.Equal(5, generated.Invoke(null, null));
    }

    private static CodeInstruction FindOpcode(List<CodeInstruction> instructions, OpCode opcode) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.opcode == opcode) {
                return instruction;
            }
        }

        throw new InvalidOperationException($"No instruction with opcode '{opcode}' found in the composed stream.");
    }

    private static CodeInstruction FindLabeled(List<CodeInstruction> instructions, Label label) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.labels.Contains(label)) {
                return instruction;
            }
        }

        throw new InvalidOperationException($"No instruction in the composed stream carries label '{label}'.");
    }

    private static void AssertEachLabelAttachedToExactlyOneInstruction(List<CodeInstruction> instructions) {
        Dictionary<Label, CodeInstruction> owners = new Dictionary<Label, CodeInstruction>();
        foreach (CodeInstruction instruction in instructions) {
            foreach (Label label in instruction.labels) {
                if (owners.TryGetValue(label, out CodeInstruction? existing) && !ReferenceEquals(existing, instruction)) {
                    Assert.Fail($"Label '{label}' is attached to more than one instruction in the composed stream.");
                }

                owners[label] = instruction;
            }
        }
    }

    private static class LabelProvenanceInjections {
        public static int MarkerCalls;

        public static int Passthrough(int original) {
            MarkerCalls++;
            return original;
        }
    }

    private static class OrphanedLabelAroundInjection {
        public static int Around(Operation<int> original) {
            return original.Invoke();
        }
    }
}
