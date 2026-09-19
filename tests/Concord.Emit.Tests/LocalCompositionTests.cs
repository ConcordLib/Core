using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

// Mod B's honest transpiler: it declares a local of its own and uses it. That is all it takes to make
// mod A's un-ordinalled [Local] ambiguous, through no fault of B's, and that is the only ambiguity
// eviction covers. A selector that would have missed against the bare target body is the author's own
// mistake, so it throws instead.
public static class EvictionTranspilers {
    public static IEnumerable<CodeInstruction> AddScratchInt(
        IEnumerable<CodeInstruction> instructions, ITranspilerContext context) {
        return Scratch(instructions, context, typeof(int), new CodeInstruction(OpCodes.Ldc_I4, 42));
    }

    public static IEnumerable<CodeInstruction> AddScratchFloat(
        IEnumerable<CodeInstruction> instructions, ITranspilerContext context) {
        return Scratch(instructions, context, typeof(float), new CodeInstruction(OpCodes.Ldc_R4, 42f));
    }

    // Mod B edits a store out of the target body. Nothing declares a slot here: the only thing that
    // moved is how many stores the float slot has, which is what makes an author's By stale.
    public static IEnumerable<CodeInstruction> DropTheLastFloatStore(
        IEnumerable<CodeInstruction> instructions, ITranspilerContext context) {
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        for (int i = output.Count - 1; i >= 0; i--) {
            if (IsStoreToSlotZero(output[i])) {
                output[i] = new CodeInstruction(OpCodes.Pop);
                break;
            }
        }

        return output;
    }

    private static bool IsStoreToSlotZero(CodeInstruction instruction) {
        if (instruction.opcode == OpCodes.Stloc_0) {
            return true;
        }

        return (instruction.opcode == OpCodes.Stloc || instruction.opcode == OpCodes.Stloc_S)
            && instruction.operand is LocalRef local && local.Index == 0;
    }

    private static List<CodeInstruction> Scratch(
        IEnumerable<CodeInstruction> instructions, ITranspilerContext context, Type type, CodeInstruction load) {
        LocalRef scratch = context.DeclareLocal(type);
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        output.Insert(0, load);
        output.Insert(1, new CodeInstruction(OpCodes.Stloc_S, scratch));
        output.Insert(2, new CodeInstruction(OpCodes.Ldloc_S, scratch));
        output.Insert(3, new CodeInstruction(OpCodes.Pop));
        return output;
    }
}

public class EvictionHost {
    // One int local in Debug and in Release: ToString() takes its address so Release keeps it in a
    // slot, and the string return type keeps a Debug return temp out of the int count.
    public string OneInt(int seed) {
        int doubled = seed * 2;
        return doubled.ToString();
    }
}

public sealed class LocalCompositionTests {
    [Fact]
    public void ABrokenLocal_EvictsOnlyItsOwnInjection() {
        ComposeResult result = WrapperComposer.Compose(OneIntTarget(), BrokenPlusTranspiler());

        Func<EvictionHost, int, string> run = result.Wrapper.CreateDelegate<Func<EvictionHost, int, string>>();
        LocalRecorder.Observed = false;
        LocalRecorder.SeenInt = 0;
        string returned = run(new EvictionHost(), 5);

        Assert.Equal("10", returned);
        Assert.True(LocalRecorder.Observed);
        Assert.Equal(0, LocalRecorder.SeenInt);
        Assert.Contains(result.Rejected, r => r.Owner == "mod-a" && r.Code == "CONC147");
    }

