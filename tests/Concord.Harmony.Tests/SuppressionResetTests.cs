using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

[Collection("HarmonySerial")]
public sealed class SuppressionResetTests
{
    private const string ModId = "test.suppressionreset.mod";

    private const string ConcordOwner = "test.suppressionreset.concord";

    private const string Seam = "SetCurrentFactory";

    [Fact]
    public void ARefusedRebuildAfterAComposedOne_DropsTheComposedHandle_SoTheFirstPatchStopsRunning()
    {
        Assert.True(SuppressionIsSafeHere(), HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));

        MethodInfo target = Fresh(typeof(StaleHandleTarget), nameof(StaleHandleTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        MonoModDetourBackend backend = new MonoModDetourBackend();
        Decider decider = new Decider();
        decider.Own(target);

        IDetourHandle? concord = null;
        try
        {
            WireGate();
            concord = backend.ApplyComposed(target, new[] { HeadOf(nameof(ResetMods.Head)) });
            Assert.True(UpdateWrapperHook.TryInstall(decider, backend, decider.Record));

            harmony.Patch(target, prefix: MethodOf(nameof(ResetMods.PlainPrefix)));
            Assert.True(
                ComposedHolds(target),
                "patch A never composed, so the refusal that follows measures nothing: " + string.Join(" | ", decider.Lines));
            Assert.Equal(2, Patcher.OwnersOf(target).Count);

            harmony.Patch(target, prefix: MethodOf(nameof(ResetMods.ArgumentPrefix)));

            string refused = Assert.Single(decider.Lines, line => line.StartsWith(UpdateWrapperHook.ComposeRefusedMarker, StringComparison.Ordinal));
            Assert.Contains("seed", refused);
            Assert.False(
                ComposedHolds(target),
                "the refused rebuild left the previously composed handle applied, so patch A still sits in Concord's body under Harmony's wrapper");
            Assert.Equal(new[] { ConcordOwner }, Patcher.OwnersOf(target));

            ResetLog.Entries.Clear();
            StaleHandleTarget.Compute(1);
            Assert.Single(ResetLog.Entries, entry => entry == "A");

            concord.Dispose();
            concord = null;
            Assert.False(
                backend.OwnsBody(target),
                "Concord still holds a body on the target after its own handle went away, so the stale composed handle can never be released");
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
    public void ACopyStampedMeasuredWithoutTheSeam_IsRefusedOnTheLateCopyArmingPath()
    {
        List<string> log = new List<string>();

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);

            Assembly faked = Assembly.Load(WithoutSeam(AssemblyImage.WithFreshModuleId(HarmonyImage())));

            Assert.NotNull(faked.GetType("HarmonyLib.Harmony"));
            Assert.Equal(typeof(HarmonyLib.Harmony).Assembly.GetName().Version, faked.GetName().Version);
            Assert.Null(faked.GetType("MonoMod.Core.DetourFactory")
                ?.GetMethod(Seam, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

            Assert.True(HarmonyArrival.IsArmed(faked));
            Assert.False(
                Hooked(faked),
                "a 0Harmony stamped with a measured version but carrying no MonoMod seam got the notifier detour from the late-copy arming path");
            Assert.Contains(log, line => line.Contains("MonoMod.Core.DetourFactory." + Seam));
        }
        finally
        {
            Disarm();
        }
    }

    [Fact]
    public void AfterReset_AHarmonyCopyThatLoadsIsNotArmed()
    {
        List<string> log = new List<string>();

        try
        {
            HarmonyArrival.Install(null, new MonoModDetourBackend(), log.Add, null, null);
            HarmonyArrival.Reset();

            Assembly after = Assembly.Load(AssemblyImage.WithFreshModuleId(HarmonyImage()));
            string stamp = "#" + after.GetHashCode();

            Assert.False(HarmonyArrival.IsArmed(after));
            Assert.False(
                Hooked(after),
                "a 0Harmony that loaded after HarmonyArrival.Reset still got the notifier detour, so Reset did not stop arming");
            Assert.DoesNotContain(log, line => line.Contains(stamp));
        }
        finally
        {
            Disarm();
        }
    }

    private static bool SuppressionIsSafeHere()
    {
        return HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()) == null;
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static TValue StaticValue<TValue>(Type owner, string name)
        where TValue : class
    {
        FieldInfo field = owner.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(owner.Name + "." + name + " not found");
        return field.GetValue(null) as TValue
            ?? throw new InvalidOperationException(owner.Name + "." + name + " was null or not a " + typeof(TValue).Name);
    }

    private static MethodInfo Fresh(Type owner, string name)
    {
        MethodInfo target = Method(owner, name);
        Assert.Null(PatchProcessor.GetPatchInfo(target));
        return target;
    }

    private static void WireGate()
    {
        SuppressionGate.WireProfiler(() => false);
        SuppressionGate.WireCapability(() => null);
    }

    private static void Disarm()
    {
        HarmonyArrival.Reset();
        SuppressionGate.ClearProbes();
        UpdateWrapperHook.DetachObserver();
    }

    private static HarmonyMethod MethodOf(string name)
    {
        return new HarmonyMethod(Method(typeof(ResetMods), name));
    }

    private static Injection HeadOf(string name)
    {
        return new Injection(Method(typeof(ResetMods), name), new InjectAt.Head(), ConcordOwner, 0);
    }

    private static byte[] HarmonyImage()
    {
        return File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location);
    }

    private static byte[] WithoutSeam(byte[] image)
    {
        byte[] pattern = new byte[Seam.Length + 1];
        for (int i = 0; i < Seam.Length; i++)
        {
            pattern[i] = (byte)Seam[i];
        }

        int hit = -1;
        for (int i = 0; i <= image.Length - pattern.Length; i++)
        {
            if (Matches(image, i, pattern))
            {
                Assert.True(hit < 0, "the metadata string '" + Seam + "' appears more than once, so renaming it is ambiguous");
                hit = i;
            }
        }

        Assert.True(hit >= 0, "the metadata string '" + Seam + "' was not found in the 0Harmony image");
        image[hit] = (byte)'Z';
        return image;
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
        Dictionary<Assembly, IDetourHandle> handles =
            StaticValue<Dictionary<Assembly, IDetourHandle>>(typeof(UpdateWrapperHook), "HookHandles");
        lock (handles)
        {
            return handles.ContainsKey(copy);
        }
    }

    private static bool ComposedHolds(MethodBase target)
    {
        Dictionary<MethodBase, IDetourHandle> composed =
            StaticValue<Dictionary<MethodBase, IDetourHandle>>(typeof(UpdateWrapperHook), "ComposedForeign");
        lock (composed)
        {
            return composed.ContainsKey(MethodIdentity.Normalize(target));
        }
    }

    private sealed class Decider : IForeignPatchObserver, IForeignPatchDecider
    {
        private readonly HashSet<MethodBase> owned = new HashSet<MethodBase>();

        public List<string> Lines { get; } = new List<string>();

        public void Own(MethodBase target)
        {
            owned.Add(target);
        }

        public void Record(string line)
        {
            Lines.Add(line);
        }

        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
        }

        public bool TrySuppressForeignRebuild(MethodBase target, object hostPatchState)
        {
            return owned.Contains(target);
        }
    }

    public static class ResetLog
    {
        public static List<string> Entries { get; } = new List<string>();
    }

    public static class StaleHandleTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            ResetLog.Entries.Add("body");
            return seed + 5;
        }
    }

    public static class ResetMods
    {
        public static void Head(ControlHandle<int> ch)
        {
            ResetLog.Entries.Add("concord");
        }

        public static void PlainPrefix()
        {
            ResetLog.Entries.Add("A");
        }

        public static void ArgumentPrefix(ref int seed)
        {
            ResetLog.Entries.Add("B");
        }
    }
}
