using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

/// <summary>
///     Recognizes Concord control API calls in copied injection method IL.
/// </summary>
internal static class ControlHandleLowering {
    /// <summary>
    ///     Classifies control-handle calls that need to be lowered into wrapper locals.
    /// </summary>
    internal enum ControlCallKind {
        None,
        Cancel,
        GetReturnValue,
        SetReturnValue,
    }

    internal static int FindControlHandleArgIndex(MethodBase injectionMethod) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int offset = injectionMethod.IsStatic ? 0 : 1;

        for (int i = 0; i < parameters.Length; i++) {
            if (IsControlHandleType(parameters[i].ParameterType)) {
                return i + offset;
            }
        }

        return -1;
    }

    internal static bool IsControlHandleReceiverLoad(Instruction instruction, int controlHandleArgIndex) {
        if (controlHandleArgIndex < 0) {
            return false;
        }

        int loaded = GetLoadArgIndex(instruction);
        return loaded == controlHandleArgIndex;
    }

    internal static int FindOperationArgIndex(MethodBase injectionMethod) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int offset = injectionMethod.IsStatic ? 0 : 1;

        for (int i = 0; i < parameters.Length; i++) {
            if (IsOperationType(parameters[i].ParameterType)) {
                return i + offset;
            }
        }

        return -1;
    }

    internal static bool IsOperationReceiverLoad(Instruction instruction, int operationArgIndex) {
        if (operationArgIndex < 0) {
            return false;
        }

        int loaded = GetLoadArgIndex(instruction);
        return loaded == operationArgIndex;
    }

    internal static bool IsOperationInvoke(Instruction instruction) {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
            return false;
        }

        if (instruction.Operand is not MethodReference reference) {
            return false;
        }

        MethodBase resolved = reference.ResolveReflection();
        Type? declaringType = resolved.DeclaringType;
        if (declaringType is null || !IsOperationType(declaringType)) {
            return false;
        }

        return resolved.Name == "Invoke";
    }

    internal static ControlCallKind ClassifyCall(Instruction instruction) {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
            return ControlCallKind.None;
        }

        if (instruction.Operand is not MethodReference reference) {
            return ControlCallKind.None;
        }

        MethodBase resolved = reference.ResolveReflection();
        Type? declaringType = resolved.DeclaringType;
        if (declaringType is null || !IsControlHandleType(declaringType)) {
            return ControlCallKind.None;
        }

        return resolved.Name switch {
            "Cancel" => ControlCallKind.Cancel,
            "get_ReturnValue" => ControlCallKind.GetReturnValue,
            "set_ReturnValue" => ControlCallKind.SetReturnValue,
            _ => ControlCallKind.None,
        };
    }

    internal static bool IsOriginalBodySpliceCall(Instruction instruction, MethodBase target) {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
            return false;
        }

        if (instruction.Operand is not MethodReference reference) {
            return false;
        }

        MethodBase resolved = reference.ResolveReflection();
        return resolved.Name == target.Name && resolved.DeclaringType == target.DeclaringType;
    }

    internal static List<Instruction> FindInvokeCallSites(IReadOnlyList<Instruction> spine, Type declaringType, string methodName, Type[]? parameterTypes = null) {
        List<Instruction> matches = new List<Instruction>();

        foreach (Instruction instruction in spine) {
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
                continue;
            }

            if (instruction.Operand is not MethodReference reference) {
                continue;
            }

            MethodBase resolved = reference.ResolveReflection();
            if (resolved.Name != methodName || resolved.DeclaringType != declaringType) {
                continue;
            }

            if (parameterTypes != null && !ParameterTypesMatch(resolved, parameterTypes)) {
                continue;
            }

            matches.Add(instruction);
        }

        return matches;
    }

    internal static bool InjectionMethodCancels(MethodBody injectionBody) {
        foreach (Instruction instruction in injectionBody.Instructions) {
            if (ClassifyCall(instruction) == ControlCallKind.Cancel) {
                return true;
            }
        }

        return false;
    }

    internal static bool InjectionMethodSetsReturnValue(MethodBody injectionBody) {
        foreach (Instruction instruction in injectionBody.Instructions) {
            if (ClassifyCall(instruction) == ControlCallKind.SetReturnValue) {
                return true;
            }
        }

        return false;
    }

    internal static int GetLoadArgIndex(Instruction instruction) {
        if (instruction.OpCode == OpCodes.Ldarg_0) {
            return 0;
        }

        if (instruction.OpCode == OpCodes.Ldarg_1) {
            return 1;
        }

        if (instruction.OpCode == OpCodes.Ldarg_2) {
            return 2;
        }

        if (instruction.OpCode == OpCodes.Ldarg_3) {
            return 3;
        }

        if (instruction.OpCode == OpCodes.Ldarg || instruction.OpCode == OpCodes.Ldarg_S) {
            return ArgIndexOperand(instruction.Operand);
        }

        if (instruction.OpCode == OpCodes.Ldarga || instruction.OpCode == OpCodes.Ldarga_S) {
            return ArgIndexOperand(instruction.Operand);
        }

        return -1;
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

    private static bool IsControlHandleType(Type type) {
        if (type == typeof(ControlHandle)) {
            return true;
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ControlHandle<>);
    }

    private static bool IsOperationType(Type type) {
        if (type == typeof(Operation)) {
            return true;
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Operation<>);
    }

    private static int ArgIndexOperand(object? operand) {
        if (operand is ParameterDefinition parameter) {
            return parameter.Sequence;
        }

        return Convert.ToInt32(operand);
    }
}
