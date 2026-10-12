using System.Reflection;
using System.Reflection.Emit;
using Xunit;
using Xunit.Sdk;

namespace Concord.Host.Adapter.Tests;

public class BodyEncoderTests {
    private static readonly Module Here = typeof(BodyEncoderTests).Module;

    [Fact]
    public void ATinyBodyIsByteExactAgainstTheMeasuredBlob() {
        ComposedBody body = Shape.Body(
            [Shape.Step(OpCodes.Ldc_I4, 42), Shape.Step(OpCodes.Ret)],
            [],
            [],
            false);

        EncodedBody encoded = BodyEncoder.Encode(body, new Tokens(), new FixedStack(1));

        Assert.Equal("1A202A0000002A", Convert.ToHexString(encoded.Bytes));
        Assert.Equal(new[] { 0, 5 }, encoded.InstructionOffsets);
        Assert.Equal(6, encoded.CodeSize);
    }

    [Fact]
    public void AHandlerRunningToTheEndOfTheBodyKeepsItsLastInstruction() {
        ComposedBody body = CatchToEnd(handlerEnd: 5);

        Assert.Equal(body.Instructions.Count, body.Clauses[0].HandlerEnd);
        AssertHandlerReachesTheEnd(BodyEncoder.Encode(body, new Tokens(), new FixedStack(1)));
    }

    [Fact]
    public void TruncatingTheHandlerEndByOneFailsTheSameCheck() {
        EncodedBody truncated = BodyEncoder.Encode(CatchToEnd(handlerEnd: 4), new Tokens(), new FixedStack(1));

        Assert.Throws<EqualException>(() => AssertHandlerReachesTheEnd(truncated));
    }

    [Fact]
    public void NestedClausesAreEmittedInnermostFirst() {
        Label exit = Shape.Mark(1);
        ComposedClause outer = new ComposedClause(ClauseKind.Catch, 0, 5, 5, 7, typeof(Exception), -1);
        ComposedClause inner = new ComposedClause(ClauseKind.Catch, 1, 3, 3, 5, typeof(InvalidOperationException), -1);
        ComposedBody body = Shape.Body(
            [
                Shape.Step(OpCodes.Nop),
                Shape.Step(OpCodes.Nop),
                Shape.Step(OpCodes.Leave, exit),
                Shape.Step(OpCodes.Pop),
                Shape.Step(OpCodes.Leave, exit),
                Shape.Step(OpCodes.Pop),
                Shape.Step(OpCodes.Leave, exit),
                Shape.Step(OpCodes.Ret, null, exit),
            ],
            [outer, inner],
            [],
            false);

        Assert.Equal(0, body.Clauses[0].TryStart);

        EncodedBody encoded = BodyEncoder.Encode(body, new Tokens(), new FixedStack(2));
        Decoded decoded = ReadBack.Body(encoded.Bytes);

        Assert.Equal(2, decoded.Regions.Length);
        Assert.Equal(encoded.InstructionOffsets[1], decoded.Regions[0].TryOffset);
        Assert.Equal(encoded.InstructionOffsets[0], decoded.Regions[1].TryOffset);
    }

    [Fact]
    public void InnermostFirstNestsTightestFirstAndKeepsSiblingOrder() {
        ComposedClause[] clauses = [
            new ComposedClause(ClauseKind.Catch, 0, 12, 12, 14, typeof(Exception), -1),
            new ComposedClause(ClauseKind.Catch, 0, 6, 6, 8, typeof(Exception), -1),
            new ComposedClause(ClauseKind.Catch, 2, 4, 4, 6, typeof(Exception), -1),
            new ComposedClause(ClauseKind.Catch, 0, 12, 14, 16, typeof(Exception), -1),
        ];

        Assert.Equal(new[] { 2, 1, 0, 3 }, BodyEncoder.InnermostFirst(clauses));
    }

