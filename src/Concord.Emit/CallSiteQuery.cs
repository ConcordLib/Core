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

    internal static List<Instruction> Select(List<Instruction> matches, uint by, MethodBase target, string siteDescription) {
        if (by == 0) {
            return matches;
        }

        if (by > matches.Count) {
            throw new ConcordEmitException(
                "CONC033",
                $"Injection on '{target.DeclaringType?.Name}.{target.Name}' targets occurrence {by} of call site '{siteDescription}', but only {matches.Count} occurrence(s) exist in the method body.");
        }

        return [matches[(int)(by - 1)]];
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
