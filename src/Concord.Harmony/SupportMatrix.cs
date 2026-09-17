#nullable disable

using System.Collections.Generic;
using System.Reflection;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony
{
    internal static partial class SupportMatrix
    {
        internal static string Validate(MethodBase target, IReadOnlyList<Injection> added, Patches patchInfo)
        {
            string hostReason = ValidateHost(target, patchInfo);
            if (hostReason != null)
            {
                return hostReason;
            }

            try
            {
                WrapperComposer.RejectSharedGenericInstantiation(target);
            }
            catch (ConcordEmitException ex)
            {
                return $"Target {target.Name} is a shared reference-type generic instantiation: {ex.Message}";
            }

            return null;
        }

        private static partial string ValidateHost(MethodBase target, Patches patchInfo);
    }
}
