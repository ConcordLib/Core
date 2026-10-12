using System;
using System.IO;
using System.Reflection;
using Concord.Detour;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

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
        return Method(TypeIn(bridge, "Concord.Harmony.TranspilerParticipant"), "Transpile");
    }

    private static PatchInfo RoundTrip(PatchInfo info)
    {
        Type serialization = TypeIn(typeof(HarmonyLib.Harmony).Assembly, "HarmonyLib.PatchInfoSerialization");
        object bytes = Invoke(serialization, "Serialize", info);
        return (PatchInfo)Invoke(serialization, "Deserialize", bytes);
    }

    private static Type TypeIn(Assembly assembly, string name)
    {
        return assembly.GetType(name) ?? throw new InvalidOperationException(name + " not found");
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name, Hidden) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static object Invoke(Type type, string name, object argument)
    {
        return Method(type, name).Invoke(null, new[] { argument })
            ?? throw new InvalidOperationException(type.Name + "." + name + " returned null");
    }
}
