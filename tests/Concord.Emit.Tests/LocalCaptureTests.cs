using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public class LocalHost {
    // A Debug build gives every `return <expr>;` a temp of the return type, so the local under test
    // is a long: that keeps exactly one candidate of its type in the body.
    public int OneLongLocal(int seed) {
        long doubled = seed * 2L;
        return (int)doubled + 1;
    }

    // Two int locals in Debug and in Release: ToString() takes each one's address, so Release cannot
    // schedule either onto the stack. A plain [Local] int is genuinely ambiguous here.
    public int TwoIntLocals(int seed) {
        int first = seed * 2;
        int second = seed * 3;
        return first.ToString().Length + second.ToString().Length;
    }
}

public static class LocalRecorder {
    public static int SeenInt;

    public static long SeenLong;

    public static int SeenCount;

    public static bool SeenBool;

    public static bool Observed;
}

public class LocalMethods {
    public void ReadDoubled([Local] int doubled) {
        LocalRecorder.SeenInt = doubled;
    }

    public void ReadDoubledLong([Local] long doubled) {
        LocalRecorder.SeenLong = doubled;
    }

    public void Observe() {
        LocalRecorder.Observed = true;
    }
}

public class TwoIntHost {
    // Three int locals in Debug and in Release. The return type is string so the Debug-only return
    // temp is not a fourth int, and each local is address-taken by ToString() so Release cannot
    // schedule any of them onto the stack.
    public string ThreeIntLocals(int seed) {
        int first = seed * 2;
        int second = seed * 3;
        int third = seed * 4;
        return string.Concat(first.ToString(), second.ToString(), third.ToString());
    }
}

public static class StaticLocalMethods {
    public static void ReadInt([Local] int value) {
        LocalRecorder.SeenInt = value;
    }
}

public class OrdinalMethods {
    public void ReadSecond([Local(Ordinal = 2)] int value) {
        LocalRecorder.SeenInt = value;
    }
}

public class GenericLocalHost {
    public int ListLocal(int seed) {
        List<int> values = [seed, seed * 2];
        return values.Count;
    }
}

public class GenericLocalMethods {
    public void ReadList([Local] List<int> values) {
        LocalRecorder.SeenCount = values.Count;
    }
}

public class BoolHost {
    // One bool local in Debug and in Release: ToString() takes the local's address so Release
    // cannot stack-schedule it, and the string return type keeps the Debug return temp out of
    // the bool count. The wrapper's own Cancel and HasReturn locals are bools too, so a search
    // that ran past the target's own slots would see three.
    public string OneBoolLocal(int seed) {
        bool positive = seed > 0;
        return positive.ToString();
    }
}

public class ExcludedHost {
    // Returns string so the Debug-only return temp is not an int a [Local] int could bind to.
    public unsafe string Pinned(int[] values) {
        fixed (int* p = values) {
            return (*p).ToString();
        }
    }
}

public class BoolMethods {
    public void ReadFlag([Local] bool value) {
        LocalRecorder.SeenBool = value;
    }
}

public class IndexMethods {
    public void ReadSlotZeroAsString([Local(Index = 0)] string value) {
        LocalRecorder.SeenCount = value.Length;
    }

    public void ReadSlotTwo([Local(Index = 2)] int value) {
        LocalRecorder.SeenInt = value;
    }

    public void ReadSlotNine([Local(Index = 9)] int value) {
        LocalRecorder.SeenInt = value;
    }
}

public static class StreamIndexMethods {
    public static void ReadSlotOne([Local(Index = 1)] int value) {
        LocalRecorder.SeenInt = value;
    }
}

public class OverflowMethods {
    public void ReadNinth([Local(Ordinal = 9)] int value) {
        LocalRecorder.SeenInt = value;
    }
}

public class MissingTypeMethods {
    public void ReadDouble([Local] double value) {
        LocalRecorder.SeenCount = (int)value;
    }
}

