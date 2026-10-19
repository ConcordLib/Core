using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Detour;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

[Collection("HarmonySerial")]
public sealed class SuppressionFilterTests
{
    private const string ModId = "test.suppressionfilter.mod";

    [Fact]
    public void Classify_OnTheHarmonyTheSuiteLoaded_SaysSuppressionIsSafe()
    {
        Assert.Null(HarmonyApiHonesty.SuppressionReason(HarmonyApiHonesty.Classify()));
    }

    [Fact]
    public void Classify_AssemblyWithNoPatchFunctions_IsUnknownAndTheRefusalSaysItCouldNotMeasure()
    {
        SharedStateWrite write = HarmonyApiHonesty.Classify(typeof(object).Assembly);

        Assert.Equal(SharedStateWrite.Unknown, write);
        string? reason = HarmonyApiHonesty.SuppressionReason(write);
        Assert.Contains("PatchFunctions.UpdateWrapper", reason);
        Assert.Contains("HarmonySharedState.UpdatePatchInfo", reason);
        Assert.Contains("could not be measured", reason);
    }

    [Fact]
    public void Validate_WithNoProbeWired_RefusesAndPointsAtTheProfilerProbe()
    {
        SuppressionGate.ClearProbes();

        Assert.Equal(SuppressionReason.ProfilerUnwired, SuppressionGate.Validate(PlainTarget()));
    }

    [Fact]
    public void Validate_WithBothProbesWired_PermitsAPlainTarget()
    {
        try
        {
            WireGate(profilerPresent: false, capabilityOff: null);

            Assert.Null(SuppressionGate.Validate(PlainTarget()));
        }
        finally
        {
            SuppressionGate.ClearProbes();
        }
    }

    [Fact]
    public void Validate_WithAProfilerPresent_RefusesNamingTheProfilerFilter()
    {
        try
        {
            WireGate(profilerPresent: true, capabilityOff: null);
            string? refused = SuppressionGate.Validate(PlainTarget());

            Assert.Equal(SuppressionReason.ProfilerPresent, refused);
            Assert.StartsWith(SuppressionReason.ProfilerTag + ":", refused);
        }
        finally
        {
            SuppressionGate.ClearProbes();
        }
    }

    [Fact]
    public void Validate_WithBodyReplacementOff_RefusesNamingTheCapabilityFilter()
    {
        try
        {
            WireGate(profilerPresent: false, capabilityOff: "this host pins method bodies");
            string? refused = SuppressionGate.Validate(PlainTarget());

            Assert.Equal(SuppressionReason.CapabilityOff("this host pins method bodies"), refused);
            Assert.StartsWith(SuppressionReason.CapabilityTag + ":", refused);
        }
        finally
        {
            SuppressionGate.ClearProbes();
        }
    }

    [Fact]
    public void Validate_OnAnOpenGenericTarget_RefusesNamingTheGenericFilter()
    {
        try
        {
            WireGate(profilerPresent: false, capabilityOff: null);
            MethodInfo open = Method(typeof(GenericFilterTarget<>), nameof(GenericFilterTarget<string>.Compute));
            string? refused = SuppressionGate.Validate(open);

            Assert.Equal(SuppressionReason.OpenGeneric(open), refused);
            Assert.StartsWith(SuppressionReason.GenericTag + ":", refused);
        }
        finally
        {
            SuppressionGate.ClearProbes();
        }
    }

    [Fact]
    public void Validate_OnAClosedGenericInstantiation_RefusesNamingTheGenericFilter()
    {
        try
        {
            WireGate(profilerPresent: false, capabilityOff: null);
            MethodInfo closed = Method(typeof(GenericFilterTarget<string>), nameof(GenericFilterTarget<string>.Compute));
            string? refused = SuppressionGate.Validate(closed);

            Assert.StartsWith(SuppressionReason.GenericTag + ":", refused);
            Assert.Contains("GenericFilterTarget`1.Compute", refused);
            Assert.NotEqual(SuppressionReason.OpenGeneric(closed), refused);
        }
        finally
        {
            SuppressionGate.ClearProbes();
        }
    }

    [Fact]
    public void AProfilerPresent_LeavesTheRebuildToHarmony()
    {
        MethodInfo target = Fresh(typeof(ProfilerFilterTarget), nameof(ProfilerFilterTarget.Compute));
        AssertHarmonyKeptTheBuild(target, nameof(FilterMods.ProfilerPrefix), profilerPresent: true, capabilityOff: null, expectedTag: SuppressionReason.ProfilerTag);
    }

    [Fact]
    public void BodyReplacementOff_LeavesTheRebuildToHarmony()
    {
        MethodInfo target = Fresh(typeof(CapabilityFilterTarget), nameof(CapabilityFilterTarget.Compute));
        AssertHarmonyKeptTheBuild(target, nameof(FilterMods.CapabilityPrefix), profilerPresent: false, capabilityOff: "no body writes here", expectedTag: SuppressionReason.CapabilityTag);
    }

