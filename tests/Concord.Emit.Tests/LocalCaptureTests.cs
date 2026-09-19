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
}

public static class LocalRecorder {
    public static int SeenInt;

    public static long SeenLong;

    public static int SeenCount;
}

public class LocalMethods {
    public void ReadDoubled([Local] int doubled) {
        LocalRecorder.SeenInt = doubled;
    }

    public void ReadDoubledLong([Local] long doubled) {
        LocalRecorder.SeenLong = doubled;
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

        // Three candidates in a Debug build: first, second, and the temp the return expression gets.
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
    public void TransformStream_CountsTheSuppliedStreamsOwnLocalsAsTargetBody() {
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

        ConcordEmitException error = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.TransformStream(target, source, [read], context));

        // The stream's own locals are the body being composed, so both read as target body. Counting
        // them off the real target instead would call both transpiler-added.
        Assert.Equal("CONC147", error.Code);
        Assert.Contains("matches 2 locals", error.Message);
        Assert.Contains("slot 0 (target body)", error.Message);
        Assert.Contains("slot 1 (target body)", error.Message);
    }
}
