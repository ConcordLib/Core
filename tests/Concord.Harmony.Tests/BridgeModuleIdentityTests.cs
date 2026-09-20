using System;
using System.IO;
using System.Reflection;
using Concord.Detour;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

// Pins the reason the adapter rewrites the module id before it loads the bridge. Harmony stores a patch
// as (module id, token), so two copies sharing one id make the newer copy unaddressable.
[Collection("HarmonySerial")]
public class BridgeModuleIdentityTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

    [Fact]
    public void AStoredTranspiler_ResolvesBackIntoTheCopyItCameFrom()
    {
        byte[] image = File.ReadAllBytes(typeof(HarmonyBridge).Assembly.Location);
        Assembly first = Assembly.Load(AssemblyImage.WithFreshModuleId(image));
        Assembly second = Assembly.Load(AssemblyImage.WithFreshModuleId(image));

        MethodInfo transpiler = Participant(second);
        Assert.NotSame(Participant(first), transpiler);

        PatchInfo info = new PatchInfo();
#pragma warning disable CS0618
        info.AddTranspiler(transpiler, "concord.bridge", Priority.Last, null, null, false);
#pragma warning restore CS0618

        Assert.Same(transpiler, RoundTrip(info).transpilers[0].PatchMethod);
    }

    private static MethodInfo Participant(Assembly bridge)
    {
        return bridge.GetType("Concord.Harmony.TranspilerParticipant").GetMethod("Transpile", Hidden);
    }

    private static PatchInfo RoundTrip(PatchInfo info)
    {
        Type serialization = typeof(HarmonyLib.Harmony).Assembly.GetType("HarmonyLib.PatchInfoSerialization");
        object bytes = serialization.GetMethod("Serialize", Hidden).Invoke(null, new object[] { info });
        return (PatchInfo)serialization.GetMethod("Deserialize", Hidden).Invoke(null, new[] { bytes });
    }
}
