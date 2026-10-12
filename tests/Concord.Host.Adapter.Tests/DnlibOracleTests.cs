using System.Reflection.Emit;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public class DnlibOracleTests {
    [Fact]
    public void ATinyBodyEncodesToTheSameBytesInBothEncoders()
        => AssertAgrees(Shape.Body([Shape.Step(OpCodes.Ldc_I4, 42), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Ret)], [], [], false), 1);

    [Fact]
    public void ABodyWithLocalsEncodesToTheSameBytesInBothEncoders()
        => AssertAgrees(
            Shape.Body(
                [Shape.Step(OpCodes.Ldc_I4_0), Shape.Step(OpCodes.Stloc_S, Shape.Slot(1, typeof(int))), Shape.Step(OpCodes.Ret)],
                [],
                [new ComposedLocal(typeof(int), false), new ComposedLocal(typeof(string), false)],
                true),
            1);

    [Fact]
    public void ACatchEncodesToTheSameBytesInBothEncoders()
        => AssertAgrees(Guarded(ClauseKind.Catch), 2);

    [Fact]
    public void AFinallyEncodesToTheSameBytesInBothEncoders()
        => AssertAgrees(Guarded(ClauseKind.Finally), 2);

    [Fact]
    public void AFaultEncodesToTheSameBytesInBothEncoders()
        => AssertAgrees(Guarded(ClauseKind.Fault), 2);

    [Fact]
    public void AFilterEncodesToTheSameBytesInBothEncoders() {
        Label exit = Labels()[0];
        AssertAgrees(
            Shape.Body(
                [
                    Shape.Step(OpCodes.Nop),
                    Shape.Step(OpCodes.Leave, exit),
                    Shape.Step(OpCodes.Ldc_I4_0),
                    Shape.Step(OpCodes.Endfilter),
                    Shape.Step(OpCodes.Pop),
                    Shape.Step(OpCodes.Leave, exit),
                    Shape.Step(OpCodes.Ret, null, exit),
                ],
                [new ComposedClause(ClauseKind.Filter, 0, 2, 4, 6, null, 2)],
                [],
                false),
            2);
    }

    [Fact]
    public void ThreeNestedHandlersEncodeToTheSameBytesInBothEncoders() {
        Label exit = Labels()[0];
        List<CodeInstruction> stream = [Shape.Step(OpCodes.Nop), Shape.Step(OpCodes.Nop), Shape.Step(OpCodes.Nop)];
        int tail = stream.Count;
        stream.AddRange([
            Shape.Step(OpCodes.Leave, exit),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Leave, exit),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Leave, exit),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Leave, exit),
            Shape.Step(OpCodes.Ret, null, exit),
        ]);

        ComposedClause[] clauses = [
            new ComposedClause(ClauseKind.Catch, 0, tail + 5, tail + 5, tail + 8, typeof(Exception), -1),
            new ComposedClause(ClauseKind.Catch, 1, tail + 3, tail + 3, tail + 5, typeof(InvalidOperationException), -1),
            new ComposedClause(ClauseKind.Catch, 2, tail + 1, tail + 1, tail + 3, typeof(ArgumentException), -1),
        ];

        AssertAgrees(Shape.Body([.. stream], clauses, [], false), 4);
    }

    [Fact]
    public void ASwitchEncodesToTheSameBytesInBothEncoders() {
        Label[] marks = Labels();
        AssertAgrees(
            Shape.Body(
                [
                    Shape.Step(OpCodes.Ldc_I4_0),
                    Shape.Step(OpCodes.Switch, new[] { marks[0], marks[1], marks[0] }),
                    Shape.Step(OpCodes.Nop),
                    Shape.Step(OpCodes.Nop, null, marks[0]),
                    Shape.Step(OpCodes.Ret, null, marks[1]),
                ],
                [],
                [],
                false),
            1);
    }

    [Fact]
    public void AWidenedBranchEncodesToTheSameBytesInBothEncoders() {
        Label exit = Labels()[0];
        List<CodeInstruction> stream = [Shape.Step(OpCodes.Br_S, exit)];
        for (int filler = 0; filler < 300; filler++) {
            stream.Add(Shape.Step(OpCodes.Nop));
        }

        stream.Add(Shape.Step(OpCodes.Ret, null, exit));
        byte[] agreed = AssertAgrees(Shape.Body([.. stream], [], [], false), 0);

        Assert.Equal(0x38, agreed[12]);
    }

    [Fact]
    public void ManyClausesPushTheExceptionSectionToItsFatFormInBothEncoders() {
        Label exit = Labels()[0];
        List<CodeInstruction> stream = [];
        List<ComposedClause> clauses = [];
        for (int clause = 0; clause < 24; clause++) {
            int start = stream.Count;
            stream.AddRange([Shape.Step(OpCodes.Nop), Shape.Step(OpCodes.Leave, exit), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Leave, exit)]);
            clauses.Add(new ComposedClause(ClauseKind.Catch, start, start + 2, start + 2, start + 4, typeof(Exception), -1));
        }

        stream.Add(Shape.Step(OpCodes.Ret, null, exit));
        byte[] agreed = AssertAgrees(Shape.Body([.. stream], [.. clauses], [], false), 25);
        int section = 12 + ((BitConverter.ToInt32(agreed, 4) + 3) & ~3);

        Assert.Equal(0x41, agreed[section]);
    }

    [Fact]
    public void PerturbingOneOperandBreaksTheByteDiff() {
        ComposedBody body = Shape.Body([Shape.Step(OpCodes.Ldc_I4, 42), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Ret)], [], [], false);
        ComposedBody perturbed = Shape.Body([Shape.Step(OpCodes.Ldc_I4, 43), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Ret)], [], [], false);

        byte[] mine = BodyEncoder.Encode(body, new Tokens(), new FixedStack(1)).Bytes;
        byte[] oracle = DnlibOracle.Encode(perturbed, new Tokens(), 1, out List<string> complaints);

        Assert.Empty(complaints);
        Assert.NotEqual(Convert.ToHexString(mine), Convert.ToHexString(oracle));
    }

    [Fact]
    public void OnlyTheSrmEncoderWidensTheHeaderForLocallocUnderInitLocals() {
        ComposedBody body = Shape.Body(
            [Shape.Step(OpCodes.Ldc_I4_8), Shape.Step(OpCodes.Localloc), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Ret)],
            [],
            [],
            true);

        byte[] mine = BodyEncoder.Encode(body, new Tokens(), new FixedStack(2)).Bytes;
        byte[] oracle = DnlibOracle.Encode(body, new Tokens(), 2, out List<string> complaints);

        Assert.Empty(complaints);
        Assert.Equal(3, mine[0] & 3);
        Assert.Equal(0x10, mine[1] & 0x10);
        Assert.Equal(2, oracle[0] & 3);
    }

    [Theory]
    [MemberData(nameof(RealBodies.Corpora), MemberType = typeof(RealBodies))]
    public void EveryReadableBodyEncodesToTheSameBytesInBothEncoders(string corpus)
        => RoundTripTests.AssertClean(Corpus.Run(RealBodies.Of(corpus), AssertBytesAgree), 80_000);

    [GameCorpusFact]
    public void EveryReadableGameBodyEncodesToTheSameBytesInBothEncoders()
        => RoundTripTests.AssertClean(Corpus.Run(RealBodies.Of(RealBodies.GameCorpus), AssertBytesAgree), 80_000);

    private static void AssertBytesAgree(System.Reflection.MethodBase method, ComposedBody body, EncodedBody encoded, Tokens tokens) {
        if (OnlySrmWidensThisHeader(body, encoded)) {
            return;
        }

        byte[] oracle = DnlibOracle.Encode(body, new Tokens(), encoded.MaxStack, out List<string> complaints);

        Assert.Empty(complaints);
        if (!encoded.Bytes.AsSpan().SequenceEqual(oracle)) {
            Assert.Equal(Convert.ToHexString(encoded.Bytes), Convert.ToHexString(oracle));
        }
    }

    private static bool OnlySrmWidensThisHeader(ComposedBody body, EncodedBody encoded) {
        return body.InitLocals
            && body.Locals.Count == 0
            && body.Clauses.Count == 0
            && encoded.MaxStack <= 8
            && encoded.CodeSize < 64;
    }

    private static byte[] AssertAgrees(ComposedBody body, int maxStack) {
        byte[] mine = BodyEncoder.Encode(body, new Tokens(), new FixedStack(maxStack)).Bytes;
        byte[] oracle = DnlibOracle.Encode(body, new Tokens(), maxStack, out List<string> complaints);

        Assert.Empty(complaints);
        Assert.Equal(Convert.ToHexString(mine), Convert.ToHexString(oracle));
        return mine;
    }

    private static ComposedBody Guarded(ClauseKind kind) {
        Label exit = Labels()[0];
        return Shape.Body(
            [
                Shape.Step(OpCodes.Nop),
                Shape.Step(OpCodes.Leave, exit),
                Shape.Step(kind == ClauseKind.Catch ? OpCodes.Pop : OpCodes.Nop),
                Shape.Step(kind == ClauseKind.Catch ? OpCodes.Leave : OpCodes.Endfinally, kind == ClauseKind.Catch ? exit : null),
                Shape.Step(OpCodes.Ret, null, exit),
            ],
            [new ComposedClause(kind, 0, 2, 2, 4, kind == ClauseKind.Catch ? typeof(Exception) : null, -1)],
            [],
            false);
    }

    private static Label[] Labels() {
        ITranspilerContext context = TranspilerContextFactory.For(typeof(DnlibOracleTests).GetMethod(nameof(Labels), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!);
        Label[] marks = [context.DefineLabel(), context.DefineLabel()];

        Assert.NotEqual(marks[0], marks[1]);
        return marks;
    }
}
