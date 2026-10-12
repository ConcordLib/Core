using System;
using System.Reflection;

namespace Concord.Harmony;

internal static class SuppressionReason
{
    internal const string ProfilerTag = "profiler";

    internal const string GenericTag = "generic";

    internal const string CapabilityTag = "capability";

    internal const string GateTag = "gate";

    internal const string NoTarget =
        GateTag + ": the pending rebuild carried no original method, so there is nothing for Concord to own";

    internal const string ProfilerUnwired =
        ProfilerTag + ": no profiler probe is wired, so Concord cannot tell whether a profiler is rewriting "
        + "method bodies in this process. Call SuppressionGate.WireProfiler from the host adapter";

    internal const string ProfilerPresent =
        ProfilerTag + ": a profiler is rewriting method bodies in this process, and its next write would delete "
        + "every Harmony patch folded into a body Concord owns";

    internal const string CapabilityUnwired =
        CapabilityTag + ": no body-replacement capability probe is wired, so Concord cannot tell whether it can "
        + "write a body on this host. Call SuppressionGate.WireCapability from the host adapter";

    internal static string CapabilityOff(string detail)
    {
        return CapabilityTag + ": Concord cannot replace a method body on this host, so suppressing Harmony's "
            + "build would leave the method owned by nobody. " + Detail(detail);
    }

    internal static string OpenGeneric(MethodBase target)
    {
        return GenericTag + ": " + Describe(target) + " still has unbound generic parameters, so it has no single "
            + "compiled body for Concord to replace";
    }

    internal static string SharedGenericBody(MethodBase target)
    {
        return GenericTag + ": " + Describe(target) + " shares one compiled body with every other reference-type "
            + "instantiation of its declaring type, so a body Concord wrote for this instantiation would run for "
            + "all of them";
    }

    internal static string GenericInstantiation(MethodBase target, string detail)
    {
        return GenericTag + ": " + Describe(target) + " is a generic instantiation Concord will not take a body "
            + "for. " + Detail(detail);
    }

    internal static string GateFailed(Exception thrown)
    {
        return GateTag + ": deciding whether to suppress threw " + thrown.GetType().Name + ", so Concord left the "
            + "target to Harmony. " + Detail(thrown.Message);
    }

    private static string Describe(MethodBase target)
    {
        string owner = target.DeclaringType == null ? "<no declaring type>" : target.DeclaringType.Name;
        return owner + "." + target.Name;
    }

    private static string Detail(string detail)
    {
        return string.IsNullOrEmpty(detail) ? "No detail was captured" : detail;
    }
}
