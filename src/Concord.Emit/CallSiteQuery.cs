using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Concord.Emit;

/// <summary>
///     Finds the instruction an injection targets inside a target body, and narrows a candidate set to
///     one occurrence.
/// </summary>
internal static class CallSiteQuery {
    internal static List<Instruction> Match(
        IReadOnlyList<Instruction> spine,
        Type declaringType,
        string memberName,
        Type[]? parameterTypes,
        bool includeFieldReads,
        bool matchNewObj) {
        List<Instruction> matches = new List<Instruction>();

        foreach (Instruction instruction in spine) {
            if (matchNewObj) {
                if (IsMatchingNewObj(instruction, declaringType, parameterTypes)) {
                    matches.Add(instruction);
                }

                continue;
            }

            if (includeFieldReads &&
                (parameterTypes is null || parameterTypes.Length == 0) &&
                IsMatchingFieldRead(instruction, declaringType, memberName)) {
                matches.Add(instruction);
                continue;
            }

            if (IsMatchingCall(instruction, declaringType, memberName, parameterTypes)) {
                matches.Add(instruction);
            }
        }

        return matches;
    }

    /// <summary>
    ///     Bounds a search to the instructions between two member-access anchors. The anchors are counted
    ///     across the whole body; only the injection's own <c>By</c> counts inside the result.
    /// </summary>
    /// <remarks>
    ///     The result is a subset of <paramref name="spine" /> holding the same <see cref="Instruction" />
    ///     objects, so a match found in it still resolves against the full spine with
    ///     <see cref="List{T}.IndexOf(T)" />. It is only ever safe to match and count against the result:
    ///     anything that reasons about branch targets or stack depth, such as
    ///     <see cref="CallSiteProvenance" />, needs the complete spine and silently under-reports on a
    ///     subset.
    /// </remarks>
    /// <param name="spine">The complete copied target body to search within.</param>
    /// <param name="range">The range to bound the search to, or null to search the whole body.</param>
    /// <param name="target">The original method being patched, used for diagnostic messages.</param>
    /// <returns><paramref name="spine" /> itself when unbounded, otherwise the instructions inside the range.</returns>
    internal static IReadOnlyList<Instruction> Narrow(IReadOnlyList<Instruction> spine, SliceRange? range, MethodBase target) {
        if (range is null) {
            return spine;
        }

        int start = 0;
        int end = spine.Count;

        if (range.FromType is null != range.FromMember is null) {
            throw new ConcordEmitException(
                "CONC131",
                $"Slice on '{target.DeclaringType?.Name}.{target.Name}' opens at a half-specified anchor: " +
                $"{HalfAnchor(range.FromType, range.FromMember, "fromType", "fromMember")} " +
                "Supply both to open at that member, or neither to open at the body head.");
        }

        if (range.ToType is null != range.ToMember is null) {
            throw new ConcordEmitException(
                "CONC132",
                $"Slice on '{target.DeclaringType?.Name}.{target.Name}' closes at a half-specified anchor: " +
                $"{HalfAnchor(range.ToType, range.ToMember, "toType", "toMember")} " +
                "Supply both to close at that member, or neither to close at the body tail.");
        }

        if (range.FromType is not null) {
            int anchor = AnchorIndex(spine, range.FromType, range.FromMember, range.FromBy);
            if (anchor < 0) {
                throw new ConcordEmitException(
                    "CONC131",
                    $"Slice on '{target.DeclaringType?.Name}.{target.Name}' opens at occurrence {range.FromBy} of " +
                    $"'{range.FromType.Name}.{range.FromMember}', which the method body does not contain.");
            }

            start = anchor + 1;
        }

        if (range.ToType is not null) {
            int anchor = AnchorIndex(spine, range.ToType, range.ToMember, range.ToBy);
            if (anchor < 0) {
                throw new ConcordEmitException(
                    "CONC132",
                    $"Slice on '{target.DeclaringType?.Name}.{target.Name}' closes at occurrence {range.ToBy} of " +
                    $"'{range.ToType.Name}.{range.ToMember}', which the method body does not contain.");
            }

            end = anchor;
        }

        if (end <= start) {
            string opens = range.FromType is null
                ? "the body head"
                : $"just after '{range.FromType.Name}.{range.FromMember}' occurrence {range.FromBy}";
            string closes = range.ToType is null
                ? "the body tail"
                : $"just before '{range.ToType.Name}.{range.ToMember}' occurrence {range.ToBy}";

            throw new ConcordEmitException(
                "CONC133",
                $"Slice on '{target.DeclaringType?.Name}.{target.Name}' is empty or inverted: it closes at or before it opens. " +
                $"It opens at index {start}, {opens}, and closes at index {end}, {closes}.");
        }

        List<Instruction> narrowed = new List<Instruction>(end - start);
        for (int i = start; i < end; i++) {
            narrowed.Add(spine[i]);
        }

        return narrowed;
    }

