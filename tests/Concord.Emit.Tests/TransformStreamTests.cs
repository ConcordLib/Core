using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class TransformStreamTests {
    [Fact]
    public void TransformStream_ComposesHeadInjectionOntoSuppliedStream() {
        MethodBase target = typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.Target))!;
        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Add),
            new CodeInstruction(OpCodes.Ret),
        ];
        Injection head = new Injection(
            typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.HeadInjection))!,
            new InjectAt.Head(),
            "test",
            0);

        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [head], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        BodyTransformerTargets.HeadObserved = 0;
        object? result = generated.Invoke(null, [41]);
        Assert.Equal(42, result);
        Assert.Equal(1, BodyTransformerTargets.HeadObserved);
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
            typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.HeadInjection))!,
            new InjectAt.Head(),
            "test-head",
            0);

        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [transpiler, head], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        BodyTransformerTargets.HeadObserved = 0;
        Assert.Equal(10, generated.Invoke(null, null));
        Assert.Equal(1, BodyTransformerTargets.HeadObserved);
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
}