    [Fact]
    public void ShortBranchesAreWidenedToLongForm() {
        Label exit = Shape.Mark(1);
        List<CodeInstruction> stream = [Shape.Step(OpCodes.Br_S, exit)];
        for (int i = 0; i < 300; i++) {
            stream.Add(Shape.Step(OpCodes.Nop));
        }

        stream.Add(Shape.Step(OpCodes.Ret, null, exit));

        EncodedBody encoded = BodyEncoder.Encode(Shape.Body([.. stream], [], [], false), new Tokens(), new FixedStack(0));
        List<Step> steps = ReadBack.Steps(ReadBack.Body(encoded.Bytes).Il);

        Assert.Equal(OpCodes.Br, steps[0].OpCode);
        Assert.Equal(5, steps[0].Size);
        Assert.Equal(encoded.InstructionOffsets[301], steps[0].Size + steps[0].Operand);
    }

    [Fact]
    public void InstructionOffsetsMatchTheEncodedBytes() {
        Label exit = Shape.Mark(1);
        ComposedBody body = Shape.Body(
            [
                Shape.Step(OpCodes.Ldc_I4, 7),
                Shape.Step(OpCodes.Ldc_I4_S, (sbyte)-3),
                Shape.Step(OpCodes.Ldc_I8, 9L),
                Shape.Step(OpCodes.Pop),
                Shape.Step(OpCodes.Pop),
                Shape.Step(OpCodes.Pop),
                Shape.Step(OpCodes.Ldstr, "hello"),
                Shape.Step(OpCodes.Pop),
                Shape.Step(OpCodes.Br, exit),
                Shape.Step(OpCodes.Ret, null, exit),
            ],
            [],
            [],
            false);

        EncodedBody encoded = BodyEncoder.Encode(body, new Tokens(), new FixedStack(3));
        List<Step> steps = ReadBack.Steps(ReadBack.Body(encoded.Bytes).Il);

        Assert.Equal(encoded.InstructionOffsets, steps.Select(step => step.Offset).ToArray());
        Assert.Equal(encoded.CodeSize, steps[^1].Offset + steps[^1].Size);
    }

    [Fact]
    public void SwitchTargetsResolveToTheirInstructions() {
        Label first = Shape.Mark(1);
        Label second = Shape.Mark(2);
        ComposedBody body = Shape.Body(
            [
                Shape.Step(OpCodes.Ldc_I4_0),
                Shape.Step(OpCodes.Switch, new[] { first, second }),
                Shape.Step(OpCodes.Nop),
                Shape.Step(OpCodes.Nop, null, first),
                Shape.Step(OpCodes.Ret, null, second),
            ],
            [],
            [],
            false);

        EncodedBody encoded = BodyEncoder.Encode(body, new Tokens(), new FixedStack(1));
        byte[] il = ReadBack.Body(encoded.Bytes).Il;
        int table = encoded.InstructionOffsets[1] + 1;
        int after = table + 4 + 8;

        Assert.Equal(2, BitConverter.ToInt32(il, table));
        Assert.Equal(encoded.InstructionOffsets[3], after + BitConverter.ToInt32(il, table + 4));
        Assert.Equal(encoded.InstructionOffsets[4], after + BitConverter.ToInt32(il, table + 8));
    }

    [Fact]
    public void AFilterRegionCarriesItsFilterStart() {
        Label exit = Shape.Mark(1);
        ComposedBody body = Shape.Body(
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
            false);

        EncodedBody encoded = BodyEncoder.Encode(body, new Tokens(), new FixedStack(2));
        Decoded decoded = ReadBack.Body(encoded.Bytes);

        Assert.Equal(ExceptionRegionKind.Filter, decoded.Regions[0].Kind);
        Assert.Equal(encoded.InstructionOffsets[2], decoded.Regions[0].FilterOffset);
        Assert.Equal(encoded.InstructionOffsets[4], decoded.Regions[0].HandlerOffset);
        Assert.Equal(encoded.InstructionOffsets[6] - encoded.InstructionOffsets[4], decoded.Regions[0].HandlerLength);
    }

    [Fact]
    public void ACatchTypeGoesThroughTheTokenSeamIntoTheClause() {
        Tokens tokens = new Tokens();
        EncodedBody encoded = BodyEncoder.Encode(CatchToEnd(handlerEnd: 5), tokens, new FixedStack(1));

        Assert.Equal(tokens.TokenFor(typeof(Exception)), ReadBack.Body(encoded.Bytes).Regions[0].CatchToken);
    }

