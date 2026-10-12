using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony;

internal static class DetourMethodHook
{
    internal const string TrippedMarker = "[Concord.Coex] backstop-tripped";

    private const string BridgeOwner = "concord.bridge";

    private static readonly MethodInfo? NotifyMethod =
        typeof(DetourMethodHook).GetMethod(nameof(Notify), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly FieldInfo? SuppressDepthField =
        typeof(UpdateWrapperHook).GetField("suppressDepth", BindingFlags.NonPublic | BindingFlags.Static);

    private static Action<string>? logger;

    private static IDetourHandle? hookHandle;

    private static IOwnsBodyQuery? ownsBodyBackend;

    internal static Func<MethodBase, bool>? OwnsBodyOverride { get; set; }

    internal static bool TryInstall(IDetourBackend rawBackend, Action<string> log)
    {
        logger = log;
        ownsBodyBackend = rawBackend as IOwnsBodyQuery;

        if (hookHandle != null)
        {
            return true;
        }

        if (ownsBodyBackend == null && OwnsBodyOverride == null)
        {
            logger = null;
            log(HarmonyBridge.BackstopUnavailableMarker + " " + rawBackend.GetType().Name + " does not implement Concord.Detour.IOwnsBodyQuery, so nothing can answer which methods Concord owns");
            return false;
        }

        MethodInfo? detourMethod = ResolveDetourMethod();
        if (detourMethod == null || NotifyMethod == null || SuppressDepthField == null)
        {
            logger = null;
            log(HarmonyBridge.BackstopUnavailableMarker + " the backstop needs HarmonyLib.PatchTools.DetourMethod, its own notify method and UpdateWrapperHook's suppression counter, and one of the three was not found");
            return false;
        }

        try
        {
            Injection notify = new Injection(NotifyMethod, new InjectAt.Head(), "concord.hook", 0);
            hookHandle = rawBackend.ApplyComposed(detourMethod, new[] { notify });
        }
        catch (Exception ex)
        {
            logger = null;
            log(HarmonyBridge.BackstopUnavailableMarker + " " + ex.Message);
            return false;
        }

        return true;
    }

    internal static void Detach()
    {
        logger = null;
        OwnsBodyOverride = null;
        ownsBodyBackend = null;
    }

    private static MethodInfo? ResolveDetourMethod()
    {
        Type? patchTools = typeof(HarmonyLib.Harmony).Assembly.GetType("HarmonyLib.PatchTools");
        return patchTools?.GetMethod("DetourMethod", BindingFlags.NonPublic | BindingFlags.Static);
    }

    private static void Notify(ControlHandle ch, MethodBase method, MethodBase replacement)
    {
        try
        {
            Action<string>? log = logger;
            if (log == null || method == null || Suppressed() || !OwnsBody(method))
            {
                return;
            }

            string owners = ForeignOwners(method);
            log(TrippedMarker + " " + Describe(method) + " was detoured after Concord took its body, by " +
                (owners.Length == 0 ? "a Harmony patch Harmony has not recorded an owner for yet" : owners) +
                ". Concord cannot own it again for the rest of this process");
        }
        catch (Exception)
        {
            logger = null;
        }
    }

    private static bool OwnsBody(MethodBase method)
    {
        Func<MethodBase, bool>? supplied = OwnsBodyOverride;
        if (supplied != null)
        {
            return supplied(method);
        }

        return ownsBodyBackend != null && ownsBodyBackend.OwnsBody(method);
    }

    private static bool Suppressed()
    {
        return SuppressDepthField!.GetValue(null) is int depth && depth > 0;
    }

    private static string Describe(MethodBase method)
    {
        return (method.DeclaringType?.FullName ?? "<module>") + "." + method.Name;
    }

    private static string ForeignOwners(MethodBase method)
    {
        Patches? patches = PatchProcessor.GetPatchInfo(method);
        if (patches == null)
        {
            return string.Empty;
        }

        SortedSet<string> owners = new SortedSet<string>(StringComparer.Ordinal);
        Collect(owners, patches.Prefixes);
        Collect(owners, patches.Postfixes);
        Collect(owners, patches.Transpilers);
        Collect(owners, patches.Finalizers);
        if (owners.Count == 0)
        {
            return string.Empty;
        }

        return "'" + string.Join("', '", owners) + "'";
    }

    private static void Collect(SortedSet<string> owners, IEnumerable<Patch> candidates)
    {
        foreach (Patch candidate in candidates)
        {
            if (candidate.owner == BridgeOwner)
            {
                continue;
            }

            owners.Add(candidate.owner);
        }
    }
}
