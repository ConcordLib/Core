#nullable disable

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony
{
    internal static partial class SupportMatrix
    {
        private static readonly PropertyInfo InnerFinalizersProperty = typeof(Patches).GetProperty("InnerFinalizers");

        private static readonly FieldInfo InnerFinalizersField = typeof(Patches).GetField("InnerFinalizers");

        private static readonly FieldInfo InnerTargetField = typeof(Patch).GetField("innerTarget");

        internal static bool HasInnerPatches(Patches patchInfo)
        {
            if (patchInfo == null)
            {
                return false;
            }

            return patchInfo.InnerPrefixes.Count > 0 || patchInfo.InnerPostfixes.Count > 0 || InnerFinalizers(patchInfo).Count > 0;
        }

        private static partial string ValidateHost(MethodBase target, IReadOnlyList<Injection> added, Patches patchInfo)
        {
            if (!HasInnerPatches(patchInfo))
            {
                return null;
            }

            foreach (Patch patch in InnerPatches(patchInfo))
            {
                object innerTarget = InnerTargetField?.GetValue(patch);
                if (innerTarget != null)
                {
                    return $"Target {target.Name} has a Harmony inner patch on a non-method operation ({innerTarget}); Concord cannot prove its composed body leaves that operation's count unchanged";
                }

                MethodBase inner;
                try
                {
                    inner = patch.innerMethod?.Method;
                }
                catch (System.Exception ex)
                {
                    return $"Target {target.Name} has a Harmony inner patch whose inner method will not resolve: {ex.Message}";
                }

                if (inner == null)
                {
                    continue;
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
            }

            return null;
        }

        private static List<Patch> InnerPatches(Patches patchInfo)
        {
            List<Patch> all = new List<Patch>();
            all.AddRange(patchInfo.InnerPrefixes);
            all.AddRange(patchInfo.InnerPostfixes);
            all.AddRange(InnerFinalizers(patchInfo));
            return all;
        }

        private static List<Patch> InnerFinalizers(Patches patchInfo)
        {
            object value = InnerFinalizersProperty != null
                ? InnerFinalizersProperty.GetValue(patchInfo, null)
                : InnerFinalizersField?.GetValue(patchInfo);

            List<Patch> finalizers = new List<Patch>();
            if (value is IEnumerable items)
            {
                foreach (object item in items)
                {
                    if (item is Patch patch)
                    {
                        finalizers.Add(patch);
                    }
                }
            }

            return finalizers;
        }
    }
}
