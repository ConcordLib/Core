using System.Reflection;
using System.Reflection.Emit;
using Xunit;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Host.Adapter.Tests;

internal static class RoundTrip {
    internal const int TinyHeaderMaxStack = 8;

    private static readonly Dictionary<short, OpCode> LongForm = new Dictionary<short, OpCode> {
        [OpCodes.Br_S.Value] = OpCodes.Br,
        [OpCodes.Brfalse_S.Value] = OpCodes.Brfalse,
        [OpCodes.Brtrue_S.Value] = OpCodes.Brtrue,
        [OpCodes.Beq_S.Value] = OpCodes.Beq,
        [OpCodes.Bge_S.Value] = OpCodes.Bge,
        [OpCodes.Bgt_S.Value] = OpCodes.Bgt,
        [OpCodes.Ble_S.Value] = OpCodes.Ble,
        [OpCodes.Blt_S.Value] = OpCodes.Blt,
        [OpCodes.Bne_Un_S.Value] = OpCodes.Bne_Un,
        [OpCodes.Bge_Un_S.Value] = OpCodes.Bge_Un,
        [OpCodes.Bgt_Un_S.Value] = OpCodes.Bgt_Un,
        [OpCodes.Ble_Un_S.Value] = OpCodes.Ble_Un,
        [OpCodes.Blt_Un_S.Value] = OpCodes.Blt_Un,
        [OpCodes.Leave_S.Value] = OpCodes.Leave,
    };

    private static readonly Dictionary<ClauseKind, ExceptionRegionKind> RegionKinds = new Dictionary<ClauseKind, ExceptionRegionKind> {
        [ClauseKind.Catch] = ExceptionRegionKind.Catch,
        [ClauseKind.Filter] = ExceptionRegionKind.Filter,
        [ClauseKind.Finally] = ExceptionRegionKind.Finally,
        [ClauseKind.Fault] = ExceptionRegionKind.Fault,
    };

    internal static Decoded Verify(ComposedBody body, EncodedBody encoded, Tokens tokens) {
        Decoded decoded = ReadBack.Body(encoded.Bytes);
        Assert.Equal(encoded.CodeSize, decoded.CodeSize);
        Assert.Equal(body.Locals.Count == 0, decoded.LocalSignatureIsNil);
        if (body.Locals.Count > 0) {
            Assert.Equal(Tokens.LocalsToken, decoded.LocalSignatureToken);
            Assert.Equal(body.Locals.Count, ReadBack.LocalCount(tokens.Locals!));
        }
        else {
            Assert.Null(tokens.Locals);
        }

        if ((encoded.Bytes[0] & 3) == 3) {
            Assert.Equal(encoded.MaxStack, decoded.MaxStack);
            Assert.Equal(body.InitLocals, decoded.InitLocals);
        }
        else {
            Assert.Equal(TinyHeaderMaxStack, decoded.MaxStack);
        }

        VerifyStream(body, encoded, decoded, tokens);
        VerifyRegions(body, encoded, decoded, tokens);
        AssertRegionsAreInnermostFirst(decoded.Regions);
        VerifyOriginMap(body, encoded);
        return decoded;
    }

    internal static void AssertRegionsAreInnermostFirst(IReadOnlyList<Region> regions) {
        for (int outer = 0; outer < regions.Count; outer++) {
            for (int inner = outer + 1; inner < regions.Count; inner++) {
                Assert.False(
                    Encloses(regions[outer], regions[inner]),
                    $"region {outer} protects [{regions[outer].TryOffset}, {regions[outer].TryOffset + regions[outer].TryLength}) and strictly encloses region {inner}, which protects [{regions[inner].TryOffset}, {regions[inner].TryOffset + regions[inner].TryLength}), so the inner one is emitted second");
            }
        }
    }

    internal static int Boundary(EncodedBody encoded, int index) {
        return index < encoded.InstructionOffsets.Count ? encoded.InstructionOffsets[index] : encoded.CodeSize;
    }

