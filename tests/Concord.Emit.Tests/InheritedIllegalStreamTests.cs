using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public static class InheritedStreamTranspilers {
    public static IEnumerable<CodeInstruction> Identity(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            yield return instruction;
        }
    }
}

public sealed class InheritedIllegalStreamTests {
    [Fact]
    public void AStreamAlreadyIllegalOnArrival_BlamesNoConcordTranspiler() {
        ConcordEmitException failure = Assert.Throws<ConcordEmitException>(() => Compose());

        Assert.Equal("CONC175", failure.Code);
        Assert.DoesNotContain(nameof(InheritedStreamTranspilers.Identity), failure.Message);
        Assert.Null(failure.LocalBindingMethod);
        Assert.Null(failure.LocalBindingInjection);
    }

    [Fact]
    public void ALegalStream_StillComposesThroughTheSameTranspiler() {
        MethodBase target = Target();
        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Add),
            new CodeInstruction(OpCodes.Ret),
        ];

        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, [Passthrough()], context);

        MethodInfo generated = StreamRoundTrip.Generate(composed, target);
        Assert.Equal(42, generated.Invoke(null, [41]));
    }

    private static List<CodeInstruction> Compose() {
        MethodBase target = Target();
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        return WrapperComposer.TransformStream(target, FaultWrapped(), [Passthrough()], context);
    }

    private static List<CodeInstruction> FaultWrapped() {
        CodeInstruction first = new CodeInstruction(OpCodes.Ldarg_0);
        first.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));

        CodeInstruction ret = new CodeInstruction(OpCodes.Ret);
        ret.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFaultBlock));

        CodeInstruction tail = new CodeInstruction(OpCodes.Nop);
        tail.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

        return [
            first,
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Add),
            ret,
            tail,
        ];
    }

    private static Injection Passthrough() {
        return new Injection(
            typeof(InheritedStreamTranspilers).GetMethod(nameof(InheritedStreamTranspilers.Identity))!,
            new InjectAt.Transpiler(false),
            "test.concord.passthrough",
            0);
    }

    private static MethodBase Target() {
        return typeof(StreamTargets).GetMethod(nameof(StreamTargets.Target))!;
    }
}
