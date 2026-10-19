using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Harmony.Tests;

[Collection("HarmonySerial")]
public sealed class CapabilityProbeTests
{
#if NET472
    private static readonly string[] Frameworks = { "net472" };
#else
    private static readonly string[] Frameworks = { "net10.0", "net9.0", "net8.0", "net6.0" };
#endif

    [Fact]
    public void ACopyStampedAVersionThisBuildNeverMeasured_ArmsBecauseItCarriesBothMembers()
    {
        List<string> log = new List<string>();

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);
            Assembly unmeasured = Assembly.Load(WithVersion(AssemblyImage.WithFreshModuleId(HarmonyImage()), new Version(2, 4, 1, 0)));
            string stamp = "#" + unmeasured.GetHashCode();

            Assert.Contains("Version=2.4.1.0", unmeasured.FullName);
            Assert.NotNull(unmeasured.GetType("HarmonyLib.PatchFunctions")
                ?.GetMethod("UpdateWrapper", BindingFlags.NonPublic | BindingFlags.Static));
            Assert.True(UpdateWrapperHook.CarriesSharedStateWriterOfAnySignature(unmeasured));

            Assert.True(
                Hooked(unmeasured),
                "a 0Harmony carrying both PatchFunctions.UpdateWrapper and HarmonySharedState.UpdatePatchInfo was refused the notifier detour over its version string: "
                    + string.Join(" | ", log));
            Assert.Contains(log, line => line.StartsWith(CoexistenceLogMarkers.HookInstalled, StringComparison.Ordinal) && line.Contains(stamp));
            Assert.DoesNotContain(log, line => line.StartsWith(CoexistenceLogMarkers.HookUnavailable, StringComparison.Ordinal) && line.Contains(stamp));
        }
        finally
        {
            Disarm();
        }
    }

    [Fact]
    public void ACopyCarryingNoSharedStateWriter_IsRefusedAndTheRefusalNamesThatMember()
    {
        List<string> log = new List<string>();

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);
            Assembly without = Assembly.Load(WithoutMember(AssemblyImage.WithFreshModuleId(HarmonyImage()), "UpdatePatchInfo"));
            string stamp = "#" + without.GetHashCode();

            Assert.NotNull(without.GetType("HarmonyLib.PatchFunctions")
                ?.GetMethod("UpdateWrapper", BindingFlags.NonPublic | BindingFlags.Static));
            Assert.False(
                UpdateWrapperHook.CarriesSharedStateWriterOfAnySignature(without),
                "renaming the UpdatePatchInfo metadata string left the member findable, so this copy measures nothing");

            Assert.False(
                Hooked(without),
                "a 0Harmony carrying no HarmonySharedState.UpdatePatchInfo got the notifier detour, so a rebuild through it would be held with nowhere to write the shared patch state");
            Assert.Contains(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HookUnavailable, StringComparison.Ordinal)
                        && line.Contains(stamp)
                        && line.Contains("HarmonyLib.HarmonySharedState.UpdatePatchInfo"));
        }
        finally
        {
            Disarm();
        }
    }

    [Fact]
    public void ACopyCarryingNoUpdateWrapper_IsRefusedAndTheRefusalNamesThatMember()
    {
        List<string> log = new List<string>();

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);
            Assembly without = Assembly.Load(WithoutMember(AssemblyImage.WithFreshModuleId(HarmonyImage()), "UpdateWrapper"));
            string stamp = "#" + without.GetHashCode();

            Assert.Null(without.GetType("HarmonyLib.PatchFunctions")
                ?.GetMethod("UpdateWrapper", BindingFlags.NonPublic | BindingFlags.Static));
            Assert.True(
                UpdateWrapperHook.CarriesSharedStateWriterOfAnySignature(without),
                "renaming the UpdateWrapper metadata string also took the shared state writer away, so the refusal below could name either member");

            Assert.False(
                Hooked(without),
                "a 0Harmony carrying no PatchFunctions.UpdateWrapper got the notifier detour, so the hook landed on a method that does not exist");
            Assert.Contains(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HookUnavailable, StringComparison.Ordinal)
                        && line.Contains(stamp)
                        && line.Contains("HarmonyLib.PatchFunctions.UpdateWrapper"));
        }
        finally
        {
            Disarm();
        }
    }

    [Theory]
    [InlineData("2.2.2")]
    [InlineData("2.3.3")]
    public void AShippingReleaseWhoseSharedStateWriterIsSignedDifferently_StillSatisfiesTheProbe(string package)
    {
        Assembly release = Assembly.Load(AssemblyImage.WithFreshModuleId(File.ReadAllBytes(ReleasePath(package))));
        Type sharedState = release.GetType("HarmonyLib.HarmonySharedState")
                           ?? throw new InvalidOperationException("Harmony " + package + " carries no HarmonyLib.HarmonySharedState");

        Assert.Null(sharedState.GetMethod(
            "UpdatePatchInfo",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(MethodBase), typeof(MethodInfo), typeof(byte[]) },
            null));
        Assert.True(
            UpdateWrapperHook.CarriesSharedStateWriterOfAnySignature(release),
            "Harmony " + package + " ships HarmonySharedState.UpdatePatchInfo under a different signature than the 3.0 this repo compiles against, and the probe refused it, so RimWorld 1.4 and 1.5 lose coexistence");
    }

    private static byte[] HarmonyImage()
    {
        return File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location);
    }

    private static byte[] WithoutMember(byte[] image, string name)
    {
        byte[] pattern = new byte[name.Length + 1];
        for (int i = 0; i < name.Length; i++)
        {
            pattern[i] = (byte)name[i];
        }

        int hit = -1;
        for (int i = 0; i <= image.Length - pattern.Length; i++)
        {
            if (Matches(image, i, pattern))
            {
                Assert.True(hit < 0, "the metadata string '" + name + "' appears more than once, so renaming it is ambiguous");
                hit = i;
            }
        }

        Assert.True(hit >= 0, "the metadata string '" + name + "' was not found in the 0Harmony image");
        image[hit] = (byte)'Z';
        return image;
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

    private static string ReleasePath(string package)
    {
        string lib = Path.Combine(PackageRoot(), "lib.harmony", package, "lib");
        foreach (string framework in Frameworks)
        {
            string dll = Path.Combine(lib, framework, "0Harmony.dll");
            if (File.Exists(dll))
            {
                return dll;
            }
        }

        throw new FileNotFoundException("Lib.Harmony " + package + " carries none of " + string.Join(", ", Frameworks) + " under " + lib
                                        + ". The test project downloads it, so run a restore.");
    }

    private static string PackageRoot()
    {
        foreach (AssemblyMetadataAttribute metadata in typeof(CapabilityProbeTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (metadata.Key == "NuGetPackageRoot" && !string.IsNullOrWhiteSpace(metadata.Value))
            {
                return metadata.Value!;
            }
        }

        throw new InvalidOperationException("the test assembly carries no NuGetPackageRoot metadata, so the Harmony releases on disk cannot be found");
    }

    private static void Disarm()
    {
        HarmonyArrival.Reset();
        SuppressionGate.ClearProbes();
        UpdateWrapperHook.DetachObserver();
    }
}
