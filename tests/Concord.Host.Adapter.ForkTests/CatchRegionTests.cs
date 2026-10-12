using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Guard {
    public const string Boom = "catch region probe";

    public static Exception? Caught { get; set; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Throw() {
        throw new InvalidOperationException(Boom);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Record(Exception thrown) {
        Caught = thrown;
        return 7;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Swallowed() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Read() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Rethrown() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Untroubled() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int AlreadyGuarded() {
        try {
            return 1;
        } catch (InvalidOperationException) {
            return 2;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Restored() {
        return 1;
    }
}

public sealed class CatchRegionTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so no catch region can be published.";

    private const int FromTry = 3;

    private const int FromHandler = 7;

    private static readonly MethodInfo Thrower = typeof(Guard).GetMethod(nameof(Guard.Throw))!;

    private static readonly MethodInfo Recorder = typeof(Guard).GetMethod(nameof(Guard.Record))!;

    private readonly List<MethodBase> written = [];

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        foreach (MethodBase target in written) {
            ForkBodyWriter.Shared.Restore(target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AnAddedCatchSwallowsAThrowTheOriginalBodyNeverMade() {
        MethodInfo target = typeof(Guard).GetMethod(nameof(Guard.Swallowed))!;

        Write(target, Swallowing(target));

        Assert.Equal(FromHandler, Guard.Swallowed());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void TheHandlerReceivesTheThrownExceptionWithItsTypeAndMessageIntact() {
        MethodInfo target = typeof(Guard).GetMethod(nameof(Guard.Read))!;
        Label exit = Exit(target);
        Guard.Caught = null;

        Write(
            target,
            Encode(
                target,
                [
                    Step(OpCodes.Call, Thrower),
                    Step(OpCodes.Ldc_I4_3),
                    Step(OpCodes.Stloc_0),
                    Step(OpCodes.Leave, exit),
                    Step(OpCodes.Call, Recorder),
                    Step(OpCodes.Stloc_0),
                    Step(OpCodes.Leave, exit),
                    Step(OpCodes.Ldloc_0, null, exit),
                    Step(OpCodes.Ret),
                ],
                [Caught(0, 4, 4, 7)]));

        int returned = Guard.Read();

        Assert.Equal(FromHandler, returned);
        InvalidOperationException caught = Assert.IsType<InvalidOperationException>(Guard.Caught);
        Assert.Equal(Guard.Boom, caught.Message);
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AHandlerThatRethrowsHandsTheCallerTheOriginalException() {
        MethodInfo target = typeof(Guard).GetMethod(nameof(Guard.Rethrown))!;
        Label exit = Exit(target);

        Write(
            target,
            Encode(
                target,
                [
                    Step(OpCodes.Call, Thrower),
                    Step(OpCodes.Ldc_I4_3),
                    Step(OpCodes.Stloc_0),
                    Step(OpCodes.Leave, exit),
                    Step(OpCodes.Pop),
                    Step(OpCodes.Rethrow),
                    Step(OpCodes.Ldloc_0, null, exit),
                    Step(OpCodes.Ret),
                ],
                [Caught(0, 4, 4, 6)]));

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => Guard.Rethrown());

        Assert.Equal(Guard.Boom, thrown.Message);
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AnUnthrownTryReturnsItsOwnValueAndLeavesTheHandlerUnrun() {
        MethodInfo target = typeof(Guard).GetMethod(nameof(Guard.Untroubled))!;
        Label exit = Exit(target);

        Write(
            target,
            Encode(
                target,
                [
                    Step(OpCodes.Ldc_I4_3),
                    Step(OpCodes.Stloc_0),
                    Step(OpCodes.Leave, exit),
                    Step(OpCodes.Pop),
                    Step(OpCodes.Ldc_I4_7),
                    Step(OpCodes.Stloc_0),
                    Step(OpCodes.Leave, exit),
                    Step(OpCodes.Ldloc_0, null, exit),
                    Step(OpCodes.Ret),
                ],
                [Caught(0, 3, 3, 7)]));

        Assert.Equal(FromTry, Guard.Untroubled());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ACatchRegionLandsOnATargetWhoseOriginalBodyAlreadyCarriedOne() {
        MethodInfo target = typeof(Guard).GetMethod(nameof(Guard.AlreadyGuarded))!;

        Assert.NotEmpty(target.GetMethodBody()!.ExceptionHandlingClauses);
        Assert.Equal(1, Guard.AlreadyGuarded());

        Write(target, Swallowing(target));

        Assert.Equal(FromHandler, Guard.AlreadyGuarded());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void RestoreTakesACatchBearingBodyBackOffTheTarget() {
        MethodInfo target = typeof(Guard).GetMethod(nameof(Guard.Restored))!;

        Write(target, Swallowing(target));
        Assert.Equal(FromHandler, Guard.Restored());

        ForkBodyWriter.Shared.Restore(target);
        written.Remove(target);

        Assert.Equal(1, Guard.Restored());
    }

    private static Label Exit(MethodBase target) {
        return TranspilerContextFactory.For(target).DefineLabel();
    }

    private static CodeInstruction Step(OpCode opcode, object? operand = null, params Label[] labels) {
        return new CodeInstruction(opcode, operand).WithLabels(labels);
    }

    private static ComposedClause Caught(int tryStart, int tryEnd, int handlerStart, int handlerEnd) {
        return new ComposedClause(ClauseKind.Catch, tryStart, tryEnd, handlerStart, handlerEnd, typeof(Exception), -1);
    }

    private static byte[] Swallowing(MethodBase target) {
        Label exit = Exit(target);
        return Encode(
            target,
            [
                Step(OpCodes.Call, Thrower),
                Step(OpCodes.Ldc_I4_3),
                Step(OpCodes.Stloc_0),
                Step(OpCodes.Leave, exit),
                Step(OpCodes.Pop),
                Step(OpCodes.Ldc_I4_7),
                Step(OpCodes.Stloc_0),
                Step(OpCodes.Leave, exit),
                Step(OpCodes.Ldloc_0, null, exit),
                Step(OpCodes.Ret),
            ],
            [Caught(0, 4, 4, 8)]);
    }

    private static byte[] Encode(MethodBase target, CodeInstruction[] stream, ComposedClause[] clauses) {
        ComposedInstruction[] wrapped = new ComposedInstruction[stream.Length];
        for (int index = 0; index < stream.Length; index++) {
            wrapped[index] = new ComposedInstruction(stream[index], null, null);
        }

        ComposedBody body = new ComposedBody(wrapped, [new ComposedLocal(typeof(int), false)], clauses, true);
        return BodyEncoder.Encode(body, new WaylithTokenSource(target.Module), new ComputedStack()).Bytes;
    }

    private void Write(MethodBase target, byte[] body) {
        ForkBodyWriter.Shared.Write(target, body, []);
        written.Add(target);
    }
}
