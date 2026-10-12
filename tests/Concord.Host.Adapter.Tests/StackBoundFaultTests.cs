using System.Reflection.Emit;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public class StackBoundFaultTests {
    private static ComposedBody TightCatchBody() {
        Concord.Label end = default;
        CodeInstruction[] stream = [
            new CodeInstruction(OpCodes.Nop),
            new CodeInstruction(OpCodes.Leave, end),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Ldc_I4_2),
            new CodeInstruction(OpCodes.Ldc_I4_3),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Leave, end),
            new CodeInstruction(OpCodes.Ret).WithLabels(end),
        ];

        return new ComposedBody(
            Bodies.Wrap(stream),
            [],
            [new ComposedClause(ClauseKind.Catch, 0, 2, 2, 10, typeof(Exception), -1)],
            false);
    }

    private static ComposedBody ThrowingCatchBody() {
        Concord.Label end = default;
        CodeInstruction[] stream = [
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Stloc_0),
            new CodeInstruction(OpCodes.Ldnull),
            new CodeInstruction(OpCodes.Throw),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ldc_I4, 99),
            new CodeInstruction(OpCodes.Stloc_0),
            new CodeInstruction(OpCodes.Leave, end),
            new CodeInstruction(OpCodes.Ldloc_0).WithLabels(end),
            new CodeInstruction(OpCodes.Ret),
        ];

        return new ComposedBody(
            Bodies.Wrap(stream),
            [new ComposedLocal(typeof(int), false)],
            [new ComposedClause(ClauseKind.Catch, 0, 4, 4, 8, typeof(Exception), -1)],
            true);
    }

    private static ComposedBody PushFreeFinallyBody() {
        Concord.Label end = default;
        CodeInstruction[] stream = [
            new CodeInstruction(OpCodes.Nop),
            new CodeInstruction(OpCodes.Leave, end),
            new CodeInstruction(OpCodes.Endfinally),
            new CodeInstruction(OpCodes.Ret).WithLabels(end),
        ];

        return new ComposedBody(
            Bodies.Wrap(stream),
            [],
            [new ComposedClause(ClauseKind.Finally, 0, 2, 2, 3, null, -1)],
            false);
    }

    private static ComposedBody DrainedPushes(int count) {
        CodeInstruction[] stream = new CodeInstruction[(count * 2) + 1];
        for (int index = 0; index < count; index++) {
            stream[index] = new CodeInstruction(OpCodes.Ldc_I4_0);
            stream[count + index] = new CodeInstruction(OpCodes.Pop);
        }

        stream[count * 2] = new CodeInstruction(OpCodes.Ret);
        return Bodies.Of(stream);
    }

    [Fact]
    public void TheBoundIsTightOnAHandlerWhoseDepthIsAllLive() {
        Assert.Equal(4, StackBound.Compute(TightCatchBody()).Bound);
    }

    [Fact]
    public void OneSlotUnderTheComputedBoundFaultsAtTheFirstCall() {
        ComposedBody body = TightCatchBody();
        StackBound bound = StackBound.Compute(body);

        EmittedMethod starved = TestIl.Emit(body, bound.MaxStack - 1, bound.UsesLocalloc, false);

        Assert.Equal(bound.Bound - 1, starved.HeaderMaxStack);
        Assert.Throws<InvalidProgramException>(() => starved.Invoke());
    }

    [Fact]
    public void TheComputedBoundRunsAndLandsInTheMethodHeader() {
        ComposedBody body = TightCatchBody();
        StackBound bound = StackBound.Compute(body);

        EmittedMethod emitted = TestIl.Emit(body, bound.MaxStack, bound.UsesLocalloc, false);

        Assert.True(emitted.HeaderIsFat);
        Assert.Equal(bound.Bound, emitted.HeaderMaxStack);
        Assert.Null(emitted.Invoke());
    }

    [Fact]
    public void AHandlerThatActuallyRunsProducesItsValueAtTheComputedBound() {
        ComposedBody body = ThrowingCatchBody();
        StackBound bound = StackBound.Compute(body);

        Assert.Equal(1, bound.Bound);

        EmittedMethod emitted = TestIl.Emit(body, bound.MaxStack, bound.UsesLocalloc, true);

        Assert.Equal(bound.Bound, emitted.HeaderMaxStack);
        Assert.Equal(99, emitted.Invoke());
    }

    [Fact]
    public void APushFreeBodyWithAFinallyRunsAtAMaxstackOfZero() {
        ComposedBody body = PushFreeFinallyBody();
        StackBound bound = StackBound.Compute(body);

        Assert.Equal(0, bound.Bound);

        EmittedMethod emitted = TestIl.Emit(body, bound.MaxStack, bound.UsesLocalloc, false);

        Assert.True(emitted.HeaderIsFat);
        Assert.Equal(0, emitted.HeaderMaxStack);
        Assert.Null(emitted.Invoke());
    }

    [Fact]
    public void ABoundOfEightStaysInTheTinyHeaderAndNineDoesNot() {
        StackBound eight = StackBound.Compute(DrainedPushes(8));
        StackBound nine = StackBound.Compute(DrainedPushes(9));

        Assert.Equal(8, eight.Bound);
        Assert.Equal(9, nine.Bound);

        EmittedMethod tiny = TestIl.Emit(DrainedPushes(8), eight.MaxStack, false, false);
        EmittedMethod fat = TestIl.Emit(DrainedPushes(9), nine.MaxStack, false, false);

        Assert.False(tiny.HeaderIsFat);
        Assert.Equal(8, tiny.HeaderMaxStack);
        Assert.Null(tiny.Invoke());

        Assert.True(fat.HeaderIsFat);
        Assert.Equal(9, fat.HeaderMaxStack);
        Assert.Null(fat.Invoke());
    }

    private static ComposedBody LocallocBody(bool initLocals) {
        CodeInstruction[] stream = [
            new CodeInstruction(OpCodes.Ldc_I4_8),
            new CodeInstruction(OpCodes.Localloc),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ret),
        ];

        return new ComposedBody(Bodies.Wrap(stream), [], [], initLocals);
    }

    [Fact]
    public void ALocallocBodyIsReportedAndRunsAtTheComputedBound() {
        StackBound bound = StackBound.Compute(LocallocBody(true));

        Assert.True(bound.UsesLocalloc);
        Assert.Equal(1, bound.Bound);

        EmittedMethod zeroed = TestIl.Emit(LocallocBody(true), bound.MaxStack, bound.UsesLocalloc, false);

        Assert.True(zeroed.HeaderIsFat);
        Assert.Equal(1, zeroed.HeaderMaxStack);
        Assert.Null(zeroed.Invoke());
    }

    [Fact]
    public void TheDynamicAllocationFlagOnlyForcesAFatHeaderWhenLocalsAreZeroed() {
        StackBound bound = StackBound.Compute(LocallocBody(false));

        EmittedMethod uninitialized = TestIl.Emit(LocallocBody(false), bound.MaxStack, bound.UsesLocalloc, false);

        Assert.True(bound.UsesLocalloc);
        Assert.False(uninitialized.HeaderIsFat);
        Assert.Equal(8, uninitialized.HeaderMaxStack);
        Assert.Null(uninitialized.Invoke());
    }
}
