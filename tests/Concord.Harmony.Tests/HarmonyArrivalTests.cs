using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Harmony.Tests;

[Collection("HarmonySerial")]
public sealed class HarmonyArrivalTests
{
    [Fact]
    public void Install_ThenASecondHarmonyLoads_ArmsInsideThatLoadOnTheLoadingThread()
    {
        List<string> log = new List<string>();
        List<Arrival> seen = new List<Arrival>();
        AssemblyLoadEventHandler probe = (_, args) => seen.Add(new Arrival(
            args.LoadedAssembly,
            HarmonyArrival.IsArmed(args.LoadedAssembly),
            Environment.CurrentManagedThreadId));

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);
            AppDomain.CurrentDomain.AssemblyLoad += probe;

            Assembly second = Assembly.Load(AssemblyImage.WithFreshModuleId(HarmonyImage()));

            Arrival arrival = Assert.Single(seen, s => s.Loaded == second);
            Assert.True(arrival.ArmedInsideLoad, "the second 0Harmony was not armed by the time the probe subscribed after Install saw it, so the arm did not land inside the load");
            Assert.Equal(Environment.CurrentManagedThreadId, arrival.Thread);
            Assert.Contains(log, line => line.StartsWith(HarmonyArrival.ArmedMarker, StringComparison.Ordinal) && line.Contains("#" + second.GetHashCode()));
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyLoad -= probe;
            Disarm();
        }
    }

    [Fact]
    public void Install_WithHarmonyAlreadyLoaded_ArmsItAtOnceAndOnlyOnce()
    {
        Assembly present = typeof(HarmonyLib.Harmony).Assembly;
        List<string> log = new List<string>();

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);
            Assert.True(HarmonyArrival.IsArmed(present));
            int afterFirst = HarmonyArrival.ArmedCount;

            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);
            Assembly notHarmony = Assembly.Load(AssemblyImage.WithFreshModuleId(File.ReadAllBytes(typeof(HarmonyArrival).Assembly.Location)));

            Assert.Equal(afterFirst, HarmonyArrival.ArmedCount);
            Assert.False(HarmonyArrival.IsArmed(notHarmony));
            Assert.Single(log, line => line.StartsWith(HarmonyArrival.ArmedMarker, StringComparison.Ordinal) && line.Contains("#" + present.GetHashCode()));
        }
        finally
        {
            Disarm();
        }
    }

    [Fact]
    public void AHarmonyThisBuildNeverMeasured_ArrivesAndGetsHooked()
    {
        List<string> log = new List<string>();

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);
            Assembly old = Assembly.Load(WithVersion(AssemblyImage.WithFreshModuleId(HarmonyImage()), new Version(2, 4, 1, 0)));

            Assert.Contains("Version=2.4.1.0", old.FullName);
            Assert.True(HarmonyArrival.IsArmed(old));
            Assert.True(Hooked(old), "a 0Harmony carrying both probed members did not get a notifier detour");
            Assert.Contains(log, line => line.StartsWith(CoexistenceLogMarkers.HookInstalled, StringComparison.Ordinal) && line.Contains("2.4.1.0") && line.Contains("#" + old.GetHashCode()));

            string stamp = " #" + old.GetHashCode() + " ";
            Assert.Contains(log, line => line.StartsWith(HarmonyArrival.ArmedMarker, StringComparison.Ordinal) && line.Contains(stamp));
            Assert.DoesNotContain(log, line => line.StartsWith(HarmonyArrival.ArmFailedMarker, StringComparison.Ordinal) && line.Contains(stamp));
        }
        finally
        {
            Disarm();
        }
    }

    [Fact]
    public void Install_WithBothProbes_ArmsTheGate_AndWithoutThemLeavesItRefusing()
    {
        MethodBase target = Method(typeof(HarmonyArrivalTests), nameof(PlainTarget));

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), _ => { }, null, null);
            Assert.Equal(SuppressionReason.ProfilerUnwired, SuppressionGate.Validate(target));

            HarmonyArrival.Install(null, new MonoModDetourBackend(), _ => { }, () => null, () => false);
            Assert.Null(SuppressionGate.Validate(target));
        }
        finally
        {
            Disarm();
        }
    }

#if NET
    [Fact]
    public void AHandlerThatThrows_IsSwallowedByCoreClr_AndTheLoadStillCompletes()
    {
        bool ran = false;
        AssemblyLoadEventHandler thrower = (_, _) =>
        {
            ran = true;
            throw new InvalidOperationException("handler threw");
        };

        AppDomain.CurrentDomain.AssemblyLoad += thrower;
        try
        {
            Assembly loaded = Assembly.Load(AssemblyImage.WithFreshModuleId(HarmonyImage()));

            Assert.True(ran, "the throwing handler never ran, so this measured nothing");
            Assert.NotNull(loaded);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyLoad -= thrower;
        }
    }
#endif

    public static int PlainTarget()
    {
        return 1;
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static byte[] HarmonyImage()
    {
        return File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location);
    }

    private static byte[] WithVersion(byte[] image, Version to)
    {
        Version from = typeof(HarmonyLib.Harmony).Assembly.GetName().Version
                       ?? throw new InvalidOperationException("the running 0Harmony has no assembly version");
        byte[] row = VersionRow(0x8004, from);
        int hit = -1;
        for (int i = 0; i <= image.Length - row.Length; i++)
        {
            if (Matches(image, i, row))
            {
                Assert.True(hit < 0, "the Assembly table version row pattern matched more than once");
                hit = i;
            }
        }

        Assert.True(hit >= 0, "the Assembly table version row for " + from + " was not found in the image");
        VersionRow(0x8004, to).CopyTo(image, hit);
        return image;
    }

    private static byte[] VersionRow(int hashAlgId, Version version)
    {
        byte[] row = new byte[12];
        BitConverter.GetBytes(hashAlgId).CopyTo(row, 0);
        BitConverter.GetBytes((ushort)version.Major).CopyTo(row, 4);
        BitConverter.GetBytes((ushort)version.Minor).CopyTo(row, 6);
        BitConverter.GetBytes((ushort)version.Build).CopyTo(row, 8);
        BitConverter.GetBytes((ushort)version.Revision).CopyTo(row, 10);
        return row;
    }

    private static bool Matches(byte[] image, int offset, byte[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (image[offset + i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Hooked(Assembly copy)
    {
        FieldInfo field = typeof(UpdateWrapperHook).GetField("HookHandles", BindingFlags.NonPublic | BindingFlags.Static)
                          ?? throw new InvalidOperationException("UpdateWrapperHook.HookHandles not found");
        Dictionary<Assembly, IDetourHandle> handles = (Dictionary<Assembly, IDetourHandle>)(field.GetValue(null)
            ?? throw new InvalidOperationException("UpdateWrapperHook.HookHandles read back null"));
        lock (handles)
        {
            return handles.ContainsKey(copy);
        }
    }

    private static void Disarm()
    {
        HarmonyArrival.Reset();
        SuppressionGate.ClearProbes();
        UpdateWrapperHook.DetachObserver();
    }

    private sealed class Arrival
    {
        public Arrival(Assembly loaded, bool armedInsideLoad, int thread)
        {
            Loaded = loaded;
            ArmedInsideLoad = armedInsideLoad;
            Thread = thread;
        }

        public Assembly Loaded { get; }

        public bool ArmedInsideLoad { get; }

        public int Thread { get; }
    }
}
