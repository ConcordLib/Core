#nullable disable

using System.Reflection;
using HarmonyLib;

namespace Concord.Harmony
{
    internal static partial class SupportMatrix
    {
        internal static bool HasILManipulators(Patches patchInfo)
        {
            if (patchInfo == null)
            {
                return false;
            }

            return patchInfo.ILManipulators.Count > 0;
        }

        private static partial string ValidateHost(MethodBase target, Patches patchInfo)
        {
            if (HasILManipulators(patchInfo))
            {
                return $"Target {target.Name} has HarmonyX IL manipulators (they rewrite the stream through an ILContext the transpiler never sees)";
            }

            return null;
        }
    }
}
