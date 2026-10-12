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
        CancelSkip,
        GetReturnValue,
        SetReturnValue,
        SetState,
        GetState,
        GetException,
        Rethrow,
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

    internal static bool IsControlHandleType(Type type) {
        if (type == typeof(ControlHandle)) {
            return true;
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ControlHandle<>);
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

    internal static bool IsOperationType(Type type) {
        if (type == typeof(Operation)) {
            return true;
        }

        if (!type.IsGenericType) {
            return false;
        }

        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(Operation<>)
               || definition == typeof(Operation<,>)
               || definition == typeof(Operation<,,>)
               || definition == typeof(Operation<,,,>)
               || definition == typeof(Operation<,,,,>)
               || definition == typeof(Operation<,,,,,>)
               || definition == typeof(Operation<,,,,,,>)
               || definition == typeof(Operation<,,,,,,,>)
               || definition == typeof(Operation<,,,,,,,,>)
               || definition == typeof(VoidOperation<>)
               || definition == typeof(VoidOperation<,>)
               || definition == typeof(VoidOperation<,,>)
               || definition == typeof(VoidOperation<,,,>)
               || definition == typeof(VoidOperation<,,,,>)
               || definition == typeof(VoidOperation<,,,,,>)
               || definition == typeof(VoidOperation<,,,,,,>)
               || definition == typeof(VoidOperation<,,,,,,,>);
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
            "Cancel" => resolved.GetParameters().Length == 1 ? ControlCallKind.CancelSkip : ControlCallKind.Cancel,
            "get_ReturnValue" => ControlCallKind.GetReturnValue,
            "set_ReturnValue" => ControlCallKind.SetReturnValue,
            "SetState" => ControlCallKind.SetState,
            "GetState" => ControlCallKind.GetState,
            "get_Exception" => ControlCallKind.GetException,
            "Rethrow" => ControlCallKind.Rethrow,
            _ => ControlCallKind.None,
        };
    }

    internal static List<Instruction>? LowerCatchCall(
        ControlCallKind kind,
        bool insideCatch,
        ProtocolLocals locals,
        MethodBase injectionMethod) {
        if (kind == ControlCallKind.GetException) {
            RequireCatchPosition(kind, insideCatch, injectionMethod);
            return new List<Instruction> { Instruction.Create(OpCodes.Ldloc, RequireSlot(locals.CaughtException, kind, injectionMethod)) };
        }

        if (kind == ControlCallKind.Rethrow) {
            RequireCatchPosition(kind, insideCatch, injectionMethod);
            return new List<Instruction> {
                Instruction.Create(OpCodes.Ldc_I4_0),
                Instruction.Create(OpCodes.Stloc, RequireSlot(locals.CatchDecision, kind, injectionMethod)),
            };
        }

        if (kind == ControlCallKind.Cancel && insideCatch) {
            return new List<Instruction> {
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Stloc, RequireSlot(locals.CatchDecision, kind, injectionMethod)),
            };
        }

        return null;
    }

    internal static Type? ResolveStateType(Instruction instruction) {
        if (instruction.Operand is not GenericInstanceMethod generic) {
            return null;
        }

        if (generic.GenericArguments.Count != 1) {
            return null;
        }

        return generic.GenericArguments[0].ResolveReflection();
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

    internal static List<Instruction> FindInvokeCallSites(
        IReadOnlyList<Instruction> spine,
        Type declaringType,
        string methodName,
        Type[]? parameterTypes = null,
        bool includeFieldReads = false) {
        return CallSiteQuery.Match(spine, declaringType, methodName, parameterTypes, includeFieldReads, false);
    }

    internal static bool InjectionMethodCancels(MethodBody injectionBody) {
        foreach (Instruction instruction in injectionBody.Instructions) {
            ControlCallKind kind = ClassifyCall(instruction);
            if (kind is ControlCallKind.Cancel or ControlCallKind.CancelSkip) {
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

    internal static bool ReturnsControl(MethodBase injectionMethod) {
        return injectionMethod is MethodInfo info && info.ReturnType == typeof(Control);
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

    internal static void RequireHeadPosition(InjectAt? at, MethodBase injectionMethod) {
        if (at is not null && WrapperComposer.SupportsCancelSkip(at)) {
            return;
        }

        throw new ConcordEmitException(
            "CONC171",
            "Injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name + "' calls " +
            "the ControlHandle.Cancel(bool) overload outside At.Head. Only a head injection has later head injections " +
            "to skip, so the overload is available at At.Head and nowhere else. Call Cancel() instead, or move the " +
            "call to an At.Head injection.");
    }

    private static void RequireCatchPosition(ControlCallKind kind, bool insideCatch, MethodBase injectionMethod) {
        if (insideCatch) {
            return;
        }

        throw new ConcordEmitException(
            "CONC168",
            "Injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name + "' calls " +
            CallName(kind) + " outside a catch handler. Both only exist while an exception is in flight, so they are " +
            "available at At.Catch and nowhere else. Move the call to an At.Catch injection.");
    }

    private static VariableDefinition RequireSlot(VariableDefinition? slot, ControlCallKind kind, MethodBase injectionMethod) {
        if (slot is not null) {
            return slot;
        }

        throw new ConcordEmitException(
            "CONC169",
            "Concord bug: no catch slot exists for injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name +
            "' even though it calls " + CallName(kind) + " inside a catch handler. Nothing in the patch declaration causes this. " +
            "Report it with the declaration.");
    }

    private static string CallName(ControlCallKind kind) {
        return kind switch {
            ControlCallKind.GetException => "ControlHandle.Exception",
            ControlCallKind.Rethrow => "ControlHandle.Rethrow()",
            _ => "ControlHandle.Cancel()",
        };
    }

    private static int ArgIndexOperand(object? operand) {
        if (operand is ParameterDefinition parameter) {
            return parameter.Sequence;
        }

        return Convert.ToInt32(operand);
    }
}
