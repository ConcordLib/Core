#nullable disable

using System.Collections.Generic;
using System.Reflection;
using Concord.Emit;
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

        private static partial string ValidateHost(MethodBase target, IReadOnlyList<Injection> added, Patches patchInfo)
        {
            if (HasILManipulators(patchInfo))
            {
                return $"Target {target.Name} has HarmonyX IL manipulators (they rewrite the stream through an ILContext the transpiler never sees)";
            }

            return null;
        }
    }
}