    // The same selector against the bare target body is an ordinary typo, so it throws rather than
    // leaving the author with an injection that silently never runs.
    [Fact]
    public void ABrokenLocalWithNoTranspilerInvolved_StillThrows() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.TwoIntLocals))!;
        MethodBase broken = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubled))!;
        MethodBase healthy = typeof(LocalMethods).GetMethod(nameof(LocalMethods.Observe))!;

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [
                new Injection(broken, new InjectAt.Return(), "mod-a", 0),
                new Injection(healthy, new InjectAt.Return(), "mod-b", 0),
            ]));

        Assert.Equal("CONC147", error.Code);
    }

    // Eviction is useless if the evicted mod cannot see why, so the rejection carries the same
    // message the throw carried: every candidate, its slot, and where the slot came from.
    [Fact]
    public void AnEvictedInjection_KeepsTheFullAmbiguityDiagnostic() {
        ComposeResult result = WrapperComposer.Compose(OneIntTarget(), BrokenPlusTranspiler());

        RejectedInjection rejected = Assert.Single(result.Rejected);

        Assert.Equal("mod-a", rejected.Owner);
        Assert.Contains("ReadInt", rejected.Message);
        Assert.Contains("(target body)", rejected.Message);
        Assert.Contains("(transpiler-added)", rejected.Message);
        Assert.Contains("Set Ordinal to pick one", rejected.Message);
    }

    [Fact]
    public void AHealthyCompose_RejectsNothing() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.OneLongLocal))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;

        ComposeResult result = WrapperComposer.Compose(target, [new Injection(injection, new InjectAt.Return(), "mod-a", 0)]);

        Assert.Empty(result.Rejected);
    }

    // With nothing else on the target, evicting would leave the owner with a silent no-op and no
    // error anywhere. The only mod affected is the one that wrote the selector, so it gets the throw.
    [Fact]
    public void ALoneBrokenLocal_StillThrows() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.TwoIntLocals))!;
        MethodBase broken = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubled))!;

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [new Injection(broken, new InjectAt.Return(), "mod-a", 0)]));

        Assert.Equal("CONC147", error.Code);
    }

    // At.Local resolves its slot the same way a [Local] parameter does, so a transpiler-added slot
    // makes it ambiguous the same way. It evicts too.
    [Fact]
    public void AnAtLocalFailure_IsEvictableToo() {
        MethodBase target = typeof(LocalPositionHost).GetMethod(nameof(LocalPositionHost.Points))!;
        MethodBase broken = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        MethodBase transpiler = typeof(EvictionTranspilers).GetMethod(nameof(EvictionTranspilers.AddScratchFloat))!;

        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(broken, new InjectAt.Local(typeof(float), LocalAccess.Store), "mod-a", 0),
            new Injection(transpiler, new InjectAt.Transpiler(false), "mod-b", 0),
        ]);

        Func<LocalPositionHost, float, string> run = result.Wrapper.CreateDelegate<Func<LocalPositionHost, float, string>>();

        Assert.Equal("12", run(new LocalPositionHost(), 5f));
        Assert.Contains(result.Rejected, r => r.Owner == "mod-a" && r.Code == "CONC147");
    }

    // The By held against the target's own IL and stopped holding once someone edited a store out.
    // That is a coexistence failure, so it drops one injection instead of failing the target.
    [Fact]
    public void AnAtLocalByThatSomethingElseMoved_IsEvicted() {
        MethodBase target = typeof(LocalPositionHost).GetMethod(nameof(LocalPositionHost.Points))!;
        MethodBase adjust = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        MethodBase transpiler = typeof(EvictionTranspilers).GetMethod(nameof(EvictionTranspilers.DropTheLastFloatStore))!;

        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(adjust, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 2, Ordinal: 1), "mod-a", 0),
            new Injection(transpiler, new InjectAt.Transpiler(false), "mod-b", 0),
        ]);

        RejectedInjection rejected = Assert.Single(result.Rejected);
        Assert.Equal("mod-a", rejected.Owner);
        Assert.Equal("CONC155", rejected.Code);
    }

    // A By the target body simply does not have is the author's own count, not another mod's doing,
    // so it fails the compose rather than dropping the injection.
    [Fact]
    public void AnAtLocalByThatDoesNotExist_Throws() {
        MethodBase target = typeof(LocalPositionHost).GetMethod(nameof(LocalPositionHost.Points))!;
        MethodBase broken = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        MethodBase healthy = typeof(LocalMethods).GetMethod(nameof(LocalMethods.Observe))!;

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [
                new Injection(broken, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 5), "mod-a", 0),
                new Injection(healthy, new InjectAt.Return(), "mod-b", 0),
            ]));

        Assert.Equal("CONC155", error.Code);
        Assert.Contains("Injection 'LocalPositionMethods.Adjust' on 'LocalPositionHost.Points'", error.Message);
    }

    // Two At.Local selectors can share one injection method, so eviction keys on the injection, not
    // the method. Keying on the method would drop both and leave the target with nothing.
    [Fact]
    public void TwoSelectorsOnOneMethod_OnlyTheBrokenOneIsEvicted() {
        MethodBase target = typeof(LocalPositionHost).GetMethod(nameof(LocalPositionHost.Points))!;
        MethodBase adjust = typeof(LocalPositionMethods).GetMethod(nameof(LocalPositionMethods.Adjust))!;
        MethodBase transpiler = typeof(EvictionTranspilers).GetMethod(nameof(EvictionTranspilers.AddScratchFloat))!;

        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(adjust, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 1, Ordinal: 1), "mod-a", 0),
            new Injection(adjust, new InjectAt.Local(typeof(float), LocalAccess.Store, By: 1), "mod-b", 0),
            new Injection(transpiler, new InjectAt.Transpiler(false), "mod-c", 0),
        ]);

        Func<LocalPositionHost, float, string> run = result.Wrapper.CreateDelegate<Func<LocalPositionHost, float, string>>();

        RejectedInjection rejected = Assert.Single(result.Rejected);
        Assert.Equal("mod-b", rejected.Owner);
        Assert.Equal("212", run(new LocalPositionHost(), 5f));
    }

    // An eviction followed by a failure that is not evictable. The eviction diagnostic would otherwise
    // be lost, because only the last failure is the one that throws.
    [Fact]
    public void WhenALaterFailureThrows_TheThrowCarriesEveryEviction() {
        MethodBase target = typeof(EvictionHost).GetMethod(nameof(EvictionHost.OneInt))!;
        MethodBase evicted = typeof(StaticLocalMethods).GetMethod(nameof(StaticLocalMethods.ReadInt))!;
        MethodBase unbindable = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        MethodBase transpiler = typeof(EvictionTranspilers).GetMethod(nameof(EvictionTranspilers.AddScratchInt))!;

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [
                new Injection(unbindable, new InjectAt.Return(), "mod-b", 0),
                new Injection(evicted, new InjectAt.Return(), "mod-a", 0),
                new Injection(transpiler, new InjectAt.Transpiler(false), "mod-c", 0),
            ]));

        Assert.Equal("CONC148", error.Code);
        Assert.Contains("ReadInt", error.Message);
        Assert.Contains("Also evicted", error.Message);
    }

    // The Harmony bridge only learns about an eviction through this out parameter. Without it the
    // whole stream used to be discarded, taking every other mod's injection with it.
    [Fact]
    public void TransformStream_ReportsWhatItEvicted_AndKeepsTheRest() {
        MethodBase target = typeof(StreamTargets).GetMethod(nameof(StreamTargets.Target))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        LocalRef first = context.DeclareLocal(typeof(int));
        LocalRef second = context.DeclareLocal(typeof(int));
        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Stloc, first),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Stloc, second),
            new CodeInstruction(OpCodes.Ldloc, first),
            new CodeInstruction(OpCodes.Ldloc, second),
            new CodeInstruction(OpCodes.Add),
            new CodeInstruction(OpCodes.Ret),
        ];

        List<CodeInstruction> composed = WrapperComposer.TransformStream(
            target,
            source,
            [
                new Injection(typeof(StaticLocalMethods).GetMethod(nameof(StaticLocalMethods.ReadInt))!, new InjectAt.Return(), "mod-a", 0),
                new Injection(typeof(StreamTargets).GetMethod(nameof(StreamTargets.HeadInjection))!, new InjectAt.Return(), "mod-b", 0),
            ],
            context,
            out IReadOnlyList<RejectedInjection> rejected);

        Assert.NotEmpty(composed);
        RejectedInjection dropped = Assert.Single(rejected);
        Assert.Equal("mod-a", dropped.Owner);
        Assert.Equal("CONC147", dropped.Code);
    }

    // Eviction is scoped to [Local] binding. Every other ConcordEmitException still fails the target.
    [Fact]
    public void ANonLocalFailure_StillFailsTheWholeCompose() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.OneLongLocal))!;
        MethodBase broken = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        MethodBase healthy = typeof(LocalMethods).GetMethod(nameof(LocalMethods.Observe))!;

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [
                new Injection(broken, new InjectAt.Head(), "mod-a", 0),
                new Injection(healthy, new InjectAt.Return(), "mod-b", 0),
            ]));

        Assert.Equal("CONC146", error.Code);
    }

    private static MethodBase OneIntTarget() {
        return typeof(EvictionHost).GetMethod(nameof(EvictionHost.OneInt))!;
    }

    private static Injection[] BrokenPlusTranspiler() {
        MethodBase broken = typeof(StaticLocalMethods).GetMethod(nameof(StaticLocalMethods.ReadInt))!;
        MethodBase healthy = typeof(LocalMethods).GetMethod(nameof(LocalMethods.Observe))!;
        MethodBase transpiler = typeof(EvictionTranspilers).GetMethod(nameof(EvictionTranspilers.AddScratchInt))!;

        return [
            new Injection(broken, new InjectAt.Return(), "mod-a", 0),
            new Injection(healthy, new InjectAt.Return(), "mod-b", 0),
            new Injection(transpiler, new InjectAt.Transpiler(false), "mod-c", 0),
        ];
    }
}
