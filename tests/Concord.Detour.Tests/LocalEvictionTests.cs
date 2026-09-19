using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Detour.Tests;

public static class LocalEvictionTargets {
    // One int local in Debug and in Release: ToString() takes its address so Release keeps it in a
    // slot, and the string return type keeps the Debug return temp out of the int count. A plain
    // [Local] int binds it until someone else's transpiler declares a second one.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string DoubledOne(int seed) {
        int doubled = seed * 2;
        return doubled.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string DoubledTwo(int seed) {
        int doubled = seed * 2;
        return doubled.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string DoubledThree(int seed) {
        int doubled = seed * 2;
        return doubled.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string DoubledFour(int seed) {
        int doubled = seed * 2;
        return doubled.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string DoubledFive(int seed) {
        int doubled = seed * 2;
        return doubled.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string DoubledSix(int seed) {
        int doubled = seed * 2;
        return doubled.ToString();
    }
}

internal sealed class CapturingTraceListener : TraceListener {
    internal List<string> Lines { get; } = new List<string>();

    public override void Write(string? message) {
        WriteLine(message);
    }

    public override void WriteLine(string? message) {
        if (message is not null) {
            Lines.Add(message);
        }
    }
}

public static class LocalEvictionInjections {
    public static int Seen;

    public static int Marked;

    public static void ReadDoubled([Local] int doubled) {
        Seen = doubled;
    }

    public static void Mark(int value) {
        Marked = value;
    }

    // Mod B's honest transpiler: it declares an int local of its own and uses it. That is all it
    // takes to make mod A's [Local] int ambiguous, through no fault of B's.
    public static IEnumerable<CodeInstruction> AddScratchLocal(
        IEnumerable<CodeInstruction> instructions,
        ITranspilerContext context) {
        LocalRef scratch = context.DeclareLocal(typeof(int));
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);

        output.Insert(0, new CodeInstruction(OpCodes.Ldc_I4, 42));
        output.Insert(1, new CodeInstruction(OpCodes.Stloc_S, scratch));
        output.Insert(2, new CodeInstruction(OpCodes.Ldloc_S, scratch));
        output.Insert(3, new CodeInstruction(OpCodes.Call, typeof(LocalEvictionInjections).GetMethod(nameof(Mark))));

        return output;
    }
}

public sealed class LocalEvictionTests {
    [Theory]
    [InlineData(2, true, true)]
    [InlineData(3, true, false)]
    [InlineData(4, false, true)]
    [InlineData(5, false, false)]
    public void EveryApplyAndUndoOrder_Succeeds(int slot, bool applyAFirst, bool undoAFirst) {
        (MethodBase target, Func<int, string> run) = Case(slot);
        IDetourBackend backend = new MonoModDetourBackend();

        IDetourHandle a;
        IDetourHandle b;
        if (applyAFirst) {
            a = backend.ApplyComposed(target, [LocalRead()]);
            b = backend.ApplyComposed(target, [Transpiler()]);
        } else {
            b = backend.ApplyComposed(target, [Transpiler()]);
            a = backend.ApplyComposed(target, [LocalRead()]);
        }

        // Undone in a finally: these targets are shared statics, so a failed assert here would leave
        // one patched for every test after it and turn one red into a screenful.
        try {
            Assert.True(a.IsApplied);
            Assert.True(b.IsApplied);

            Reset();
            Assert.Equal("10", run(5));
            Assert.Equal(42, LocalEvictionInjections.Marked);
            Assert.Equal(0, LocalEvictionInjections.Seen);

            IDetourHandle first = undoAFirst ? a : b;
            IDetourHandle second = undoAFirst ? b : a;

            first.Dispose();
            Assert.False(first.IsApplied);

            Reset();
            Assert.Equal("10", run(5));
            if (undoAFirst) {
                Assert.Equal(42, LocalEvictionInjections.Marked);
            } else {
                // B is gone, so the int local is unambiguous again and A binds it.
                Assert.Equal(10, LocalEvictionInjections.Seen);
            }

            second.Dispose();
            Assert.False(second.IsApplied);

            Reset();
            Assert.Equal("10", run(5));
            Assert.Equal(0, LocalEvictionInjections.Marked);
            Assert.Equal(0, LocalEvictionInjections.Seen);
        } finally {
            Undo(b, a);
        }
    }

    [Fact]
    public void ATranspilerThatBreaksAnotherModsLocal_StillApplies() {
        (MethodBase target, Func<int, string> run) = Case(1);
        IDetourBackend backend = new MonoModDetourBackend();
        List<string> reported = new List<string>();

        IDetourHandle a = backend.ApplyComposed(target, [LocalRead()]);
        IDetourHandle? b = null;

        try {
            Reset();
            Assert.Equal("10", run(5));
            Assert.Equal(10, LocalEvictionInjections.Seen);

            Action<string>? previous = PatchLog.Sink;
            PatchLog.Sink = reported.Add;
            try {
                b = backend.ApplyComposed(target, [Transpiler()]);
            } finally {
                PatchLog.Sink = previous;
            }

            // The evicted mod has to be able to find out why its injection stopped running.
            string evicted = Assert.Single(reported, entry => entry.StartsWith(CoexistenceLogMarkers.InjectionEvicted, StringComparison.Ordinal));
            Assert.Contains("mod-a", evicted);
            Assert.Contains("CONC147", evicted);
            Assert.Contains("transpiler-added", evicted);

            Assert.True(b.IsApplied);
            Reset();
            Assert.Equal("10", run(5));
            Assert.Equal(42, LocalEvictionInjections.Marked);
            Assert.Equal(0, LocalEvictionInjections.Seen);

            b.Dispose();

            Reset();
            Assert.Equal("10", run(5));
            Assert.Equal(10, LocalEvictionInjections.Seen);
            Assert.Equal(0, LocalEvictionInjections.Marked);

            a.Dispose();
            Reset();
            Assert.Equal("10", run(5));
            Assert.Equal(0, LocalEvictionInjections.Seen);
        } finally {
            Undo(b, a);
        }
    }

    // No sink registered is the default, and Debug.WriteLine would compile away under Release. This
    // is the only test that exercises the fallback, so it is the only thing that would catch that.
    [Fact]
    public void WithNoSinkRegistered_TheDiagnosticStillReachesTrace() {
        (MethodBase target, Func<int, string> run) = Case(6);
        IDetourBackend backend = new MonoModDetourBackend();
        CapturingTraceListener listener = new CapturingTraceListener();
        Action<string>? previous = PatchLog.Sink;

        IDetourHandle a = backend.ApplyComposed(target, [LocalRead()]);
        IDetourHandle b;
        PatchLog.Sink = null;
        Trace.Listeners.Add(listener);
        try {
            b = backend.ApplyComposed(target, [Transpiler()]);
        } finally {
            Trace.Listeners.Remove(listener);
            PatchLog.Sink = previous;
        }

        try {
            Assert.Contains(
                listener.Lines,
                line => line.StartsWith(CoexistenceLogMarkers.InjectionEvicted, StringComparison.Ordinal)
                    && line.Contains("mod-a")
                    && line.Contains("CONC147"));

            Reset();
            Assert.Equal("10", run(5));
        } finally {
            Undo(b, a);
        }
    }

    // Outermost first, so the target is left the way it was found. A handle a test already disposed
    // reports IsApplied false and is skipped, which is what lets the same call close both the happy
    // path and a failed assert partway through.
    private static void Undo(params IDetourHandle?[] handles) {
        foreach (IDetourHandle? handle in handles) {
            if (handle?.IsApplied == true) {
                handle.Dispose();
            }
        }
    }

    private static void Reset() {
        LocalEvictionInjections.Seen = 0;
        LocalEvictionInjections.Marked = 0;
    }

    private static Injection LocalRead() {
        MethodInfo method = typeof(LocalEvictionInjections).GetMethod(nameof(LocalEvictionInjections.ReadDoubled))!;
        return new Injection(method, new InjectAt.Return(), "mod-a", 0);
    }

    private static Injection Transpiler() {
        MethodInfo method = typeof(LocalEvictionInjections).GetMethod(nameof(LocalEvictionInjections.AddScratchLocal))!;
        return new Injection(method, new InjectAt.Transpiler(false), "mod-b", 0);
    }

    private static (MethodBase Target, Func<int, string> Run) Case(int slot) {
        return slot switch {
            1 => (Method(nameof(LocalEvictionTargets.DoubledOne)), LocalEvictionTargets.DoubledOne),
            2 => (Method(nameof(LocalEvictionTargets.DoubledTwo)), LocalEvictionTargets.DoubledTwo),
            3 => (Method(nameof(LocalEvictionTargets.DoubledThree)), LocalEvictionTargets.DoubledThree),
            4 => (Method(nameof(LocalEvictionTargets.DoubledFour)), LocalEvictionTargets.DoubledFour),
            5 => (Method(nameof(LocalEvictionTargets.DoubledFive)), LocalEvictionTargets.DoubledFive),
            _ => (Method(nameof(LocalEvictionTargets.DoubledSix)), LocalEvictionTargets.DoubledSix),
        };
    }

    private static MethodBase Method(string name) {
        return typeof(LocalEvictionTargets).GetMethod(name)!;
    }
}