    /// <summary>
    ///     Narrows a match set to one occurrence.
    /// </summary>
    /// <param name="matches">The matched sites, in body order.</param>
    /// <param name="by">The 1-based occurrence to select, or 0 to select every match.</param>
    /// <param name="target">The original method being patched, used for the diagnostic message.</param>
    /// <param name="siteDescription">The call site as the author named it, used for the diagnostic message.</param>
    /// <param name="sliced">
    ///     Whether <paramref name="matches" /> came from a slice-narrowed search. The count reported on
    ///     failure is then an in-range count, and saying "in the method body" would be false.
    /// </param>
    internal static List<Instruction> Select(List<Instruction> matches, uint by, MethodBase target, string siteDescription, bool sliced) {
        if (by == 0) {
            return matches;
        }

        if (by > matches.Count) {
            throw new ConcordEmitException(
                "CONC033",
                $"Injection on '{target.DeclaringType?.Name}.{target.Name}' targets occurrence {by} of call site '{siteDescription}', " +
                $"but only {matches.Count} occurrence(s) exist {ScopeName(sliced)}.");
        }

        return [matches[(int)(by - 1)]];
    }

    /// <summary>
    ///     Names the region a call-site count was taken over, so a diagnostic does not claim a
    ///     slice-scoped count describes the whole body.
    /// </summary>
    /// <param name="sliced">Whether the search was bounded by a <see cref="SliceRange" />.</param>
    internal static string ScopeName(bool sliced) {
        return sliced ? "inside the declared range" : "in the method body";
    }

    private static string HalfAnchor(Type? anchorType, string? anchorMember, string typeParameter, string memberParameter) {
        return anchorType is null
            ? $"'{anchorMember}' was named without a declaring type, so {typeParameter} is missing."
            : $"'{anchorType.Name}' was named without a member, so {memberParameter} is missing.";
    }

    private static int AnchorIndex(IReadOnlyList<Instruction> spine, Type declaringType, string? memberName, uint by) {
        if (memberName is null) {
            return -1;
        }

        uint seen = 0;
        for (int i = 0; i < spine.Count; i++) {
            if (!IsMatchingCall(spine[i], declaringType, memberName, null) &&
                !IsMatchingFieldRead(spine[i], declaringType, memberName)) {
                continue;
            }

            seen++;
            if (seen == by) {
                return i;
            }
        }

        return -1;
    }

    private static bool IsMatchingCall(Instruction instruction, Type declaringType, string methodName, Type[]? parameterTypes) {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
            return false;
        }

        if (instruction.Operand is not MethodReference reference) {
            return false;
        }

        MethodBase resolved = reference.ResolveReflection();
        if (resolved.Name != methodName || resolved.DeclaringType != declaringType) {
            return false;
        }

        return parameterTypes == null || ParameterTypesMatch(resolved, parameterTypes);
    }

    private static bool IsMatchingFieldRead(Instruction instruction, Type declaringType, string fieldName) {
        if (instruction.OpCode != OpCodes.Ldfld && instruction.OpCode != OpCodes.Ldsfld) {
            return false;
        }

        if (instruction.Operand is not FieldReference reference) {
            return false;
        }

        FieldInfo resolved = reference.ResolveReflection();
        return resolved.Name == fieldName && resolved.DeclaringType == declaringType;
    }

    private static bool IsMatchingNewObj(Instruction instruction, Type constructedType, Type[]? parameterTypes) {
        if (instruction.OpCode != OpCodes.Newobj) {
            return false;
        }

        if (instruction.Operand is not MethodReference reference) {
            return false;
        }

        MethodBase resolved = reference.ResolveReflection();
        if (resolved.DeclaringType != constructedType) {
            return false;
        }

        return parameterTypes == null || ParameterTypesMatch(resolved, parameterTypes);
    }

    private static bool ParameterTypesMatch(MethodBase method, Type[] parameterTypes) {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != parameterTypes.Length) {
            return false;
        }

        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].ParameterType.FullName != parameterTypes[i].FullName) {
                return false;
            }
        }

        return true;
    }
}
