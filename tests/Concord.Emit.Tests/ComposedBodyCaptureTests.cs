using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;
using Xunit.Abstractions;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace Concord.Emit.Tests;

public sealed class ComposedBodyCaptureTests {
    private readonly ITestOutputHelper output;

    public ComposedBodyCaptureTests(ITestOutputHelper output) {
        this.output = output;
    }

    [Fact]
    public void CaptureOff_DescribesNoBody() {
        ComposeResult result = Compose(capture: false, Target(), Head());

        Assert.Null(result.Body);
        Assert.Null(result.BodyRejection);
    }

    [Fact]
    public void CapturedSpine_CarriesTheOriginalOffsets() {
        ComposeResult result = Compose(capture: true, Target(), Head());

        Assert.Null(result.BodyRejection);
        Assert.NotNull(result.Body);

        HashSet<int> original = OriginalOffsets(Target());
        Assert.True(original.Count > 1 && original.Max() > 0, "every source offset is 0, so a carried offset would prove nothing");

        HashSet<int> carried = [];
        foreach (ComposedInstruction instruction in result.Body!.Instructions) {
            if (instruction.OriginOffset is int offset) {
                carried.Add(offset);
            }
        }

        Assert.Equal(original, carried);
    }

    [Fact]
    public void InjectedIl_NamesTheInjectionThatProducedIt() {
        ComposeResult result = Compose(capture: true, Target(), Head());

        Assert.Null(result.BodyRejection);
        Assert.NotNull(result.Body);

        HashSet<MethodBase> named = [];
        foreach (ComposedInstruction instruction in result.Body!.Instructions) {
            if (instruction.OriginInjection is MethodBase injection) {
                named.Add(injection);
            }
        }

        MethodBase head = typeof(OriginTargets).GetMethod(nameof(OriginTargets.Head))!;
        Assert.Equal(head, Assert.Single(named));
        Assert.Equal(typeof(OriginTargets).Assembly, Assert.Single(named).DeclaringType!.Assembly);
    }

    [Fact]
    public void TwoInjections_AreToldApart() {
        ComposeResult result = Compose(capture: true, Target(), Head(), Around());

        Assert.Null(result.BodyRejection);
        Assert.NotNull(result.Body);

        HashSet<MethodBase> named = [];
        foreach (ComposedInstruction instruction in result.Body!.Instructions) {
            if (instruction.OriginInjection is MethodBase injection) {
                named.Add(injection);
            }
        }

        Assert.Equal(2, named.Count);
        Assert.Contains(typeof(OriginTargets).GetMethod(nameof(OriginTargets.Head))!, named);
        Assert.Contains(typeof(OriginTargets).GetMethod(nameof(OriginTargets.Around))!, named);
    }

    [Fact]
    public void SplicedSpine_StaysTargetIl() {
        ComposeResult result = Compose(capture: true, Target(), Around());

        Assert.True(result.Body is not null, "around compose refused a body: " + result.BodyRejection);

        HashSet<int> carried = [];
        foreach (ComposedInstruction instruction in result.Body!.Instructions) {
            if (instruction.OriginOffset is int offset) {
                carried.Add(offset);
                Assert.Null(instruction.OriginInjection);
            }
        }

        Assert.Equal(OriginalOffsets(Target()), carried);
    }

    [Fact]
    public void EveryInstructionCarriesAtMostOneOrigin() {
        foreach ((string Name, MethodBase Target, Injection[] Injections) shape in Shapes()) {
            ComposeResult result = Compose(capture: true, shape.Target, shape.Injections);
            Assert.True(result.Body is not null, shape.Name + " refused a body: " + result.BodyRejection);

            int spine = 0;
            int injected = 0;
            int neither = 0;
            foreach (ComposedInstruction instruction in result.Body!.Instructions) {
                Assert.False(
                    instruction.OriginOffset is not null && instruction.OriginInjection is not null,
                    shape.Name + ": an instruction claims both a target offset and an injection");

                if (instruction.OriginOffset is not null) {
                    spine++;
                } else if (instruction.OriginInjection is not null) {
                    injected++;
                } else {
                    neither++;
                }
            }

            int total = result.Body.Instructions.Count;
            output.WriteLine($"{shape.Name}: {spine} spine + {injected} injected + {neither} neither = {total}");
            Assert.Equal(total, spine + injected + neither);
            Assert.True(spine > 0 && injected > 0, shape.Name + " attributed nothing, so the sum proves nothing");
        }
    }

    // A DynamicMethod is refused as an operand because it has no metadata token; as attribution it is
    // never tokenized, and OriginalBody.Clone hands out DynamicMethodDefinitions in normal operation.
    [Fact]
    public void DynamicMethodAttribution_IsNotRefused() {
        DynamicMethod injection = new DynamicMethod("attribution", typeof(void), Type.EmptyTypes);
        ComposedBody body = new ComposedBody(
            [new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, injection)],
            [],
            [],
            false);

        Assert.Null(TokenizableOperands.Reject(body, Target()));
        Assert.NotNull(TokenizableOperands.Reject([new CodeInstruction(OpCodes.Call, injection)], Target()));
    }

    private static IEnumerable<(string Name, MethodBase Target, Injection[] Injections)> Shapes() {
        yield return ("head", Target(), [Head()]);
        yield return ("around", Target(), [Around()]);
        yield return ("head+around", Target(), [Head(), Around()]);
        yield return (
            "constant",
            typeof(ConstantHost).GetMethod(nameof(ConstantHost.ShouldEject))!,
            [new Injection(typeof(ConstantInjectionMethods).GetMethod(nameof(ConstantInjectionMethods.PlusOne))!, new InjectAt.Constant(18f), "capture-constant", 0)]);
        yield return (
            "wrap",
            typeof(ChainTarget).GetMethod(nameof(ChainTarget.RunTwice))!,
            [new Injection(typeof(ChainInjectionMethods).GetMethod(nameof(ChainInjectionMethods.AddTen))!, new InjectAt.Invoke(typeof(ChainFoo), nameof(ChainFoo.Bar), At.Around, 2), "capture-wrap", 0)]);
    }

    private static Injection Head() {
        return new Injection(typeof(OriginTargets).GetMethod(nameof(OriginTargets.Head))!, new InjectAt.Head(), "capture-head", 0);
    }

    private static Injection Around() {
        return new Injection(typeof(OriginTargets).GetMethod(nameof(OriginTargets.Around))!, new InjectAt.Around(), "capture-around", 0);
    }

    private static MethodBase Target() {
        return typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Simple))!;
    }

    private static HashSet<int> OriginalOffsets(MethodBase target) {
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        HashSet<int> offsets = [];
        foreach (Instruction instruction in source.Definition.Body.Instructions) {
            offsets.Add(instruction.Offset);
        }

        return offsets;
    }

    private static ComposeResult Compose(bool capture, MethodBase target, params Injection[] injections) {
        WrapperComposer.CaptureComposedBody = capture;
        try {
            return WrapperComposer.Compose(target, injections);
        } finally {
            WrapperComposer.CaptureComposedBody = false;
        }
    }
}