    [Fact]
    public void LocalsGoThroughTheSignatureSeamAndTheirTokenLandsInTheHeader() {
        Tokens tokens = new Tokens();
        ComposedBody body = Shape.Body(
            [Shape.Step(OpCodes.Ret)],
            [],
            [new ComposedLocal(typeof(int), false), new ComposedLocal(typeof(string), false), new ComposedLocal(typeof(object), false)],
            true);

        EncodedBody encoded = BodyEncoder.Encode(body, tokens, new FixedStack(0));
        Decoded decoded = ReadBack.Body(encoded.Bytes);

        Assert.Equal("0703080E1C", Convert.ToHexString(tokens.Locals!));
        Assert.Equal(0x11000001, decoded.LocalSignatureToken);
        Assert.True(decoded.InitLocals);
    }

    [Fact]
    public void ABodyWithNoLocalsAsksForNoSignatureToken() {
        Tokens tokens = new Tokens();
        EncodedBody encoded = BodyEncoder.Encode(Shape.Body([Shape.Step(OpCodes.Ret)], [], [], false), tokens, new FixedStack(0));
        Decoded decoded = ReadBack.Body(encoded.Bytes);

        Assert.Null(tokens.Locals);
        Assert.True(decoded.LocalSignatureIsNil);
        Assert.False(decoded.InitLocals);
    }

