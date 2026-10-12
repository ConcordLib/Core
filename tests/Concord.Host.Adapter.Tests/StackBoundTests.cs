using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public class StackBoundTests {
    private static int Nothing() {
        return 0;
    }

    private static void Void() {
    }

    [Fact]
    public void ComputeRejectsANullBody() {
        Assert.Throws<ArgumentNullException>(() => StackBound.Compute(null!));
    }

    [Fact]
    public void ABodyThatPushesNothingNeedsNothing() {
        StackBound bound = StackBound.Compute(Bodies.Of(new CodeInstruction(OpCodes.Ret)));

        Assert.Equal(0, bound.Bound);
    }

    [Fact]
    public void TheBoundIsTheDeepestTheStackGetsNotTheSumOfThePushes() {
        StackBound bound = StackBound.Compute(Bodies.Of(
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Ldc_I4_2),
            new CodeInstruction(OpCodes.Add),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ret)));

        Assert.Equal(2, bound.Bound);
    }

    [Fact]
    public void DupLeavesTwoWhereItFoundOne() {
        StackBound bound = StackBound.Compute(Bodies.Of(
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Dup),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ret)));

        Assert.Equal(2, bound.Bound);
    }

    [Fact]
    public void ABranchWhoseArmsLeaveDifferentDepthsIsMeasuredOnTheDeeperArm() {
        Label join = Shape.Mark(1);
        StackBound bound = StackBound.Compute(Bodies.Of(
            Shape.Step(OpCodes.Ldc_I4_0),
            Shape.Step(OpCodes.Brtrue, join),
            Shape.Step(OpCodes.Ldc_I4_1),
            Shape.Step(OpCodes.Ldc_I4_2),
            Shape.Step(OpCodes.Ldc_I4_3),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Ret, null, join)));

        Assert.Equal(3, bound.Bound);
    }

    [Fact]
    public void AnUnconditionalBranchCarriesNoDepthIntoTheInstructionAfterIt() {
        Label over = Shape.Mark(1);
        StackBound bound = StackBound.Compute(Bodies.Of(
            Shape.Step(OpCodes.Br, over),
            Shape.Step(OpCodes.Ldc_I4_0),
            Shape.Step(OpCodes.Ldc_I4_1),
            Shape.Step(OpCodes.Ret, null, over)));

        Assert.Equal(0, bound.Bound);
    }

    [Fact]
    public void ASwitchCarriesItsDepthToEveryArmAndToTheFallthrough() {
        Label first = Shape.Mark(1);
        Label second = Shape.Mark(2);
        StackBound bound = StackBound.Compute(Bodies.Of(
            Shape.Step(OpCodes.Ldc_I4_0),
            Shape.Step(OpCodes.Ldc_I4_1),
            Shape.Step(OpCodes.Switch, new[] { first, second }),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Ret),
            Shape.Step(OpCodes.Ldc_I4_2, null, first),
            Shape.Step(OpCodes.Ldc_I4_3),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Ret, null, second)));

        Assert.Equal(3, bound.Bound);
    }

    [Fact]
    public void LeavingATryEmptiesTheStackSoTheLandingStartsAtZero() {
        Label landing = Shape.Mark(1);
        CodeInstruction[] stream = [
            Shape.Step(OpCodes.Ldc_I4_0),
            Shape.Step(OpCodes.Ldc_I4_1),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Leave, landing),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Leave, landing),
            Shape.Step(OpCodes.Ldc_I4_2, null, landing),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Ret),
        ];
        ComposedBody body = Shape.Body(stream, [new ComposedClause(ClauseKind.Catch, 0, 4, 4, 6, typeof(Exception), -1)], [], false);

        Assert.Equal(2, StackBound.Compute(body).Bound);
    }

    [Fact]
    public void AFinallyHandlerIsWalkedFromAnEmptyStack() {
        Label landing = Shape.Mark(1);
        CodeInstruction[] stream = [
            Shape.Step(OpCodes.Nop),
            Shape.Step(OpCodes.Leave, landing),
            Shape.Step(OpCodes.Ldc_I4_0),
            Shape.Step(OpCodes.Ldc_I4_1),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Endfinally),
            Shape.Step(OpCodes.Ret, null, landing),
        ];
        ComposedBody body = Shape.Body(stream, [new ComposedClause(ClauseKind.Finally, 0, 2, 2, 7, null, -1)], [], false);

        Assert.Equal(2, StackBound.Compute(body).Bound);
    }

    [Fact]
    public void AnInstructionReachedTwiceAtDifferentDepthsTakesTheDeeperOne() {
        Label join = Shape.Mark(1);
        StackBound bound = StackBound.Compute(Bodies.Of(
            Shape.Step(OpCodes.Ldc_I4_0),
            Shape.Step(OpCodes.Ldc_I4_1),
            Shape.Step(OpCodes.Brtrue, join),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Ldc_I4_2, null, join),
            Shape.Step(OpCodes.Dup),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Pop),
            Shape.Step(OpCodes.Ret)));

        Assert.Equal(3, bound.Bound);
    }

    [Fact]
    public void AVarpushCallResolvesItsPushFromTheOperandSignature() {
        MethodInfo nothing = typeof(StackBoundTests).GetMethod(nameof(Nothing), BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo empty = typeof(StackBoundTests).GetMethod(nameof(Void), BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.Equal(1, StackBound.Compute(Bodies.Of(new CodeInstruction(OpCodes.Call, nothing))).Bound);
        Assert.Equal(0, StackBound.Compute(Bodies.Of(new CodeInstruction(OpCodes.Call, empty))).Bound);
        Assert.Equal(1, StackBound.Compute(Bodies.Of(new CodeInstruction(OpCodes.Callvirt, nothing))).Bound);
        Assert.Equal(0, StackBound.Compute(Bodies.Of(new CodeInstruction(OpCodes.Callvirt, empty))).Bound);
    }

    [Fact]
    public void AConstructorPushesNothingThroughCallAndOneThroughNewobj() {
        ConstructorInfo constructor = typeof(object).GetConstructor(Type.EmptyTypes)!;

        Assert.Equal(0, StackBound.Compute(Bodies.Of(new CodeInstruction(OpCodes.Call, constructor))).Bound);
        Assert.Equal(1, StackBound.Compute(Bodies.Of(new CodeInstruction(OpCodes.Newobj, constructor))).Bound);
    }

    [Fact]
    public void AVarpushWithNoReadableSignatureCountsTheMostACallCanPush() {
        StackBound bound = StackBound.Compute(Bodies.Of(new CodeInstruction(OpCodes.Calli, new object())));

        Assert.Equal(1, bound.Bound);
    }

    [Fact]
    public void ACatchOrFilterHandlerStartsOneDeepAndAFinallyOrFaultHandlerStartsEmpty() {
        CodeInstruction[] stream = [new CodeInstruction(OpCodes.Nop), new CodeInstruction(OpCodes.Ret)];

        Assert.Equal(0, StackBound.Compute(Bodies.Of(stream)).Bound);
        Assert.Equal(1, StackBound.Compute(Bodies.Guarded(stream, ClauseKind.Catch)).Bound);
        Assert.Equal(1, StackBound.Compute(Bodies.Guarded(stream, ClauseKind.Filter)).Bound);
        Assert.Equal(0, StackBound.Compute(Bodies.Guarded(stream, ClauseKind.Finally)).Bound);
        Assert.Equal(0, StackBound.Compute(Bodies.Guarded(stream, ClauseKind.Fault)).Bound);
    }

    [Fact]
    public void FourHandlersSharingOneEntryStillOnlyNeedTheOneExceptionSlot() {
        CodeInstruction[] stream = [new CodeInstruction(OpCodes.Nop), new CodeInstruction(OpCodes.Ret)];
        ComposedBody body = new ComposedBody(
            Bodies.Wrap(stream),
            [],
            [
                new ComposedClause(ClauseKind.Catch, 0, 1, 1, 2, typeof(Exception), -1),
                new ComposedClause(ClauseKind.Catch, 0, 1, 1, 2, typeof(InvalidOperationException), -1),
                new ComposedClause(ClauseKind.Filter, 0, 1, 1, 2, null, 1),
                new ComposedClause(ClauseKind.Finally, 0, 1, 1, 2, null, -1),
            ],
            false);

        Assert.Equal(1, StackBound.Compute(body).Bound);
    }

    [Fact]
    public void AHandlerRunningToTheLiteralEndOfTheBodyIsStillCounted() {
        CodeInstruction[] stream = [new CodeInstruction(OpCodes.Nop), new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Pop)];
        ComposedBody body = new ComposedBody(
            Bodies.Wrap(stream),
            [],
            [new ComposedClause(ClauseKind.Catch, 0, 1, 1, stream.Length, typeof(Exception), -1)],
            false);

        Assert.Equal(2, StackBound.Compute(body).Bound);
    }

    [Fact]
    public void LocallocIsReportedOnlyWhenTheBodyUsesIt() {
        Assert.False(StackBound.Compute(Bodies.Of(new CodeInstruction(OpCodes.Ldc_I4_8))).UsesLocalloc);
        Assert.True(StackBound.Compute(Bodies.Of(
            new CodeInstruction(OpCodes.Ldc_I4_8),
            new CodeInstruction(OpCodes.Localloc),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ret))).UsesLocalloc);
    }

    [Fact]
    public void EveryOpcodeTheRuntimeDefinesHasBothAPushAndAPopCount() {
        FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.True(fields.Length > 200);

        foreach (FieldInfo field in fields) {
            OpCode opcode = (OpCode)field.GetValue(null)!;
            StackBound bound = StackBound.Compute(Bodies.Of(new CodeInstruction(opcode)));

            Assert.InRange(bound.Bound, 0, 2);
        }
    }

    [Fact]
    public void ABoundOverTheHeaderCeilingRefusesInsteadOfTruncating() {
        CodeInstruction[] stream = new CodeInstruction[ushort.MaxValue + 1];
        for (int index = 0; index < stream.Length; index++) {
            stream[index] = new CodeInstruction(OpCodes.Ldc_I4_0);
        }

        StackBound bound = StackBound.Compute(Bodies.Of(stream));

        Assert.Equal(ushort.MaxValue + 1, bound.Bound);
        Assert.False(bound.FitsHeader);
        Assert.Throws<InvalidOperationException>(() => bound.MaxStack);
    }

    [Fact]
    public void TheHeaderCeilingIsInclusiveOfTheLargestUshort() {
        CodeInstruction[] stream = new CodeInstruction[ushort.MaxValue];
        for (int index = 0; index < stream.Length; index++) {
            stream[index] = new CodeInstruction(OpCodes.Ldc_I4_0);
        }

        StackBound bound = StackBound.Compute(Bodies.Of(stream));

        Assert.True(bound.FitsHeader);
        Assert.Equal(ushort.MaxValue, bound.MaxStack);
    }

    [Fact]
    public void TheTinyHeaderGateIsEightOrLess() {
        Assert.True(StackBound.Compute(Bodies.Pushes(8)).FitsTinyHeader);
        Assert.False(StackBound.Compute(Bodies.Pushes(9)).FitsTinyHeader);
    }
}
