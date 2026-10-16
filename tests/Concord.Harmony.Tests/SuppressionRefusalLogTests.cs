using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

public static class RefusalLogTargets
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NoDecider()
    {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Declined()
    {
        return 1;
    }
}

public static class RefusalLogMods
{
    public static void Prefix()
    {
    }
}

[Collection("HarmonySerial")]
public sealed class SuppressionRefusalLogTests
{
    private const string ModId = "test.refusal.mod";

    [Fact]
    public void AnObserverThatIsNotADecider_SaysSoInTheLog()
    {
        MethodInfo target = Fresh(nameof(RefusalLogTargets.NoDecider));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        List<string> lines = new List<string>();

        try
        {
            Arm(new PlainObserver(), lines);

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(RefusalLogMods).GetMethod(nameof(RefusalLogMods.Prefix))));

            Assert.Contains(lines, line => line.StartsWith(UpdateWrapperHook.SuppressBlockedMarker, StringComparison.Ordinal)
                                           && line.Contains("no suppression decider is wired"));
        }
        finally
        {
            Disarm(harmony);
        }
    }

    [Fact]
    public void ADeciderThatDeclinesTheTarget_SaysSoInTheLog()
    {
        MethodInfo target = Fresh(nameof(RefusalLogTargets.Declined));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        List<string> lines = new List<string>();

        try
        {
            Arm(new RefusingDecider(), lines);

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(RefusalLogMods).GetMethod(nameof(RefusalLogMods.Prefix))));

            Assert.Contains(lines, line => line.StartsWith(UpdateWrapperHook.SuppressBlockedMarker, StringComparison.Ordinal)
                                           && line.Contains("the router declined to hold this target"));
        }
        finally
        {
            Disarm(harmony);
        }
    }

    private static MethodInfo Fresh(string name)
    {
        MethodInfo target = typeof(RefusalLogTargets).GetMethod(name)
                            ?? throw new InvalidOperationException(nameof(RefusalLogTargets) + "." + name + " not found");
        Assert.Null(PatchProcessor.GetPatchInfo(target));
        return target;
    }

    private static void Arm(IForeignPatchObserver observer, List<string> lines)
    {
        SuppressionGate.WireProfiler(() => false);
        SuppressionGate.WireCapability(() => null);
        Assert.True(UpdateWrapperHook.TryInstall(observer, new MonoModDetourBackend(), lines.Add));
    }

    private static void Disarm(HarmonyLib.Harmony harmony)
    {
        UpdateWrapperHook.DetachObserver();
        SuppressionGate.ClearProbes();
        TestUnpatch.Own(harmony, ModId);
    }

    private sealed class PlainObserver : IForeignPatchObserver
    {
        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
        }
    }

    private sealed class RefusingDecider : IForeignPatchObserver, IForeignPatchDecider
    {
        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
        }

        public bool TrySuppressForeignRebuild(MethodBase target, object hostPatchState)
        {
            return false;
        }
    }
}
