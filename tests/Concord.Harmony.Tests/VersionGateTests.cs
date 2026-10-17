using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Harmony.Tests;

public sealed class VersionGateTests
{
    [Theory]
    [InlineData("2.4.2.0")]
    [InlineData("2.4.3.0")]
    [InlineData("3.0.0.0")]
    [InlineData("3.0.1.0")]
    [InlineData("3.0.17.4")]
    public void GateAdmitsAMeasuredRelease(string version)
    {
        Assert.Null(SupportMatrix.BackstopRefusal(version));
    }

    [Theory]
    [InlineData("2.4.1.0")]
    [InlineData("2.5.0.0")]
    [InlineData("3.1.0.0")]
    [InlineData("4.0.0.0")]
    public void GateCallsAnUnmeasuredReleaseUnchecked_NotRefused(string version)
    {
        string? refusal = SupportMatrix.BackstopRefusal(version);

        Assert.NotNull(refusal);
        Assert.Contains("is unchecked", refusal);
        Assert.DoesNotContain("is refused", refusal);
        Assert.Contains("stays asleep", refusal);
    }

    [Theory]
    [InlineData("2.2.2.0", "MonoMod.Core.DetourFactory")]
    [InlineData("2.3.3.0", "MonoMod.Core.DetourFactory.SetCurrentFactory")]
    [InlineData("2.3.6.0", "MonoMod.Core.DetourFactory.SetCurrentFactory")]
    public void GateRefusesAMeasuredReleaseThatLacksTheDetourFactorySeam(string version, string lacking)
    {
        string? refusal = SupportMatrix.BackstopRefusal(version);

        Assert.NotNull(refusal);
        Assert.Contains("is refused", refusal);
        Assert.Contains(lacking, refusal);
        Assert.Contains("stays asleep", refusal);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("2.4")]
    public void GateRefusesAVersionTextItCannotRead(string? version)
    {
        string? refusal = SupportMatrix.BackstopRefusal(version);

        Assert.NotNull(refusal);
        Assert.Contains("does not parse", refusal);
        Assert.DoesNotContain("is unchecked", refusal);
        Assert.DoesNotContain("is refused", refusal);
    }

    [Fact]
    public void VersionTextMatchesTheLoadedHarmonyAssemblyVersion()
    {
        string expected = HarmonyVersion().ToString();
        Assert.Equal(expected, UpdateWrapperHook.HarmonyVersionText());
    }

    [Fact]
    public void TheHarmonyThisRunLoadedIsAMeasuredRelease_AndTheGateAdmitsIt()
    {
        Assert.Null(SupportMatrix.BackstopRefusal());
    }

    [Fact]
    public void ShapeCheckAdmitsTheRealHarmonyAndRefusesACopyStampedMeasuredWithoutTheMonoModSeam()
    {
        Assert.Null(SupportMatrix.BackstopRefusal("3.0.0.0", typeof(HarmonyLib.Harmony).Assembly));

        string? refusal = SupportMatrix.BackstopRefusal("3.0.0.0", typeof(SupportMatrix).Assembly);

        Assert.NotNull(refusal);
        Assert.Contains("MonoMod.Core.DetourFactory.SetCurrentFactory", refusal);
        Assert.Contains("stays asleep", refusal);
    }

    [Fact]
    public void ShapeCheckIsSkippedWhenNoCopyIsSupplied()
    {
        Assert.Null(SupportMatrix.BackstopRefusal("3.0.0.0", null));
    }

    private static Version HarmonyVersion()
    {
        return typeof(HarmonyLib.Harmony).Assembly.GetName().Version
               ?? throw new InvalidOperationException("the loaded HarmonyLib assembly name carries no version");
    }
}

[Collection("HarmonySerial")]
public sealed class VersionGateArrivalTests
{
    [Fact]
    public void AnUncheckedNewerHarmonyArrives_StaysUnhooked_AndALaterMeasuredCopyStillArms()
    {
        List<string> log = new List<string>();

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);

            Assembly newer = Assembly.Load(WithVersion(AssemblyImage.WithFreshModuleId(HarmonyImage()), new Version(4, 0, 0, 0)));

            Assert.Contains("Version=4.0.0.0", newer.FullName);
            Assert.True(HarmonyArrival.IsArmed(newer));
            Assert.False(Hooked(newer), "a 0Harmony no version of this build measured got a notifier detour");
            Assert.Contains(log, line => line.Contains("Harmony 4.0.0.0 is unchecked"));

            string newerStamp = " #" + newer.GetHashCode() + " ";
            Assert.Contains(log, line => line.StartsWith(HarmonyArrival.ArmFailedMarker, StringComparison.Ordinal) && line.Contains(newerStamp));

            Assembly measured = Assembly.Load(AssemblyImage.WithFreshModuleId(HarmonyImage()));

            Assert.True(Hooked(measured), "a measured 0Harmony loading after an unchecked one did not get a notifier detour, so one unchecked copy turned the hook off for every later copy");
            Assert.Contains(log, line => line.StartsWith(HarmonyArrival.ArmedMarker, StringComparison.Ordinal) && line.Contains(" #" + measured.GetHashCode() + " "));
        }
        finally
        {
            HarmonyArrival.Reset();
            SuppressionGate.ClearProbes();
            UpdateWrapperHook.DetachObserver();
        }
    }

    private static byte[] HarmonyImage()
    {
        return File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location);
    }

    private static byte[] WithVersion(byte[] image, Version to)
    {
        Version from = typeof(HarmonyLib.Harmony).Assembly.GetName().Version
                       ?? throw new InvalidOperationException("the loaded HarmonyLib assembly name carries no version");
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
}
