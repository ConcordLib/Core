using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Xunit;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Host.Adapter.ForkTests;

public static class Guarded {
    public static int Handled(int seed) {
        return seed + 1;
    }

    public static int Swapped(int seed) {
        return seed + 1;
    }

    public static int Protect(int seed) {
        return seed + 1;
    }

    public static int Mapped(int seed) {
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Live(int seed) {
        return seed + 1;
    }
}

public static class Contributors {
    public static void Guard() {
    }

    public static void Handler() {
    }

    public static void Rival() {
    }
}

public sealed class CatchAttributionTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so this project's facts all stand down together.";

    private const int Handler = 4;

    private const int InTry = 2;

    private static readonly MethodInfo GuardOf = typeof(Contributors).GetMethod(nameof(Contributors.Guard))!;

    private static readonly MethodInfo HandlerOf = typeof(Contributors).GetMethod(nameof(Contributors.Handler))!;

    private static readonly MethodInfo RivalOf = typeof(Contributors).GetMethod(nameof(Contributors.Rival))!;

    private readonly List<MethodBase> written = [];

    private readonly List<MethodBase> registered = [];

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        foreach (MethodBase target in written) {
            ForkRuntime.Restore(target);
        }

        foreach (MethodBase target in registered) {
            FrameAttribution.Forget(target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AnOffsetInsideAnAddedCatchHandlerNamesTheOwnerThatContributedTheHandler() {
        MethodInfo target = typeof(Guarded).GetMethod(nameof(Guarded.Handled))!;
        EncodedBody encoded = Register(target, HandlerOf, "handler");
        int band = WaylithDetourBackend.SentinelBand(target, encoded);

        Assert.Equal("handler", FrameAttribution.OwnerAt(target, band + encoded.InstructionOffsets[Handler]));
        Assert.Equal("handler", FrameAttribution.OwnerAt(target, band + encoded.InstructionOffsets[Handler + 1]));
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void TheSameHandlerOffsetNamesTheOtherOwnerWhenTheOtherOwnerContributedIt() {
        MethodInfo handled = typeof(Guarded).GetMethod(nameof(Guarded.Handled))!;
        MethodInfo swapped = typeof(Guarded).GetMethod(nameof(Guarded.Swapped))!;
        EncodedBody mine = Register(handled, HandlerOf, "handler");
        EncodedBody theirs = Register(swapped, RivalOf, "rival");
        int band = WaylithDetourBackend.SentinelBand(handled, mine);

        Assert.Equal(mine.InstructionOffsets[Handler], theirs.InstructionOffsets[Handler]);
        Assert.Equal(band, WaylithDetourBackend.SentinelBand(swapped, theirs));
        Assert.Equal("handler", FrameAttribution.OwnerAt(handled, band + mine.InstructionOffsets[Handler]));
        Assert.Equal("rival", FrameAttribution.OwnerAt(swapped, band + theirs.InstructionOffsets[Handler]));
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AddingAHandlerLeavesTheProtectedTryAttributingByOriginAlone() {
        MethodInfo target = typeof(Guarded).GetMethod(nameof(Guarded.Protect))!;
        EncodedBody encoded = Register(target, HandlerOf, "handler");
        int band = WaylithDetourBackend.SentinelBand(target, encoded);

        Assert.Null(FrameAttribution.OwnerAt(target, 0));
        Assert.Equal("guard", FrameAttribution.OwnerAt(target, band + encoded.InstructionOffsets[InTry]));
        Assert.Equal("guard", FrameAttribution.OwnerAt(target, band + encoded.InstructionOffsets[InTry + 1]));
        Assert.Equal("handler", FrameAttribution.OwnerAt(target, band + encoded.InstructionOffsets[Handler]));
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void EveryHandlerInstructionGetsItsOwnPairRatherThanCollapsingIntoOne() {
        MethodInfo target = typeof(Guarded).GetMethod(nameof(Guarded.Mapped))!;
        ComposedBody body = CatchBody(HandlerOf);
        EncodedBody encoded = Encode(body);
        int band = WaylithDetourBackend.SentinelBand(target, encoded);
        IReadOnlyList<OffsetPair> pairs = WaylithDetourBackend.OffsetMap(target, body, encoded, Ordered(HandlerOf, "handler"));

        Assert.Equal(body.Instructions.Count, pairs.Count);
        Assert.Equal(body.Instructions.Count, pairs.Select(pair => pair.Original).Distinct().Count());
        Assert.Equal(
            [
                "0->0",
                $"{band + 1}->1",
                $"{band + 2}->2",
                $"{band + 7}->7",
                $"{band + 8}->8",
                $"{band + 9}->9",
                $"{band + 14}->14",
                "1->15",
                "2->16",
            ],
            pairs.Select(pair => $"{pair.Original}->{pair.Replaced}"));
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AThrowRaisedInsideAnAddedCatchHandlerReportsAnOffsetThatNamesTheHandlersOwner() {
        MethodInfo target = typeof(Guarded).GetMethod(nameof(Guarded.Live))!;
        ComposedBody body = CatchBody(HandlerOf);
        EncodedBody encoded = BodyEncoder.Encode(body, new WaylithTokenSource(target.Module), new ComputedStack());
        int band = WaylithDetourBackend.SentinelBand(target, encoded);
        FrameAttribution.Register(target, body, encoded, Ordered(HandlerOf, "handler"));
        registered.Add(target);

        ForkBodyWriter.Shared.Write(target, encoded.Bytes, WaylithDetourBackend.OffsetMap(target, body, encoded, Ordered(HandlerOf, "handler")));
        written.Add(target);

        NotSupportedException raised = Assert.Throws<NotSupportedException>(() => Guarded.Live(1));
        StackFrame frame = Frame(raised, target);

        Assert.Equal(band + encoded.InstructionOffsets[Handler + 2], frame.GetILOffset());
        Assert.Equal("handler", FrameAttribution.OwnerOfFrame(frame));
    }

    private static StackFrame Frame(Exception raised, MethodBase target) {
        StackTrace trace = new StackTrace(raised, false);
        for (int i = 0; i < trace.FrameCount; i++) {
            StackFrame frame = trace.GetFrame(i)!;
            if (frame.GetMethod() == target) {
                return frame;
            }
        }

        throw new InvalidOperationException($"'{target.Name}' raised but is not a frame of its own stack trace, so no reported offset can be read off it.");
    }

    private static ComposedBody CatchBody(MethodBase handler) {
        ConstructorInfo thrownInTry = typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!;
        ConstructorInfo thrownInHandler = typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!;

        return new ComposedBody(
            [
                Copied(OpCodes.Ldarg_0, 0),
                Injected(new CodeInstruction(OpCodes.Pop), GuardOf),
                Injected(new CodeInstruction(OpCodes.Newobj, thrownInTry), GuardOf),
                Injected(new CodeInstruction(OpCodes.Throw), GuardOf),
                Injected(new CodeInstruction(OpCodes.Pop), handler),
                Injected(new CodeInstruction(OpCodes.Newobj, thrownInHandler), handler),
                Injected(new CodeInstruction(OpCodes.Throw), handler),
                Copied(OpCodes.Ldc_I4_0, 1),
                Copied(OpCodes.Ret, 2),
            ],
            [],
            [new ComposedClause(ClauseKind.Catch, 0, 4, 4, 7, typeof(InvalidOperationException), -1)],
            false);
    }

    private static EncodedBody Encode(ComposedBody body) {
        return BodyEncoder.Encode(body, new Minted(), new ComputedStack());
    }

    private static Injection[] Ordered(MethodBase handler, string owner) {
        return [new Injection(GuardOf, new InjectAt.Head(), "guard", 0), new Injection(handler, new InjectAt.Tail(), owner, 0)];
    }

    private static ComposedInstruction Copied(OpCode opcode, int origin) {
        return new ComposedInstruction(new CodeInstruction(opcode), origin, null);
    }

    private static ComposedInstruction Injected(CodeInstruction instruction, MethodBase injection) {
        return new ComposedInstruction(instruction, null, injection);
    }

    private EncodedBody Register(MethodInfo target, MethodBase handler, string owner) {
        ComposedBody body = CatchBody(handler);
        EncodedBody encoded = Encode(body);
        FrameAttribution.Register(target, body, encoded, Ordered(handler, owner));
        registered.Add(target);
        return encoded;
    }

    private sealed class Minted : ITokenSource {
        private readonly Dictionary<object, int> assigned = [];

        public int TokenFor(Type type) => Assign(type, 0x01000000);

        public int TokenFor(FieldInfo field) => Assign(field, 0x04000000);

        public int TokenFor(MethodBase method) => Assign(method, 0x0a000000);

        public int TokenFor(string literal) => Assign(literal, 0x70000000);

        public int TokenForLocals(byte[] signature) => 0x11000001;

        private int Assign(object key, int table) {
            if (!assigned.TryGetValue(key, out int token)) {
                token = table | (assigned.Count + 1);
                assigned.Add(key, token);
            }

            return token;
        }
    }
}