    internal static bool HasNestedClauses(IReadOnlyList<ComposedClause> clauses) {
        for (int outer = 0; outer < clauses.Count; outer++) {
            for (int inner = 0; inner < clauses.Count; inner++) {
                if (outer != inner && Nests(clauses[outer], clauses[inner])) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Nests(ComposedClause outer, ComposedClause inner) {
        return inner.TryStart >= outer.TryStart
            && inner.TryEnd <= outer.TryEnd
            && (inner.TryStart > outer.TryStart || inner.TryEnd < outer.TryEnd);
    }

    private static bool Encloses(Region outer, Region inner) {
        int outerEnd = outer.TryOffset + outer.TryLength;
        int innerEnd = inner.TryOffset + inner.TryLength;
        return inner.TryOffset >= outer.TryOffset
            && innerEnd <= outerEnd
            && (inner.TryOffset > outer.TryOffset || innerEnd < outerEnd);
    }

    private static void VerifyStream(ComposedBody body, EncodedBody encoded, Decoded decoded, ITokenSource tokens) {
        List<Step> steps = ReadBack.Steps(decoded.Il);
        Assert.Equal(body.Instructions.Count, steps.Count);
        Assert.Equal(encoded.InstructionOffsets, steps.Select(step => step.Offset).ToArray());

        Dictionary<Label, int> targets = [];
        for (int index = 0; index < body.Instructions.Count; index++) {
            foreach (Label label in body.Instructions[index].Instruction.labels) {
                targets[label] = index;
            }
        }

        for (int index = 0; index < steps.Count; index++) {
            CodeInstruction instruction = body.Instructions[index].Instruction;
            OpCode expected = LongForm.TryGetValue(instruction.opcode.Value, out OpCode widened) ? widened : instruction.opcode;
            Assert.Equal(expected.Value, steps[index].OpCode.Value);
            if (instruction.operand is Label branch) {
                Assert.Equal(5, steps[index].Size);
                Assert.Equal(encoded.InstructionOffsets[targets[branch]], steps[index].Offset + steps[index].Size + steps[index].Operand);
                continue;
            }

            VerifyOperand(instruction, steps[index], tokens);
        }
    }

    private static void VerifyOperand(CodeInstruction instruction, Step step, ITokenSource tokens) {
        switch (instruction.opcode.OperandType) {
            case OperandType.InlineString:
            case OperandType.InlineType:
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineTok:
                Assert.Equal(Token(instruction.operand, tokens), (int)step.Operand);
                return;
            case OperandType.InlineI:
                Assert.Equal(unchecked((int)Convert.ToInt64(instruction.operand)), (int)step.Operand);
                return;
            case OperandType.InlineI8:
                Assert.Equal(Convert.ToInt64(instruction.operand), step.Operand);
                return;
            case OperandType.ShortInlineI:
                if (instruction.opcode == OpCodes.Ldc_I4_S) {
                    Assert.Equal(Convert.ToSByte(instruction.operand), (sbyte)step.Operand);
                }
                else {
                    Assert.Equal(Convert.ToByte(instruction.operand), (byte)step.Operand);
                }

                return;
            case OperandType.ShortInlineVar:
                Assert.Equal(Slot(instruction.operand), (byte)step.Operand);
                return;
            case OperandType.InlineVar:
                Assert.Equal(Slot(instruction.operand), (ushort)step.Operand);
                return;
            case OperandType.ShortInlineR:
                Assert.Equal(BitConverter.SingleToInt32Bits(Convert.ToSingle(instruction.operand)), (int)step.Operand);
                return;
            case OperandType.InlineR:
                Assert.Equal(BitConverter.DoubleToInt64Bits(Convert.ToDouble(instruction.operand)), step.Operand);
                return;
            default:
                return;
        }
    }

    private static int Token(object? operand, ITokenSource tokens) {
        return operand switch {
            Type type => tokens.TokenFor(type),
            FieldInfo field => tokens.TokenFor(field),
            MethodBase method => tokens.TokenFor(method),
            string literal => tokens.TokenFor(literal),
            _ => throw new NotSupportedException($"a token operand cannot be a {operand?.GetType().ToString() ?? "null"}"),
        };
    }

    private static int Slot(object? operand) {
        return operand switch {
            LocalRef local => local.Index,
            _ => Convert.ToInt32(operand),
        };
    }

    private static void VerifyRegions(ComposedBody body, EncodedBody encoded, Decoded decoded, Tokens tokens) {
        int[] order = BodyEncoder.InnermostFirst(body.Clauses);
        Assert.Equal(order.Length, decoded.Regions.Length);
        for (int slot = 0; slot < order.Length; slot++) {
            ComposedClause clause = body.Clauses[order[slot]];
            Region region = decoded.Regions[slot];
            Assert.Equal(RegionKinds[clause.Kind], region.Kind);
            Assert.Equal(Boundary(encoded, clause.TryStart), region.TryOffset);
            Assert.Equal(Boundary(encoded, clause.TryEnd) - Boundary(encoded, clause.TryStart), region.TryLength);
            Assert.Equal(Boundary(encoded, clause.HandlerStart), region.HandlerOffset);
            Assert.Equal(Boundary(encoded, clause.HandlerEnd) - Boundary(encoded, clause.HandlerStart), region.HandlerLength);
            if (clause.Kind == ClauseKind.Filter) {
                Assert.Equal(Boundary(encoded, clause.FilterStart), region.FilterOffset);
            }

            if (clause.Kind == ClauseKind.Catch) {
                Assert.Equal(tokens.TokenFor(clause.CatchType!), region.CatchToken);
            }
        }
    }

    private static void VerifyOriginMap(ComposedBody body, EncodedBody encoded) {
        if (body.Instructions.Any(instruction => instruction.OriginOffset is null)) {
            return;
        }

        int previousOrigin = -1;
        int previousEncoded = -1;
        for (int index = 0; index < body.Instructions.Count; index++) {
            int origin = body.Instructions[index].OriginOffset!.Value;
            Assert.True(origin > previousOrigin, $"instruction {index} has origin offset {origin}, not past {previousOrigin}");
            Assert.True(encoded.InstructionOffsets[index] > previousEncoded, $"instruction {index} lands at {encoded.InstructionOffsets[index]}, not past {previousEncoded}");
            previousOrigin = origin;
            previousEncoded = encoded.InstructionOffsets[index];
        }
    }
}

public class RoundTripTests {
    [Fact]
    public void AFinallyRegionRoundTripsThroughItsOwnKind() {
        ITranspilerContext context = TranspilerContextFactory.For(typeof(RoundTripTests).GetMethod(nameof(AFinallyRegionRoundTripsThroughItsOwnKind))!);
        Label exit = context.DefineLabel();
        ComposedBody body = Shape.Body(
            [
                Shape.Step(OpCodes.Nop),
                Shape.Step(OpCodes.Leave, exit),
                Shape.Step(OpCodes.Nop),
                Shape.Step(OpCodes.Endfinally),
                Shape.Step(OpCodes.Ret, null, exit),
            ],
            [new ComposedClause(ClauseKind.Finally, 0, 2, 2, 4, null, -1)],
            [],
            false);

        Tokens tokens = new Tokens();
        EncodedBody encoded = BodyEncoder.Encode(body, tokens, new FixedStack(1));
        Decoded decoded = RoundTrip.Verify(body, encoded, tokens);

        Assert.Equal(ExceptionRegionKind.Finally, Assert.Single(decoded.Regions).Kind);
        Assert.Equal(0, decoded.Regions[0].CatchToken);
    }

    [Fact]
    public void AFaultRegionRoundTripsThroughItsOwnKind() {
        ITranspilerContext context = TranspilerContextFactory.For(typeof(RoundTripTests).GetMethod(nameof(AFaultRegionRoundTripsThroughItsOwnKind))!);
        Label exit = context.DefineLabel();
        ComposedBody body = Shape.Body(
            [
                Shape.Step(OpCodes.Nop),
                Shape.Step(OpCodes.Leave, exit),
                Shape.Step(OpCodes.Nop),
                Shape.Step(OpCodes.Endfinally),
                Shape.Step(OpCodes.Ret, null, exit),
            ],
            [new ComposedClause(ClauseKind.Fault, 0, 2, 2, 4, null, -1)],
            [],
            false);

        Tokens tokens = new Tokens();
        Decoded decoded = RoundTrip.Verify(body, BodyEncoder.Encode(body, tokens, new FixedStack(1)), tokens);

        Assert.Equal(ExceptionRegionKind.Fault, Assert.Single(decoded.Regions).Kind);
    }

    [Fact]
    public void ATwoWayBranchWithDistinctLabelsRoundTripsBothArms() {
        ITranspilerContext context = TranspilerContextFactory.For(typeof(RoundTripTests).GetMethod(nameof(ATwoWayBranchWithDistinctLabelsRoundTripsBothArms))!);
        Label taken = context.DefineLabel();
        Label after = context.DefineLabel();

        Assert.NotEqual(taken, after);

        ComposedBody body = Shape.Body(
            [
                Shape.Step(OpCodes.Ldc_I4_0),
                Shape.Step(OpCodes.Brtrue_S, taken),
                Shape.Step(OpCodes.Br_S, after),
                Shape.Step(OpCodes.Nop, null, taken),
                Shape.Step(OpCodes.Ret, null, after),
            ],
            [],
            [],
            false);

        Tokens tokens = new Tokens();
        EncodedBody encoded = BodyEncoder.Encode(body, tokens, new FixedStack(1));
        List<Step> steps = ReadBack.Steps(RoundTrip.Verify(body, encoded, tokens).Il);

        Assert.Equal(OpCodes.Brtrue, steps[1].OpCode);
        Assert.Equal(OpCodes.Br, steps[2].OpCode);
        Assert.Equal(encoded.InstructionOffsets[3], steps[1].Offset + steps[1].Size + steps[1].Operand);
        Assert.Equal(encoded.InstructionOffsets[4], steps[2].Offset + steps[2].Size + steps[2].Operand);
    }

    [Fact]
    public void TheInnermostFirstCheckCatchesAnOutermostFirstRegionList() {
        Region outer = new Region(ExceptionRegionKind.Finally, 0, 40, 40, 6, -1, 0);
        Region inner = new Region(ExceptionRegionKind.Finally, 4, 12, 16, 6, -1, 0);

        RoundTrip.AssertRegionsAreInnermostFirst([inner, outer]);

        Assert.Throws<Xunit.Sdk.FalseException>(() => RoundTrip.AssertRegionsAreInnermostFirst([outer, inner]));
    }

    [Fact]
    public void TheInnermostFirstCheckAllowsSiblingsAndSharedTryRanges() {
        Region first = new Region(ExceptionRegionKind.Catch, 0, 10, 10, 4, -1, 0x01000001);
        Region second = new Region(ExceptionRegionKind.Catch, 0, 10, 14, 4, -1, 0x01000002);
        Region sibling = new Region(ExceptionRegionKind.Finally, 20, 10, 30, 4, -1, 0);

        RoundTrip.AssertRegionsAreInnermostFirst([first, second, sibling]);
    }

    [Theory]
    [MemberData(nameof(RealBodies.Corpora), MemberType = typeof(RealBodies))]
    public void EveryReadableBodyRoundTripsThroughTheDecoder(string corpus) {
        AssertClean(Corpus.Run(RealBodies.Of(corpus), (_, body, encoded, tokens) => RoundTrip.Verify(body, encoded, tokens)), 80_000);
    }

    [GameCorpusFact]
    public void EveryReadableGameBodyRoundTripsThroughTheDecoder() {
        AssertClean(Corpus.Run(RealBodies.Of(RealBodies.GameCorpus), (_, body, encoded, tokens) => RoundTrip.Verify(body, encoded, tokens)), 80_000);
    }

    [Fact]
    public void RealNestedBodiesComeOutInnermostFirstEvenWhenTheirClausesArriveReversed() {
        int nested = 0;
        CorpusResult result = Corpus.Run(RealBodies.Runtime, (_, body, _, _) => {
            if (!RoundTrip.HasNestedClauses(body.Clauses)) {
                return;
            }

            nested++;
            ComposedBody reversed = new ComposedBody(body.Instructions, body.Locals, [.. body.Clauses.Reverse()], body.InitLocals);
            Tokens tokens = new Tokens();
            RoundTrip.Verify(reversed, BodyEncoder.Encode(reversed, tokens, new ComputedStack()), tokens);
        });

        AssertClean(result, 80_000);
        Assert.True(nested >= 250, $"only {nested} real bodies carry strictly nested clauses, too few to exercise the ordering");
    }

    internal static void AssertClean(CorpusResult result, int floor) {
        Assert.True(result.Encoded >= floor, $"only {result.Encoded} bodies reached the encoder, under the floor of {floor}. {result.Summary}");
        Assert.True(result.Failures.Count == 0, result.Summary);
        foreach (string bucket in result.Refusals.Keys) {
            Assert.Contains(bucket, Corpus.AllowedRefusals);
        }
        Assert.True(result.Refusals.Values.Sum() * 100 < result.Encoded, $"refused {result.Refusals.Values.Sum()} of {result.Encoded + result.Refusals.Values.Sum()}, over one percent. {result.Summary}");
    }
}
