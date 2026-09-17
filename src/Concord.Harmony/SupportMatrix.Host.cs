#nullable disable

using System.Reflection;
using HarmonyLib;

namespace Concord.Harmony
{
    internal static partial class SupportMatrix
    {
        internal static bool HasInnerPatches(Patches patchInfo)
        {
            if (patchInfo == null)
            {
                return false;
            }

            return patchInfo.InnerPrefixes.Count > 0 || patchInfo.InnerPostfixes.Count > 0;
        }

        private static partial string ValidateHost(MethodBase target, Patches patchInfo)
        {
            if (HasInnerPatches(patchInfo))
            {
                return $"Target {target.Name} has Harmony 2.4 inner patches (not composable with Concord detours)";
            }

            return null;
        }
    }
}
