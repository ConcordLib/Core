using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Emit;

namespace Concord.Harmony;

/// <summary>
///     Decides whether Concord may suppress Harmony's own wrapper build for a target and own that body
///     instead. Every refusal comes back as the reason, and an accepted target comes back as null.
/// </summary>
public static class SuppressionGate
{
    private static Func<string?>? capabilityProbe;

    private static Func<bool>? profilerProbe;

    /// <summary>
    ///     Wires the host's body-replacement capability probe, which returns null when Concord can replace a
    ///     method body on this host and otherwise why it cannot. Suppression refuses until this is wired.
    /// </summary>
    /// <param name="probe">The probe to call on each decision.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="probe" /> is null.</exception>
    public static void WireCapability(Func<string?> probe)
    {
        capabilityProbe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <summary>
    ///     Wires the host's profiler probe, which returns true while a profiler may rewrite method bodies.
    ///     Suppression refuses until this is wired.
    /// </summary>
    /// <param name="probe">The probe to call on each decision.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="probe" /> is null.</exception>
    public static void WireProfiler(Func<bool> probe)
    {
        profilerProbe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    internal static void ClearProbes()
    {
        capabilityProbe = null;
        profilerProbe = null;
    }

    internal static string? Validate(MethodBase target)
    {
        return Validate(target, null);
    }

    internal static string? Validate(MethodBase target, IReadOnlyList<Injection>? added)
    {
        try
        {
            if (target == null)
            {
                return SuppressionReason.NoTarget;
            }

            return Profiler() ?? Generic(target, added) ?? Capability();
        }
        catch (Exception thrown)
        {
            return SuppressionReason.GateFailed(thrown);
        }
    }

    private static string? Profiler()
    {
        Func<bool>? probe = profilerProbe;
        if (probe == null)
        {
            return SuppressionReason.ProfilerUnwired;
        }

        return probe() ? SuppressionReason.ProfilerPresent : null;
    }

    private static string? Generic(MethodBase target, IReadOnlyList<Injection>? added)
    {
        if (target.ContainsGenericParameters)
        {
            return SuppressionReason.OpenGeneric(target);
        }

        if (WrapperComposer.SharesGenericBody(target))
        {
            return SuppressionReason.SharedGenericBody(target);
        }

        try
        {
            WrapperComposer.RejectSharedGenericInstantiation(target, added);
        }
        catch (ConcordEmitException thrown)
        {
            return SuppressionReason.GenericInstantiation(target, thrown.Message);
        }

        return null;
    }

    private static string? Capability()
    {
        Func<string?>? probe = capabilityProbe;
        if (probe == null)
        {
            return SuppressionReason.CapabilityUnwired;
        }

        string? unavailable = probe();
        return unavailable == null ? null : SuppressionReason.CapabilityOff(unavailable);
    }
}
