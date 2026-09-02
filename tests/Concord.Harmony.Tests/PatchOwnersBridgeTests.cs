using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

public static class OwnersBridgeTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute()
    {
        return 4;
    }
}

public static class OwnersBridgeMods
{
    public static void ConcordHead(ControlHandle ch)
    {
    }

    public static void ForeignPrefix()
    {
    }
}

// Serialized with the rest of the Harmony tests: the notifier hook and Harmony's own patch state are
// process-wide, so these cannot run alongside another test that patches.
[Collection("HarmonySerial")]
public class PatchOwnersBridgeTests
{
    [Fact]
    public void OwnersOf_StillNamesTheOwner_AfterTheTargetMovesOntoTheBridge()
    {
        MethodBase target = typeof(OwnersBridgeTarget).GetMethod(nameof(OwnersBridgeTarget.Compute));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.owners");

        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), Noop);
        HarmonyBridge bridge = new HarmonyBridge(Noop);

        try
        {
            router.ActivateHost(bridge);

            using IDetourHandle handle = router.ApplyComposed(target, new[] { Head() });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));
            Assert.Equal(new[] { "test.concord.owners" }, Patcher.OwnersOf(target));

            foreign.Patch(
                target,
                prefix: new HarmonyMethod(typeof(OwnersBridgeMods).GetMethod(nameof(OwnersBridgeMods.ForeignPrefix))));

            // The injections now live in Harmony's wrapper, so only the bridge can still name the owner.
            Assert.Equal(RouteState.Bridge, router.GetRoute(target));
            Assert.Equal(new[] { "test.concord.owners" }, Patcher.OwnersOf(target));
        }
        finally
        {
            foreign.UnpatchAll("test.foreign.owners");
            UpdateWrapperHook.Uninstall();
        }
    }

    private static void Noop(string message)
    {
    }

    private static Injection Head()
    {
        return new Injection(
            typeof(OwnersBridgeMods).GetMethod(nameof(OwnersBridgeMods.ConcordHead)),
            new InjectAt.Head(),
            "test.concord.owners",
            0);
    }
}