    [Fact]
    public void LocallocUnderInitLocalsForcesAFatHeader() {
        CodeInstruction[] withLocalloc = [Shape.Step(OpCodes.Ldc_I4_8), Shape.Step(OpCodes.Localloc), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Ret)];
        CodeInstruction[] without = [Shape.Step(OpCodes.Ldc_I4_8), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Ret)];

        Assert.Equal(3, BodyEncoder.Encode(Shape.Body(withLocalloc, [], [], true), new Tokens(), new FixedStack(2)).Bytes[0] & 3);
        Assert.Equal(2, BodyEncoder.Encode(Shape.Body(without, [], [], true), new Tokens(), new FixedStack(2)).Bytes[0] & 3);
        Assert.Equal(2, BodyEncoder.Encode(Shape.Body(withLocalloc, [], [], false), new Tokens(), new FixedStack(2)).Bytes[0] & 3);
    }

    [Fact]
    public void MaxStackComesFromTheSeam() {
        FixedStack stack = new FixedStack(9);
        ComposedBody body = Shape.Body([Shape.Step(OpCodes.Ret)], [], [], false);

        EncodedBody encoded = BodyEncoder.Encode(body, new Tokens(), stack);

        Assert.Same(body, stack.Asked);
        Assert.Equal(9, encoded.MaxStack);
        Assert.Equal(9, ReadBack.Body(encoded.Bytes).MaxStack);
    }

    [Fact]
    public void LocalSlotOperandsEncodeTheirIndex() {
        ComposedBody body = Shape.Body(
            [
                Shape.Step(OpCodes.Ldloc_S, Shape.Slot(3, typeof(int))),
                Shape.Step(OpCodes.Pop),
                Shape.Step(OpCodes.Ldloc, Shape.Slot(300, typeof(int))),
                Shape.Step(OpCodes.Pop),
                Shape.Step(OpCodes.Ret),
            ],
            [],
            [],
            false);

        byte[] il = ReadBack.Body(BodyEncoder.Encode(body, new Tokens(), new FixedStack(1)).Bytes).Il;

        Assert.Equal(3, il[1]);
        Assert.Equal(300, BitConverter.ToUInt16(il, 5));
    }

    [Fact]
    public void ArgumentSlotOperandsEncodeTheirIndex() {
        ComposedBody body = Shape.Body(
            [Shape.Step(OpCodes.Ldarg_S, (byte)4), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Ret)],
            [],
            [],
            false);

        Assert.Equal(4, ReadBack.Body(BodyEncoder.Encode(body, new Tokens(), new FixedStack(1)).Bytes).Il[1]);
    }

    [Fact]
    public void CalliIsRefusedBecauseNoTokenApiProducesAStandaloneSignature() {
        ComposedBody body = Shape.Body([Shape.Step(OpCodes.Calli), Shape.Step(OpCodes.Ret)], [], [], false);

        Assert.Throws<NotSupportedException>(() => BodyEncoder.Encode(body, new Tokens(), new FixedStack(1)));
    }

    [Fact]
    public void ABranchToALabelNoInstructionCarriesIsRefused() {
        ComposedBody body = Shape.Body([Shape.Step(OpCodes.Br, Shape.Mark(9)), Shape.Step(OpCodes.Ret)], [], [], false);

        Assert.Throws<ArgumentException>(() => BodyEncoder.Encode(body, new Tokens(), new FixedStack(0)));
    }

    [Fact]
    public void AClauseBoundaryPastTheEndOfTheBodyIsRefused() {
        Label exit = Shape.Mark(1);
        ComposedBody body = Shape.Body(
            [Shape.Step(OpCodes.Nop), Shape.Step(OpCodes.Leave, exit), Shape.Step(OpCodes.Endfinally), Shape.Step(OpCodes.Ret, null, exit)],
            [new ComposedClause(ClauseKind.Finally, 0, 2, 2, 5, null, -1)],
            [],
            false);

        Assert.Throws<ArgumentOutOfRangeException>(() => BodyEncoder.Encode(body, new Tokens(), new FixedStack(1)));
    }

    [Fact]
    public void AShortLocalSlotThatDoesNotFitOneByteIsRefused() {
        ComposedBody body = Shape.Body(
            [Shape.Step(OpCodes.Ldloc_S, Shape.Slot(300, typeof(int))), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Ret)],
            [],
            [],
            false);

        Assert.Throws<ArgumentOutOfRangeException>(() => BodyEncoder.Encode(body, new Tokens(), new FixedStack(1)));
    }

    [Fact]
    public void ARealSizedBodyWithThreeNestedHandlersEncodesEndToEnd() {
        Label exit = Shape.Mark(1);
        List<CodeInstruction> stream = [Shape.Step(OpCodes.Nop), Shape.Step(OpCodes.Nop), Shape.Step(OpCodes.Nop)];
        for (int i = 0; i < 800; i++) {
            stream.Add(Shape.Step(OpCodes.Nop));
        }

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

        EncodedBody encoded = BodyEncoder.Encode(Shape.Body([.. stream], clauses, [], false), new Tokens(), new FixedStack(4));
        Decoded decoded = ReadBack.Body(encoded.Bytes);
        List<Step> steps = ReadBack.Steps(decoded.Il);

        Assert.Equal(3, encoded.Bytes[0] & 3);
        Assert.Equal(encoded.InstructionOffsets, steps.Select(step => step.Offset).ToArray());
        Assert.Equal([encoded.InstructionOffsets[2], encoded.InstructionOffsets[1], encoded.InstructionOffsets[0]], decoded.Regions.Select(region => region.TryOffset).ToArray());
        Assert.Equal(encoded.CodeSize, decoded.Regions[2].HandlerOffset + decoded.Regions[2].HandlerLength);
    }

    [Fact]
    public void ALabelAttachedToTwoInstructionsIsRefused() {
        Label exit = Shape.Mark(1);
        ComposedBody body = Shape.Body(
            [Shape.Step(OpCodes.Br, exit), Shape.Step(OpCodes.Nop, null, exit), Shape.Step(OpCodes.Ret, null, exit)],
            [],
            [],
            false);

        Assert.Throws<ArgumentException>(() => BodyEncoder.Encode(body, new Tokens(), new FixedStack(0)));
    }

    [Fact]
    public void AGenericCallInRealIlReachesTheEncoderStillConstructed() {
        MethodBase source = typeof(BodyEncoderTests).GetMethod(nameof(CallsAGenericMethod), BindingFlags.NonPublic | BindingFlags.Static)!;
        ComposedBody body = RealBodies.Read(source, out string? skipped)
            ?? throw new InvalidOperationException($"the probe body did not read back: {skipped}");

        MethodInfo called = body.Instructions
            .Select(instruction => instruction.Instruction.operand)
            .OfType<MethodInfo>()
            .Single();

        Assert.True(called.IsGenericMethod);
        Assert.False(called.IsGenericMethodDefinition);
        Assert.Equal(typeof(int), Assert.Single(called.GetGenericArguments()));
    }

    [Fact]
    public void ABodyCallingAGenericMethodInstantiationIsRefusedBeforeTheWrite() {
        RecordingMinter minter = new RecordingMinter();
        MethodInfo empty = typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(typeof(int));
        ComposedBody body = Shape.Body(
            [Shape.Step(OpCodes.Call, empty), Shape.Step(OpCodes.Pop), Shape.Step(OpCodes.Ret)],
            [],
            [],
            false);

        TokenAccessException refused = Assert.Throws<TokenAccessException>(
            () => BodyEncoder.Encode(body, new WaylithTokenSource(Here, minter), new FixedStack(1)));

        Assert.Contains("System.Array.Empty", refused.Message, StringComparison.Ordinal);
        Assert.Contains("MethodSpec", refused.Message, StringComparison.Ordinal);
        Assert.Contains("BadImageFormatException", refused.Message, StringComparison.Ordinal);
        Assert.Empty(minter.Calls);
    }

    [Fact]
    public void AMethodOnAConstructedGenericTypeStillReachesTheMint() {
        RecordingMinter minter = new RecordingMinter();
        MethodInfo add = typeof(List<int>).GetMethod(nameof(List<int>.Add))!;
        ComposedBody body = Shape.Body(
            [Shape.Step(OpCodes.Call, add), Shape.Step(OpCodes.Ret)],
            [],
            [],
            false);

        BodyEncoder.Encode(body, new WaylithTokenSource(Here, minter), new FixedStack(2));

        Assert.Same(add, Assert.Single(minter.Calls).Operand);
    }

    [Fact]
    public void ASelfRecursiveGenericCallResolvesAsADefinitionAndIsStillRefused() {
        MethodBase source = typeof(BodyEncoderTests).GetMethod(nameof(RecursesWithItsOwnTypeParameter), BindingFlags.NonPublic | BindingFlags.Static)!;
        ComposedBody body = RealBodies.Read(source, out string? skipped)
            ?? throw new InvalidOperationException($"the probe body did not read back: {skipped}");

        MethodInfo called = body.Instructions
            .Select(instruction => instruction.Instruction.operand)
            .OfType<MethodInfo>()
            .Single();

        Assert.True(called.IsGenericMethod);
        Assert.True(called.IsGenericMethodDefinition);

        RecordingMinter minter = new RecordingMinter();

        TokenAccessException refused = Assert.Throws<TokenAccessException>(
            () => new WaylithTokenSource(Here, minter).TokenFor(called));

        Assert.Contains("MethodSpec", refused.Message, StringComparison.Ordinal);
        Assert.Empty(minter.Calls);
    }

    private static int CallsAGenericMethod(int n) {
        return Array.Empty<int>().Length + n;
    }

    private static void RecursesWithItsOwnTypeParameter<T>(int n) {
        if (n > 0) {
            RecursesWithItsOwnTypeParameter<T>(n - 1);
        }
    }

    private static ComposedBody CatchToEnd(int handlerEnd) {
        Label exit = Shape.Mark(1);
        return Shape.Body(
            [
                Shape.Step(OpCodes.Nop),
                Shape.Step(OpCodes.Leave, exit),
                Shape.Step(OpCodes.Ret, null, exit),
                Shape.Step(OpCodes.Pop),
                Shape.Step(OpCodes.Rethrow),
            ],
            [new ComposedClause(ClauseKind.Catch, 0, 2, 3, handlerEnd, typeof(Exception), -1)],
            [],
            false);
    }

    private static void AssertHandlerReachesTheEnd(EncodedBody encoded) {
        Region region = Assert.Single(ReadBack.Body(encoded.Bytes).Regions);

        Assert.Equal(encoded.InstructionOffsets[3], region.HandlerOffset);
        Assert.Equal(encoded.CodeSize, region.HandlerOffset + region.HandlerLength);
    }
}
