using System.Collections.Generic;
using System.Reflection;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony
{
    internal static partial class SupportMatrix
    {
        private static readonly FieldInfo? InnerTargetField = typeof(Patch).GetField("innerTarget");

        private static readonly FieldInfo? InnerMethodField = typeof(Patch).GetField("innerMethod");

        internal static bool HasInnerPatches(Patches? patchInfo)
        {
            if (patchInfo == null)
            {
                return false;
            }

            return HarmonyPatchSort.InnerCategory(patchInfo, "InnerPrefixes").Count > 0 ||
                HarmonyPatchSort.InnerCategory(patchInfo, "InnerPostfixes").Count > 0 ||
                HarmonyPatchSort.InnerCategory(patchInfo, "InnerFinalizers").Count > 0;
        }

        private static partial string? ValidateHost(MethodBase target, IReadOnlyList<Injection> added, Patches? patchInfo)
        {
            if (patchInfo == null || !HasInnerPatches(patchInfo))
            {
                return null;
            }

            foreach (Patch patch in InnerPatches(patchInfo))
            {
                string? reason = ValidateInnerPatch(target, added, patch);
                if (reason != null)
                {
                    return reason;
                }
            }

            return null;
        }

        private static string? ValidateInnerPatch(MethodBase target, IReadOnlyList<Injection> added, Patch patch)
        {
            object? innerTarget = InnerTargetField?.GetValue(patch);
            if (innerTarget != null)
            {
                return $"Target {target.Name} has a Harmony inner patch on a non-method operation ({innerTarget}). Concord cannot prove its composed body leaves that operation's count unchanged";
            }

            MethodInfo? inner;
            try
            {
                object? innerMethod = InnerMethodField?.GetValue(patch);
                inner = innerMethod?.GetType().GetProperty("Method")?.GetValue(innerMethod) as MethodInfo;
            }
            catch (System.Exception ex)
            {
                return $"Target {target.Name} has a Harmony inner patch whose inner method will not resolve: {ExceptionReason.Of(ex)}";
            }

            if (inner == null)
            {
                return null;
            }

            foreach (Injection injection in added)
            {
                if (injection.At is InjectAt.Transpiler)
                {
                    return $"Target {target.Name} has a Harmony inner patch on {inner.Name} and Concord transpiler injection {injection.InjectionMethod.Name} (a transpiler can add or remove the calls the inner patch counts)";
                }

                if (BodyCalls(injection.InjectionMethod, called => called == inner))
                {
                    return $"Injection method {injection.InjectionMethod.Name} calls {inner.Name}, which a Harmony inner patch on {target.Name} counts by position";
                }
            }

            return null;
        }

        private static List<Patch> InnerPatches(Patches patchInfo)
        {
            List<Patch> all = new List<Patch>();
            all.AddRange(HarmonyPatchSort.InnerCategory(patchInfo, "InnerPrefixes"));
            all.AddRange(HarmonyPatchSort.InnerCategory(patchInfo, "InnerPostfixes"));
            all.AddRange(HarmonyPatchSort.InnerCategory(patchInfo, "InnerFinalizers"));
            return all;
        }
    }
}