public class InvokeLocalHost {
    // Returns string so the Debug-only return temp is not a second long, and ToString() takes the
    // local's address so Release keeps it in a slot instead of on the stack.
    public string CallAfterAssign(int seed) {
        long doubled = seed * 2L;
        return Helper(doubled.ToString());
    }

    public static string Helper(string value) {
        return value;
    }
}

public sealed class NewObjLocalBox {
    public NewObjLocalBox(string value) {
        Value = value;
    }

    public string Value { get; }
}

public class NewObjLocalHost {
    public string ConstructAfterAssign(int seed) {
        long doubled = seed * 2L;
        NewObjLocalBox box = new NewObjLocalBox(doubled.ToString());
        return box.Value;
    }
}

public static class StaticLocalReader {
    public static void ReadLong([Local] long doubled) {
        LocalRecorder.SeenLong = doubled;
    }
}

public sealed class LocalCaptureTests {
    [Fact]
    public void ImplicitTypeMatch_BindsTheOnlyLocalOfThatType() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.OneLongLocal))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<LocalHost, int, int> run = result.Wrapper.CreateDelegate<Func<LocalHost, int, int>>();

        LocalRecorder.SeenLong = 0;
        int returned = run(new LocalHost(), 5);

        Assert.Equal(11, returned);
        Assert.Equal(10L, LocalRecorder.SeenLong);
    }

    [Fact]
    public void ImplicitMatch_RejectsAmbiguity_AndNamesEveryCandidate() {
        MethodBase target = typeof(TwoIntHost).GetMethod(nameof(TwoIntHost.ThreeIntLocals))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubled))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        // first, second and third. The return temp is a string, so it never joins the int count.
        Assert.Equal("CONC147", error.Code);
        Assert.Contains("matches 3 locals", error.Message);
        Assert.Contains("slot 0 (target body)", error.Message);
        Assert.Contains("slot 1 (target body)", error.Message);
        Assert.Contains("slot 2 (target body)", error.Message);
    }

    [Fact]
    public void ImplicitTypeMatch_BindsAGenericLocal() {
        MethodBase target = typeof(GenericLocalHost).GetMethod(nameof(GenericLocalHost.ListLocal))!;
        MethodBase injection = typeof(GenericLocalMethods).GetMethod(nameof(GenericLocalMethods.ReadList))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<GenericLocalHost, int, int> run = result.Wrapper.CreateDelegate<Func<GenericLocalHost, int, int>>();

        LocalRecorder.SeenCount = 0;
        int returned = run(new GenericLocalHost(), 5);

        Assert.Equal(2, returned);
        Assert.Equal(2, LocalRecorder.SeenCount);
    }

    [Fact]
    public void Ordinal_PicksTheNthLocalOfThatType() {
        MethodBase target = typeof(TwoIntHost).GetMethod(nameof(TwoIntHost.ThreeIntLocals))!;
        MethodBase injection = typeof(OrdinalMethods).GetMethod(nameof(OrdinalMethods.ReadSecond))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<TwoIntHost, int, string> run = result.Wrapper.CreateDelegate<Func<TwoIntHost, int, string>>();

        LocalRecorder.SeenInt = 0;
        run(new TwoIntHost(), 5);

        Assert.Equal(15, LocalRecorder.SeenInt);
    }

    [Fact]
    public void TransformStream_CountsLocalsPastTheTargetsOwnAsTranspilerAdded() {
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
        Injection read = new Injection(
            typeof(StaticLocalMethods).GetMethod(nameof(StaticLocalMethods.ReadInt))!,
            new InjectAt.Return(),
            "test",
            0);

        // On this route every local arrives declared through the context, the target's own included,
        // so the written-back body cannot say which came from another patcher's transpiler. The target
        // decides: StreamTargets.Target declares none, so both of these were added on the way in. That
        // is what lets the Harmony bridge evict one mod's broken selector instead of failing the whole
        // stream and taking every other mod's injection with it.
        Injection healthy = new Injection(
            typeof(StreamTargets).GetMethod(nameof(StreamTargets.HeadInjection))!, new InjectAt.Return(), "other", 0);

        WrapperComposer.TransformStream(target, source, [read, healthy], context, out IReadOnlyList<RejectedInjection> rejected);

        RejectedInjection dropped = Assert.Single(rejected);
        Assert.Equal("CONC147", dropped.Code);
        Assert.Contains("matches 2 locals", dropped.Message);
        Assert.Contains("(transpiler-added)", dropped.Message);
    }

    [Fact]
    public void SearchRange_ExcludesProtocolLocals() {
        MethodBase target = typeof(BoolHost).GetMethod(nameof(BoolHost.OneBoolLocal))!;
        MethodBase injection = typeof(BoolMethods).GetMethod(nameof(BoolMethods.ReadFlag))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<BoolHost, int, string> run = result.Wrapper.CreateDelegate<Func<BoolHost, int, string>>();

        LocalRecorder.SeenBool = false;
        run(new BoolHost(), 5);

        Assert.True(LocalRecorder.SeenBool);
    }

    [Fact]
    public void PinnedLocal_IsRejectedByKind() {
        MethodBase target = typeof(ExcludedHost).GetMethod(nameof(ExcludedHost.Pinned))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubled))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC153", error.Code);
        Assert.Contains("pinned", error.Message);
    }

    [Fact]
    public void ByRefLocal_IsRejectedByKind() {
        MethodBase target = typeof(StreamTargets).GetMethod(nameof(StreamTargets.Target))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        context.DeclareLocal(typeof(int).MakeByRefType());
        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ret),
        ];
        Injection read = new Injection(
            typeof(StaticLocalMethods).GetMethod(nameof(StaticLocalMethods.ReadInt))!,
            new InjectAt.Return(),
            "test",
            0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.TransformStream(target, source, [read], context));

        Assert.Equal("CONC153", error.Code);
        Assert.Contains("byref", error.Message);
    }

    [Fact]
    public void UnusedSlot_IsRejected() {
        MethodBase target = typeof(StreamTargets).GetMethod(nameof(StreamTargets.Target))!;
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        LocalRef used = context.DeclareLocal(typeof(int));
        context.DeclareLocal(typeof(int));
        List<CodeInstruction> source = [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Stloc, used),
            new CodeInstruction(OpCodes.Ldloc, used),
            new CodeInstruction(OpCodes.Ret),
        ];
        Injection read = new Injection(
            typeof(StreamIndexMethods).GetMethod(nameof(StreamIndexMethods.ReadSlotOne))!,
            new InjectAt.Return(),
            "test",
            0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.TransformStream(target, source, [read], context));

        // Slot 1 is declared but no instruction names it, so binding it would read zero forever.
        Assert.Equal("CONC162", error.Code);
        Assert.Contains("slot 1", error.Message);
    }

    [Fact]
    public void Index_BindsTheNamedSlot() {
        MethodBase target = typeof(TwoIntHost).GetMethod(nameof(TwoIntHost.ThreeIntLocals))!;
        MethodBase injection = typeof(IndexMethods).GetMethod(nameof(IndexMethods.ReadSlotTwo))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<TwoIntHost, int, string> run = result.Wrapper.CreateDelegate<Func<TwoIntHost, int, string>>();

        LocalRecorder.SeenInt = 0;
        run(new TwoIntHost(), 5);

        Assert.Equal(20, LocalRecorder.SeenInt);
    }

    [Fact]
    public void Index_PastTheSearchRange_IsRejected() {
        MethodBase target = typeof(TwoIntHost).GetMethod(nameof(TwoIntHost.ThreeIntLocals))!;
        MethodBase injection = typeof(IndexMethods).GetMethod(nameof(IndexMethods.ReadSlotNine))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC149", error.Code);
        Assert.Contains("asks for slot 9", error.Message);
    }

    [Fact]
    public void Index_OntoAMismatchedSlotType_IsRejected() {
        MethodBase target = typeof(TwoIntHost).GetMethod(nameof(TwoIntHost.ThreeIntLocals))!;
        MethodBase injection = typeof(IndexMethods).GetMethod(nameof(IndexMethods.ReadSlotZeroAsString))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC150", error.Code);
        Assert.Contains("slot 0", error.Message);
        Assert.Contains("System.Int32", error.Message);
    }

    [Fact]
    public void Ordinal_PastTheCandidateCount_IsRejected() {
        MethodBase target = typeof(TwoIntHost).GetMethod(nameof(TwoIntHost.ThreeIntLocals))!;
        MethodBase injection = typeof(OverflowMethods).GetMethod(nameof(OverflowMethods.ReadNinth))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC148", error.Code);
        Assert.Contains("occurrence 9", error.Message);
        Assert.Contains("holds 3", error.Message);
    }

    // The overflow path re-scans for an unbindable slot the same way the implicit path does, so a
    // pinned local that emptied the candidate list gets named here too rather than reported as a
    // bare count mismatch.
    [Fact]
    public void Ordinal_PastTheCandidateCount_NamesAPinnedSlot() {
        MethodBase target = typeof(ExcludedHost).GetMethod(nameof(ExcludedHost.Pinned))!;
        MethodBase injection = typeof(OverflowMethods).GetMethod(nameof(OverflowMethods.ReadNinth))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC153", error.Code);
        Assert.Contains("pinned", error.Message);
        Assert.Contains("occurrence 9", error.Message);
    }

    [Fact]
    public void ImplicitMatch_WithNoLocalOfThatType_IsRejected() {
        MethodBase target = typeof(TwoIntHost).GetMethod(nameof(TwoIntHost.ThreeIntLocals))!;
        MethodBase injection = typeof(MissingTypeMethods).GetMethod(nameof(MissingTypeMethods.ReadDouble))!;
        Injection read = new Injection(injection, new InjectAt.Return(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC148", error.Code);
        Assert.Contains("matches no local of type", error.Message);
    }

    [Fact]
    public void Head_RejectsLocalCapture() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.OneLongLocal))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        Injection read = new Injection(injection, new InjectAt.Head(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC146", error.Code);
    }

    [Fact]
    public void Constant_RejectsLocalCapture() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.OneLongLocal))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        Injection read = new Injection(injection, new InjectAt.Constant(2), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC158", error.Code);
        Assert.Contains("At.Return", error.Message);
    }

    [Fact]
    public void WholeMethodAround_RejectsLocalCapture() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.OneLongLocal))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        Injection read = new Injection(injection, new InjectAt.Around(), "test", 0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC160", error.Code);
        Assert.Contains("At.Return", error.Message);
        Assert.DoesNotContain("At.Finally", error.Message);
    }

    [Fact]
    public void InvokeHeadShift_BindsALocal() {
        MethodBase target = typeof(InvokeLocalHost).GetMethod(nameof(InvokeLocalHost.CallAfterAssign))!;
        MethodBase injection = typeof(StaticLocalReader).GetMethod(nameof(StaticLocalReader.ReadLong))!;
        Injection read = new Injection(
            injection,
            new InjectAt.Invoke(typeof(InvokeLocalHost), nameof(InvokeLocalHost.Helper), At.Head),
            "test",
            0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<InvokeLocalHost, int, string> run = result.Wrapper.CreateDelegate<Func<InvokeLocalHost, int, string>>();

        LocalRecorder.SeenLong = 0;
        string returned = run(new InvokeLocalHost(), 5);

        Assert.Equal("10", returned);
        Assert.Equal(10L, LocalRecorder.SeenLong);
    }

    // At.Argument rewrites one call argument through a 'T M(T original)' method, so a [Local]
    // sibling there has no binding map and would read a call argument instead.
    [Fact]
    public void InvokeArgumentShift_RejectsLocalCapture() {
        MethodBase target = typeof(InvokeLocalHost).GetMethod(nameof(InvokeLocalHost.CallAfterAssign))!;
        MethodBase injection = typeof(StaticLocalReader).GetMethod(nameof(StaticLocalReader.ReadLong))!;
        Injection read = new Injection(
            injection,
            new InjectAt.Invoke(typeof(InvokeLocalHost), nameof(InvokeLocalHost.Helper), At.Argument),
            "test",
            0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        Assert.Equal("CONC158", error.Code);
        Assert.Contains("At.Argument", error.Message);
    }

    [Theory]
    [InlineData(At.Head)]
    [InlineData(At.Tail)]
    public void NewObjShift_BindsALocal(At shift) {
        MethodBase target = typeof(NewObjLocalHost).GetMethod(nameof(NewObjLocalHost.ConstructAfterAssign))!;
        MethodBase injection = typeof(StaticLocalReader).GetMethod(nameof(StaticLocalReader.ReadLong))!;
        Injection read = new Injection(injection, new InjectAt.NewObj(typeof(NewObjLocalBox), shift), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<NewObjLocalHost, int, string> run = result.Wrapper.CreateDelegate<Func<NewObjLocalHost, int, string>>();

        LocalRecorder.SeenLong = 0;
        string returned = run(new NewObjLocalHost(), 5);

        Assert.Equal("10", returned);
        Assert.Equal(10L, LocalRecorder.SeenLong);
    }

    [Fact]
    public void InvokeAroundShift_RejectsLocalCapture() {
        MethodBase target = typeof(InvokeLocalHost).GetMethod(nameof(InvokeLocalHost.CallAfterAssign))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        Injection read = new Injection(
            injection,
            new InjectAt.Invoke(typeof(InvokeLocalHost), nameof(InvokeLocalHost.Helper), At.Around),
            "test",
            0);

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [read]));

        // The Around shift binds leading parameters to the matched call's arguments, outside the
        // binding map, so an ungated [Local] there reads a call argument or trips CONC039.
        Assert.Equal("CONC158", error.Code);
        Assert.Contains("At.Around", error.Message);
    }

    [Fact]
    public void Tail_BindsALocal() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.OneLongLocal))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        Injection read = new Injection(injection, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<LocalHost, int, int> run = result.Wrapper.CreateDelegate<Func<LocalHost, int, int>>();

        LocalRecorder.SeenLong = 0;
        Assert.Equal(11, run(new LocalHost(), 5));
        Assert.Equal(10L, LocalRecorder.SeenLong);
    }

    [Fact]
    public void Finally_BindsALocal() {
        MethodBase target = typeof(LocalHost).GetMethod(nameof(LocalHost.OneLongLocal))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        Injection read = new Injection(injection, new InjectAt.Finally(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<LocalHost, int, int> run = result.Wrapper.CreateDelegate<Func<LocalHost, int, int>>();

        LocalRecorder.SeenLong = 0;
        Assert.Equal(11, run(new LocalHost(), 5));
        Assert.Equal(10L, LocalRecorder.SeenLong);
    }

    [Fact]
    public void InvokeTailShift_BindsALocal() {
        MethodBase target = typeof(InvokeLocalHost).GetMethod(nameof(InvokeLocalHost.CallAfterAssign))!;
        MethodBase injection = typeof(LocalMethods).GetMethod(nameof(LocalMethods.ReadDoubledLong))!;
        Injection read = new Injection(
            injection,
            new InjectAt.Invoke(typeof(InvokeLocalHost), nameof(InvokeLocalHost.Helper), At.Tail),
            "test",
            0);

        ComposeResult result = WrapperComposer.Compose(target, [read]);
        Func<InvokeLocalHost, int, string> run = result.Wrapper.CreateDelegate<Func<InvokeLocalHost, int, string>>();

        LocalRecorder.SeenLong = 0;
        Assert.Equal("10", run(new InvokeLocalHost(), 5));
        Assert.Equal(10L, LocalRecorder.SeenLong);
    }
}
