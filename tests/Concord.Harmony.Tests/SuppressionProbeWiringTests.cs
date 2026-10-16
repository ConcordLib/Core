using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Harmony.Tests;

public static class ProbeWiringTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute()
    {
        return 1;
    }
}

[Collection("HarmonySerial")]
public sealed class SuppressionProbeWiringTests : IDisposable
{
    public void Dispose()
    {
        UpdateWrapperHook.DetachObserver();
        SuppressionGate.ClearProbes();
    }

    [Fact]
    public void ABackendCarryingProbes_WiresTheGateSoSuppressionIsReachable()
    {
        SuppressionGate.ClearProbes();
        ProbeBackend backend = new ProbeBackend();

        Assert.True(UpdateWrapperHook.TryInstall(new SilentObserver(), backend, Noop));

        Assert.Null(SuppressionGate.Validate(Target()));
    }

    [Fact]
    public void ABackendReportingAProfiler_WiresTheGateSoItRefuses()
    {
        SuppressionGate.ClearProbes();
        ProbeBackend backend = new ProbeBackend { Profiler = true };

        Assert.True(UpdateWrapperHook.TryInstall(new SilentObserver(), backend, Noop));

        Assert.Equal(SuppressionReason.ProfilerPresent, SuppressionGate.Validate(Target()));
    }

    [Fact]
    public void ABackendReportingNoBodyReplacement_WiresTheGateSoItRefuses()
    {
        SuppressionGate.ClearProbes();
        ProbeBackend backend = new ProbeBackend { Unavailable = "this host has no body replacement api" };

        Assert.True(UpdateWrapperHook.TryInstall(new SilentObserver(), backend, Noop));

        string? refused = SuppressionGate.Validate(Target());
        Assert.StartsWith(SuppressionReason.CapabilityTag + ":", refused);
        Assert.Contains("this host has no body replacement api", refused);
    }

    [Fact]
    public void ABackendCarryingNoProbes_LeavesTheGateUnwired()
    {
        SuppressionGate.ClearProbes();

        Assert.True(UpdateWrapperHook.TryInstall(new SilentObserver(), new MonoModDetourBackend(), Noop));

        Assert.Equal(SuppressionReason.ProfilerUnwired, SuppressionGate.Validate(Target()));
    }

    private static void Noop(string line)
    {
    }

    private static MethodBase Target()
    {
        return typeof(ProbeWiringTarget).GetMethod(nameof(ProbeWiringTarget.Compute))!;
    }

    private sealed class SilentObserver : IForeignPatchObserver
    {
        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
        }
    }

    private sealed class ProbeBackend : IDetourBackend, ISuppressionProbes
    {
        public bool Profiler { get; set; }

        public string? Unavailable { get; set; }

        public string? BodyReplacementUnavailable()
        {
            return Unavailable;
        }

        public bool ProfilerMayRewriteBodies()
        {
            return Profiler;
        }

        public IDetourHandle Apply(MethodBase original, MethodInfo replacement)
        {
            throw new NotSupportedException();
        }

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added)
        {
            throw new NotSupportedException();
        }
    }
}
