#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Concord.Harmony;

/// <content>
///     Upstream Harmony flavour of the foreign owner scans, including inner patches.
/// </content>
public sealed partial class HarmonyBridge
{
    private static partial bool HasForeignPatch(Patches patchInfo)
    {
        if (patchInfo == null)
        {
            return false;
        }

        if (patchInfo.InnerPrefixes.Count > 0 || patchInfo.InnerPostfixes.Count > 0)
        {
            return true;
        }

        return HasForeignEntry(patchInfo.Prefixes) ||
               HasForeignEntry(patchInfo.Postfixes) ||
               HasForeignEntry(patchInfo.Transpilers) ||
               HasForeignEntry(patchInfo.Finalizers);
    }

    private static partial IReadOnlyList<string> CollectForeignOwners(MethodBase target)
    {
        Patches patchInfo = PatchProcessor.GetPatchInfo(target);
        if (patchInfo == null)
        {
            return Array.Empty<string>();
        }

        HashSet<string> owners = new HashSet<string>();
        CollectForeignOwners(patchInfo.Prefixes, owners);
        CollectForeignOwners(patchInfo.Postfixes, owners);
        CollectForeignOwners(patchInfo.Transpilers, owners);
        CollectForeignOwners(patchInfo.Finalizers, owners);
        CollectForeignOwners(patchInfo.InnerPrefixes, owners);
        CollectForeignOwners(patchInfo.InnerPostfixes, owners);

        return new List<string>(owners);
    }
}