    [Fact]
    public void AGenericInstantiationTarget_LeavesTheRebuildToHarmony()
    {
        if (TestRuntime.IsNetFramework)
        {
            return;
        }

        MethodInfo target = Fresh(typeof(GenericFilterTarget<string>), nameof(GenericFilterTarget<string>.Compute));
        AssertHarmonyKeptTheBuild(target, nameof(FilterMods.GenericPrefix), profilerPresent: false, capabilityOff: null, expectedTag: SuppressionReason.GenericTag);
    }

    [Fact]
    public void TwoRefusalsWithDifferentReasons_BothReachTheLog()
    {
        if (TestRuntime.IsNetFramework)
        {
            return;
        }

        MethodInfo first = Fresh(typeof(GenericFilterTarget<object>), nameof(GenericFilterTarget<object>.Compute));
        MethodInfo second = Fresh(typeof(SecondGenericFilterTarget<object>), nameof(SecondGenericFilterTarget<object>.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        AlwaysSuppress decider = new AlwaysSuppress();

        try
        {
            WireGate(profilerPresent: false, capabilityOff: null);
            Assert.True(UpdateWrapperHook.TryInstall(decider, new MonoModDetourBackend(), decider.Record));
            FilterLog.Entries.Clear();

            harmony.Patch(first, prefix: MethodOf(nameof(FilterMods.GenericPrefix)));
            harmony.Patch(second, prefix: MethodOf(nameof(FilterMods.GenericPrefix)));

            List<string> blocked = decider.Lines.FindAll(line => line.StartsWith(UpdateWrapperHook.SuppressBlockedMarker, StringComparison.Ordinal));
            Assert.Equal(2, blocked.Count);
            Assert.Contains(blocked, line => line.Contains(nameof(GenericFilterTarget<object>)));
            Assert.Contains(blocked, line => line.Contains(nameof(SecondGenericFilterTarget<object>)));
        }
        finally
        {
            Disarm(harmony);
        }
    }

    [Fact]
    public void AnObserverThatIsNotADecider_LeavesTheRebuildToHarmony()
    {
        MethodInfo target = Fresh(typeof(NoDeciderTarget), nameof(NoDeciderTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        ObserverOnly observer = new ObserverOnly();

        try
        {
            WireGate(profilerPresent: false, capabilityOff: null);
            Assert.True(UpdateWrapperHook.TryInstall(observer, new MonoModDetourBackend(), observer.Record));
            FilterLog.Entries.Clear();

            harmony.Patch(target, prefix: MethodOf(nameof(FilterMods.NoDeciderPrefix)));

            Assert.Equal(99, NoDeciderTarget.Compute());
            Assert.Equal(new[] { "no-decider" }, FilterLog.Entries);
            Assert.Equal(1, observer.Notified);
        }
        finally
        {
            Disarm(harmony);
        }
    }

    [Fact]
    public void ADeciderThatThrows_LeavesTheRebuildToHarmonyAndLogsTheFailure()
    {
        MethodInfo target = Fresh(typeof(ThrowingDeciderTarget), nameof(ThrowingDeciderTarget.Compute));
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        ThrowingDecider decider = new ThrowingDecider();

        try
        {
            WireGate(profilerPresent: false, capabilityOff: null);
            Assert.True(UpdateWrapperHook.TryInstall(decider, new MonoModDetourBackend(), decider.Record));
            FilterLog.Entries.Clear();
            decider.Lines.Clear();

            harmony.Patch(target, prefix: MethodOf(nameof(FilterMods.ThrowingPrefix)));

            Assert.Equal(99, ThrowingDeciderTarget.Compute());
            Assert.Equal(new[] { "throwing" }, FilterLog.Entries);
            string logged = Assert.Single(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressFailedMarker, StringComparison.Ordinal));
            Assert.Contains("ThrowingDeciderTarget.Compute", logged);
            Assert.Contains("decider blew up", logged);
        }
        finally
        {
            Disarm(harmony);
        }
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static MethodInfo PlainTarget()
    {
        return Method(typeof(GateProbeTarget), nameof(GateProbeTarget.Compute));
    }

    private static MethodInfo Fresh(Type owner, string name)
    {
        MethodInfo target = Method(owner, name);
        Assert.Null(PatchProcessor.GetPatchInfo(target));
        return target;
    }

    private static void WireGate(bool profilerPresent, string? capabilityOff)
    {
        SuppressionGate.WireProfiler(() => profilerPresent);
        SuppressionGate.WireCapability(() => capabilityOff);
    }

    private static void Disarm(HarmonyLib.Harmony harmony)
    {
        UpdateWrapperHook.DetachObserver();
        SuppressionGate.ClearProbes();
        TestUnpatch.Own(harmony, ModId);
    }

    private static HarmonyMethod MethodOf(string name)
    {
        return new HarmonyMethod(Method(typeof(FilterMods), name));
    }

    private static void AssertHarmonyKeptTheBuild(MethodInfo target, string prefix, bool profilerPresent, string? capabilityOff, string expectedTag)
    {
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        AlwaysSuppress decider = new AlwaysSuppress();

        try
        {
            WireGate(profilerPresent, capabilityOff);
            Assert.True(UpdateWrapperHook.TryInstall(decider, new MonoModDetourBackend(), decider.Record));
            FilterLog.Entries.Clear();

            MethodInfo handedBack = harmony.Patch(target, prefix: MethodOf(prefix));

            Assert.NotSame(OriginalStubs.For(target), handedBack);
            Assert.Equal(99, target.Invoke(null, Array.Empty<object>()));
            Assert.Single(FilterLog.Entries);
            Assert.Empty(decider.Asked);

            string blocked = Assert.Single(decider.Lines, line => line.StartsWith(UpdateWrapperHook.SuppressBlockedMarker, StringComparison.Ordinal));
            Assert.Contains(target.Name, blocked);
            Assert.Contains(expectedTag, blocked);
        }
        finally
        {
            Disarm(harmony);
        }
    }

#if NET
    [Fact]
    public void Classify_UpdateWrapperThatWritesSharedStateWithNothingToReplicateItThrough_RefusesAndNamesTheMissingMember()
    {
        Assembly fake = HarmonyShaped(writesSharedState: true);
        SharedStateWrite write = HarmonyApiHonesty.Classify(fake);

        Assert.Equal(SharedStateWrite.InsideUpdateWrapper, write);
        string? reason = HarmonyApiHonesty.SuppressionReason(write, fake);
        Assert.Contains("inside PatchFunctions.UpdateWrapper", reason);
        Assert.Contains("could not find PatchInfo.ValidateSurvivingMetadata", reason);
        Assert.Contains("Harmony keeps building", reason);
    }

    [Fact]
    public void Classify_UpdateWrapperThatDoesNotWriteSharedState_PermitsSuppression()
    {
        SharedStateWrite write = HarmonyApiHonesty.Classify(HarmonyShaped(writesSharedState: false));

        Assert.Equal(SharedStateWrite.AfterUpdateWrapper, write);
        Assert.Null(HarmonyApiHonesty.SuppressionReason(write));
    }

    private static Assembly HarmonyShaped(bool writesSharedState)
    {
        string name = "ConcordFakeHarmony" + Guid.NewGuid().ToString("N");
        PersistedAssemblyBuilder assembly = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(name);

        TypeBuilder sharedState = module.DefineType(
            "HarmonyLib.HarmonySharedState",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder updatePatchInfo = sharedState.DefineMethod(
            "UpdatePatchInfo",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        updatePatchInfo.GetILGenerator().Emit(OpCodes.Ret);

        TypeBuilder patchFunctions = module.DefineType(
            "HarmonyLib.PatchFunctions",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder updateWrapper = patchFunctions.DefineMethod(
            "UpdateWrapper",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        ILGenerator il = updateWrapper.GetILGenerator();
        if (writesSharedState)
        {
            il.Emit(OpCodes.Call, updatePatchInfo);
        }

        il.Emit(OpCodes.Ret);

        sharedState.CreateType();
        patchFunctions.CreateType();

        using MemoryStream image = new MemoryStream();
        assembly.Save(image);
        return Assembly.Load(image.ToArray());
    }
#endif

    private sealed class AlwaysSuppress : IForeignPatchObserver, IForeignPatchDecider
    {
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
            return true;
        }
    }

    private sealed class ObserverOnly : IForeignPatchObserver
    {
        public int Notified { get; private set; }

        public List<string> Lines { get; } = new List<string>();

        public void Record(string line)
        {
            Lines.Add(line);
        }

        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
            Notified++;
        }
    }

    private sealed class ThrowingDecider : IForeignPatchObserver, IForeignPatchDecider
    {
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
            throw new InvalidOperationException("decider blew up");
        }
    }

    public static class FilterLog
    {
        public static List<string> Entries { get; } = new List<string>();
    }

    public static class GateProbeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 51;
        }
    }

    public static class ProfilerFilterTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 52;
        }
    }

    public static class CapabilityFilterTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 53;
        }
    }

    public static class GenericFilterTarget<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 54;
        }
    }

    public static class SecondGenericFilterTarget<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 54;
        }
    }

    public static class NoDeciderTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 55;
        }
    }

    public static class ThrowingDeciderTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 56;
        }
    }

    public static class FilterMods
    {
        public static bool ProfilerPrefix(ref int __result)
        {
            FilterLog.Entries.Add("profiler");
            __result = 99;
            return false;
        }

        public static bool CapabilityPrefix(ref int __result)
        {
            FilterLog.Entries.Add("capability");
            __result = 99;
            return false;
        }

        public static bool GenericPrefix(ref int __result)
        {
            FilterLog.Entries.Add("generic");
            __result = 99;
            return false;
        }

        public static bool NoDeciderPrefix(ref int __result)
        {
            FilterLog.Entries.Add("no-decider");
            __result = 99;
            return false;
        }

        public static bool ThrowingPrefix(ref int __result)
        {
            FilterLog.Entries.Add("throwing");
            __result = 99;
            return false;
        }
    }
}
