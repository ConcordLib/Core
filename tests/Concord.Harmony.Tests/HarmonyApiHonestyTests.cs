using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

public static class HonestyPatchInfoTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute()
    {
        return 11;
    }
}

public static class HonestyPatchedMethodsTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute()
    {
        return 12;
    }
}

public static class HonestyOriginalSingleTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute()
    {
        return 13;
    }
}

public static class HonestyOriginalPairTargets
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int First()
    {
        return 14;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Second()
    {
        return 15;
    }
}

public static class HonestyUnpatchTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute()
    {
        return 16;
    }
}

public static class HonestyMods
{
    public static void ModPrefix()
    {
    }

    public static void ConcordHead(ControlHandle ch)
    {
    }
}

[Collection("HarmonySerial")]
public sealed class HarmonyApiHonestyTests
{
    private const string ModId = "test.honesty.mod";

    private const string ConcordOwner = "test.honesty.concord";

    private const int ModPriority = 321;

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    [Fact]
    public void GetPatchInfo_WithConcordOwningTheBody_StillReportsTheModsOwnerAndPriority()
    {
        MethodInfo target = Method(typeof(HonestyPatchInfoTarget), nameof(HonestyPatchInfoTarget.Compute));
        AssertFreshTarget(target);

        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        HarmonyBridge bridge = new HarmonyBridge(_ => { });
        ForeignRouteResult? routed = null;

        try
        {
            harmony.Patch(target, prefix: ModPrefix());
            routed = bridge.TryRoute(target, new[] { ConcordHead() }, false);
            Assert.Equal(ForeignRouteKind.Routed, routed.Kind);

            Patches patches = PatchProcessor.GetPatchInfo(target);
            Patch prefix = Assert.Single(patches.Prefixes);

            Assert.Equal(ModId, prefix.owner);
            Assert.Equal(ModPriority, prefix.priority);
            Assert.Equal(
                Method(typeof(HonestyMods), nameof(HonestyMods.ModPrefix)),
                prefix.PatchMethod);
            Assert.Contains(ModId, patches.Owners);
        }
        finally
        {
            routed?.Handle?.Dispose();
            TestUnpatch.Own(harmony, ModId);
        }
    }

    [Fact]
    public void GetPatchedMethods_WithConcordOwningTheBody_StillListsTheTarget()
    {
        MethodInfo target = Method(typeof(HonestyPatchedMethodsTarget), nameof(HonestyPatchedMethodsTarget.Compute));
        AssertFreshTarget(target);

        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        HarmonyBridge bridge = new HarmonyBridge(_ => { });
        ForeignRouteResult? routed = null;

        try
        {
            harmony.Patch(target, prefix: ModPrefix());
            routed = bridge.TryRoute(target, new[] { ConcordHead() }, false);
            Assert.Equal(ForeignRouteKind.Routed, routed.Kind);

            Assert.Contains(target, new List<MethodBase>(harmony.GetPatchedMethods()));
            Assert.Contains(target, new List<MethodBase>(HarmonyLib.Harmony.GetAllPatchedMethods()));
        }
        finally
        {
            routed?.Handle?.Dispose();
            TestUnpatch.Own(harmony, ModId);
        }
    }

    [Fact]
    public void GetOriginalMethod_WithConcordOwningTheBody_ReturnsTheOriginalNotTheReplacement()
    {
        MethodInfo target = Method(typeof(HonestyOriginalSingleTarget), nameof(HonestyOriginalSingleTarget.Compute));
        AssertFreshTarget(target);

        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        HarmonyBridge bridge = new HarmonyBridge(_ => { });
        ForeignRouteResult? routed = null;

        try
        {
            MethodInfo replacement = harmony.Patch(target, prefix: ModPrefix());
            routed = bridge.TryRoute(target, new[] { ConcordHead() }, false);
            Assert.Equal(ForeignRouteKind.Routed, routed.Kind);

            Assert.NotSame(target, replacement);
            Assert.Same(target, HarmonyLib.Harmony.GetOriginalMethod(replacement));
        }
        finally
        {
            routed?.Handle?.Dispose();
            TestUnpatch.Own(harmony, ModId);
        }
    }

    [Fact]
    public void GetOriginalMethod_TwoPatchedMethodsLive_ReturnsTheRightOriginalForEach()
    {
        MethodInfo first = Method(typeof(HonestyOriginalPairTargets), nameof(HonestyOriginalPairTargets.First));
        MethodInfo second = Method(typeof(HonestyOriginalPairTargets), nameof(HonestyOriginalPairTargets.Second));
        AssertFreshTarget(first);
        AssertFreshTarget(second);

        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        HarmonyBridge bridge = new HarmonyBridge(_ => { });
        ForeignRouteResult? firstRoute = null;
        ForeignRouteResult? secondRoute = null;

        try
        {
            MethodInfo firstReplacement = harmony.Patch(first, prefix: ModPrefix());
            MethodInfo secondReplacement = harmony.Patch(second, prefix: ModPrefix());

            firstRoute = bridge.TryRoute(first, new[] { ConcordHead() }, false);
            secondRoute = bridge.TryRoute(second, new[] { ConcordHead() }, false);
            Assert.Equal(ForeignRouteKind.Routed, firstRoute.Kind);
            Assert.Equal(ForeignRouteKind.Routed, secondRoute.Kind);

            Assert.NotSame(firstReplacement, secondReplacement);
            Assert.Same(first, HarmonyLib.Harmony.GetOriginalMethod(firstReplacement));
            Assert.Same(second, HarmonyLib.Harmony.GetOriginalMethod(secondReplacement));
        }
        finally
        {
            firstRoute?.Handle?.Dispose();
            secondRoute?.Handle?.Dispose();
            TestUnpatch.Own(harmony, ModId);
        }
    }

    [Fact]
    public void Unpatch_WithConcordOwningTheBody_RemovesThePatchAndGetPatchInfoStopsReportingIt()
    {
        MethodInfo target = Method(typeof(HonestyUnpatchTarget), nameof(HonestyUnpatchTarget.Compute));
        AssertFreshTarget(target);

        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        HarmonyBridge bridge = new HarmonyBridge(_ => { });
        ForeignRouteResult? routed = null;

        try
        {
            harmony.Patch(target, prefix: ModPrefix());
            routed = bridge.TryRoute(target, new[] { ConcordHead() }, false);
            Assert.Equal(ForeignRouteKind.Routed, routed.Kind);
            Assert.Single(PatchProcessor.GetPatchInfo(target).Prefixes);

            harmony.Unpatch(target, HarmonyPatchType.Prefix, ModId);

            Assert.Empty(PatchProcessor.GetPatchInfo(target).Prefixes);
            Assert.DoesNotContain(ModId, PatchProcessor.GetPatchInfo(target).Owners);
            Assert.DoesNotContain(target, new List<MethodBase>(harmony.GetPatchedMethods()));
        }
        finally
        {
            routed?.Handle?.Dispose();
            TestUnpatch.Own(harmony, ModId);
        }
    }

    private static void AssertFreshTarget(MethodInfo target)
    {
        Assert.Null(PatchProcessor.GetPatchInfo(target));
    }

    private static HarmonyMethod ModPrefix()
    {
        return new HarmonyMethod(Method(typeof(HonestyMods), nameof(HonestyMods.ModPrefix)))
        {
            priority = ModPriority,
        };
    }

    private static Injection ConcordHead()
    {
        return new Injection(
            Method(typeof(HonestyMods), nameof(HonestyMods.ConcordHead)),
            new InjectAt.Head(),
            ConcordOwner,
            0);
    }
}
