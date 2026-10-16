using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class SuppressedVictims {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Suppressed() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Unsuppressed() {
        return 1;
    }
}

public static class SuppressedStub {
    public static int Stub() {
        throw new InvalidOperationException("The suppressed-rebuild stub was called, so Harmony detoured the target to it instead of leaving the original alone.");
    }
}

public static class SuppressNotify {
    public static MethodBase? Watched;

    public static int Fired;

    public static void Head(ControlHandle<MethodInfo> ch, MethodBase? original, PatchInfo patchInfo) {
        if (original == null || !ReferenceEquals(original, Watched)) {
            return;
        }

        Fired++;
        ch.ReturnValue = typeof(SuppressedStub).GetMethod(nameof(SuppressedStub.Stub))!;
        ch.Cancel();
    }
}

public sealed class SuppressedRebuildTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so nothing can be written.";

    private static readonly MethodInfo Shifter = typeof(Shift).GetMethod(nameof(Shift.Plus10))!;

    private readonly Harmony harmony = new Harmony("concord.fork.suppressed");

    private readonly List<MethodBase> written = [];

    private IDetourHandle? hook;

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        harmony.UnpatchAll(harmony.Id);
        hook?.Dispose();
        SuppressNotify.Watched = null;
        SuppressNotify.Fired = 0;
        foreach (MethodBase target in written) {
            ForkRuntime.Restore(target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ASuppressedHarmonyPatchNeverLandsAndLeavesTheBodyWritable() {
        MethodInfo target = typeof(SuppressedVictims).GetMethod(nameof(SuppressedVictims.Suppressed))!;
        SuppressNotify.Watched = target;
        InstallHook();

        harmony.Patch(target, postfix: new HarmonyMethod(Shifter));

        Assert.Equal(1, SuppressNotify.Fired);
        Assert.Equal(1, SuppressedVictims.Suppressed());

        Write(target, 5);

        Assert.Equal(5, SuppressedVictims.Suppressed());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AnUnsuppressedPatchStillPoisonsSoTheProbeIsNotVacuous() {
        MethodInfo target = typeof(SuppressedVictims).GetMethod(nameof(SuppressedVictims.Unsuppressed))!;
        SuppressNotify.Watched = typeof(SuppressedVictims).GetMethod(nameof(SuppressedVictims.Suppressed))!;
        InstallHook();

        harmony.Patch(target, postfix: new HarmonyMethod(Shifter));

        Assert.Equal(0, SuppressNotify.Fired);
        Assert.Equal(11, SuppressedVictims.Unsuppressed());

        Write(target, 5);

        Assert.Equal(11, SuppressedVictims.Unsuppressed());
    }

    private void InstallHook() {
        MethodInfo updateWrapper = typeof(Harmony).Assembly
            .GetType("HarmonyLib.PatchFunctions")!
            .GetMethod("UpdateWrapper", BindingFlags.NonPublic | BindingFlags.Static)!;

        MethodInfo notify = typeof(SuppressNotify).GetMethod(nameof(SuppressNotify.Head))!;
        IDetourBackend backend = new WaylithDetourBackend(new MonoModDetourBackend());
        hook = backend.ApplyComposed(updateWrapper, [new Injection(notify, new InjectAt.Head(), "probe", 0)]);
    }

    private void Write(MethodBase target, int value) {
        ForkRuntime.Write(target, value);
        written.Add(target);
    }
}
