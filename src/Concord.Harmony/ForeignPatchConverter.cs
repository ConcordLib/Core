using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony;

internal static class ForeignPatchConverter {
    internal const string HarmonyOwner = "<harmony>";

    internal static Conversion Convert(MethodBase original, PatchInfo patchInfo) {
        List<Injection> injections = new List<Injection>();
        List<Refusal> refusals = new List<Refusal>();

        Patches? incoming;
        try {
            incoming = SupportMatrix.Incoming(patchInfo);
        } catch (Exception ex) {
            refusals.Add(new Refusal(HarmonyOwner, "Concord could not read the incoming PatchInfo. " + Describe(ex)));
            return new Conversion(injections, refusals);
        }

        if (incoming == null) {
            if (patchInfo != null) {
                refusals.Add(new Refusal(
                    HarmonyOwner,
                    "Concord could not mirror PatchInfo onto HarmonyLib.Patches on Harmony " + UpdateWrapperHook.HarmonyVersionText() + "."));
            }

            return new Conversion(injections, refusals);
        }

        HarmonyPatchSort.ByCategory sorted;
        try {
            sorted = HarmonyPatchSort.Sort(original, incoming);
        } catch (Exception ex) {
            refusals.Add(new Refusal(HarmonyOwner, "Concord could not reproduce Harmony's patch order. " + Describe(ex)));
            return new Conversion(injections, refusals);
        }

        MapCategory(
            sorted.Prefixes,
            patch => PrefixMapping.Validate(original, patch.PatchMethod),
            patch => One(PrefixMapping.Map(original, patch.PatchMethod, patch.owner)),
            injections,
            refusals);

        MapCategory(
            Reversed(sorted.Transpilers),
            patch => HarmonyTranspilerAdapter.Validate(patch.PatchMethod),
            patch => One(HarmonyTranspilerAdapter.Map(patch.PatchMethod, patch.owner)),
            injections,
            refusals);

        MapCategory(
            sorted.Postfixes,
            patch => PostfixMapping.Validate(original, patch.PatchMethod),
            patch => PostfixMapping.Map(original, patch.PatchMethod, patch.owner, patch.priority),
            injections,
            refusals);

        MapCategory(
            sorted.Finalizers,
            patch => FinalizerParticipant.Validate(original, patch.PatchMethod),
            patch => FinalizerParticipant.Map(original, patch.PatchMethod, patch.owner, patch.priority),
            injections,
            refusals);

        RefuseInfixes(Inner(incoming, "InnerPrefixes"), "InnerPrefixes", original, refusals);
        RefuseInfixes(Inner(incoming, "InnerPostfixes"), "InnerPostfixes", original, refusals);
        RefuseInfixes(Inner(incoming, "InnerFinalizers"), "InnerFinalizers", original, refusals);

        return new Conversion(injections, refusals);
    }

    private static void MapCategory(
        IReadOnlyList<Patch> category,
        Func<Patch, string?> validate,
        Func<Patch, IReadOnlyList<Injection>> map,
        List<Injection> injections,
        List<Refusal> refusals) {
        foreach (Patch patch in category) {
            string? reason;
            try {
                reason = validate(patch);
            } catch (Exception ex) {
                reason = Describe(ex);
            }

            if (reason != null) {
                refusals.Add(new Refusal(patch.owner, reason));
                continue;
            }

            IReadOnlyList<Injection> mapped;
            try {
                mapped = map(patch);
            } catch (Exception ex) {
                refusals.Add(new Refusal(patch.owner, Describe(ex)));
                continue;
            }

            foreach (Injection injection in mapped) {
                injections.Add(injection);
            }
        }
    }

    private static void RefuseInfixes(IReadOnlyList<Patch> category, string name, MethodBase original, List<Refusal> refusals) {
        foreach (Patch patch in category) {
            refusals.Add(new Refusal(
                patch.owner,
                "Harmony " + name + " carries an infix on '" + (original?.Name ?? "<unknown>") +
                "' and Concord has no infix mapping, so this target cannot be composed."));
        }
    }

    private static IReadOnlyList<Patch> Inner(Patches incoming, string name) {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        object? value = typeof(Patches).GetField(name, flags)?.GetValue(incoming)
                        ?? typeof(Patches).GetProperty(name, flags)?.GetValue(incoming);
        return value as IReadOnlyList<Patch> ?? Array.Empty<Patch>();
    }

    private static IReadOnlyList<Patch> Reversed(IReadOnlyList<Patch> category) {
        Patch[] reversed = new Patch[category.Count];
        for (int i = 0; i < reversed.Length; i++) {
            reversed[i] = category[category.Count - 1 - i];
        }

        return reversed;
    }

    private static IReadOnlyList<Injection> One(Injection injection) {
        return new[] { injection };
    }

    private static string Describe(Exception ex) {
        return ex.GetType().Name + ": " + ex.Message;
    }

    internal readonly struct Refusal {
        internal Refusal(string owner, string reason) {
            Owner = owner;
            Reason = reason;
        }

        internal string Owner { get; }

        internal string Reason { get; }
    }

    internal sealed class Conversion {
        internal Conversion(IReadOnlyList<Injection> injections, IReadOnlyList<Refusal> refusals) {
            Injections = injections;
            Refusals = refusals;
        }

        internal IReadOnlyList<Injection> Injections { get; }

        internal IReadOnlyList<Refusal> Refusals { get; }
    }
}
