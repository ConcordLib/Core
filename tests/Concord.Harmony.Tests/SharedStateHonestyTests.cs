using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

public static class SharedStateHonestyTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute()
    {
        return 61;
    }
}

public static class SharedStateHonestyBuiltTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute(int seed)
    {
        return seed + 1;
    }
}

public static class SharedStateHonestyMods
{
    public static void ConcordHead(ControlHandle<int> ch)
    {
        ch.ReturnValue = 99;
        ch.Cancel();
    }

    public static bool RefusedPrefix(int seed, ref int __result)
    {
        __result = 42;
        return false;
    }

    public static bool PlainPrefix(ref int __result)
    {
        __result = 43;
        return false;
    }
}

[Collection("HarmonySerial")]
public sealed class SharedStateHonestyTests
{
    private const string ModId = "test.sharedstate.mod";

    [Fact]
    public void CancelledRebuild_ThenUnpatch_LeavesHarmonysOwnApiHonest()
    {
        MethodInfo target = Method(typeof(SharedStateHonestyTarget), nameof(SharedStateHonestyTarget.Compute));
        Assert.Null(PatchProcessor.GetPatchInfo(target));

        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider(target);
        IDetourHandle? concord = null;

        try
        {
            concord = backend.ApplyComposed(target, new[] { ConcordHead() });
            SuppressionGate.WireProfiler(() => false);
            SuppressionGate.WireCapability(() => null);
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record), string.Join(" | ", decider.Lines));

            MethodInfo handedBack = harmony.Patch(target);

            Assert.Same(OriginalStubs.For(target), handedBack);
            Patches reported = PatchProcessor.GetPatchInfo(target);
            Assert.NotNull(reported);
            Assert.Empty(reported.Owners);
            Assert.Contains(target, new List<MethodBase>(HarmonyLib.Harmony.GetAllPatchedMethods()));
            Assert.Same(target, HarmonyLib.Harmony.GetOriginalMethod(handedBack));
            Assert.Equal(99, SharedStateHonestyTarget.Compute());

            harmony.Unpatch(target, HarmonyPatchType.All, ModId);

            Assert.Equal(new[] { (MethodBase)target, target }, decider.Asked);
            Assert.NotNull(PatchProcessor.GetPatchInfo(target));
            Assert.Contains(target, new List<MethodBase>(HarmonyLib.Harmony.GetAllPatchedMethods()));
            Assert.Same(target, HarmonyLib.Harmony.GetOriginalMethod(OriginalStubs.For(target)));
            Assert.Equal(99, SharedStateHonestyTarget.Compute());
        }
        finally
        {
            UpdateWrapperHook.DetachObserver();
            SuppressionGate.ClearProbes();
            concord?.Dispose();
            TestUnpatch.Own(harmony, ModId);
        }
    }

    [Fact]
    public void OnceHarmonyBuiltTheWrapper_LaterRebuildsAreNeverSuppressed()
    {
        MethodInfo target = Method(typeof(SharedStateHonestyBuiltTarget), nameof(SharedStateHonestyBuiltTarget.Compute));
        Assert.Null(PatchProcessor.GetPatchInfo(target));

        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider(target);
        IDetourHandle? concord = null;

        try
        {
            concord = backend.ApplyComposed(target, new[] { ConcordHead() });
            SuppressionGate.WireProfiler(() => false);
            SuppressionGate.WireCapability(() => null);
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record), string.Join(" | ", decider.Lines));

            MethodInfo refused = harmony.Patch(target, prefix: new HarmonyMethod(Method(typeof(SharedStateHonestyMods), nameof(SharedStateHonestyMods.RefusedPrefix))));
            Assert.NotSame(OriginalStubs.For(target), refused);
            Assert.Single(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, System.StringComparison.Ordinal));

            MethodInfo again = harmony.Patch(target, prefix: new HarmonyMethod(Method(typeof(SharedStateHonestyMods), nameof(SharedStateHonestyMods.PlainPrefix))));

            Assert.NotSame(OriginalStubs.For(target), again);
            Assert.Equal(new[] { (MethodBase)target }, decider.Asked);
            string blocked = Assert.Single(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressBlockedMarker, System.StringComparison.Ordinal));
            Assert.Contains("already built a wrapper for SharedStateHonestyBuiltTarget.Compute", blocked);
            Assert.Equal(42, SharedStateHonestyBuiltTarget.Compute(1));
        }
        finally
        {
            UpdateWrapperHook.DetachObserver();
            SuppressionGate.ClearProbes();
            concord?.Dispose();
            TestUnpatch.Own(harmony, ModId);
        }
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static Injection ConcordHead()
    {
        return new Injection(
            Method(typeof(SharedStateHonestyMods), nameof(SharedStateHonestyMods.ConcordHead)),
            new InjectAt.Head(),
            "test.sharedstate.concord",
            0);
    }

    private sealed class Decider : IForeignPatchObserver, IForeignPatchDecider
    {
        private readonly MethodBase owned;

        public Decider(MethodBase owned)
        {
            this.owned = owned;
        }

        public List<MethodBase> Asked { get; } = new List<MethodBase>();

        public List<string> Lines { get; } = new List<string>();

        public void Record(string line)
        {
            Lines.Add(line);
        }

        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
        }

        public bool TrySuppressForeignRebuild(MethodBase target, object hostPatchState)
        {
            Asked.Add(target);
            return target == owned;
        }
    }
}
