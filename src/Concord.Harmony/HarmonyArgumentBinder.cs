using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Concord.Harmony;

internal enum HarmonyPatchRole {
    Prefix,
    Postfix,
    Finalizer,
}

internal enum HarmonyParameterKind {
    TargetArgument,
    Instance,
    Result,
    ResultRef,
    State,
    Exception,
    Field,
    Positional,
    ArgsArray,
    OriginalMethod,
    RunOriginal,
    Renamed,
    Unknown,
}

internal static class HarmonyArgumentBinder {
    private static readonly Dictionary<short, OpCode> Opcodes = BuildOpcodes();

    internal static HarmonyParameterKind KindOf(ParameterInfo parameter) {
        if (parameter.GetCustomAttribute<HarmonyArgument>() != null) {
            return HarmonyParameterKind.Renamed;
        }

        string name = parameter.Name ?? string.Empty;
        switch (name) {
            case "__instance": return HarmonyParameterKind.Instance;
            case "__result": return HarmonyParameterKind.Result;
            case "__resultRef": return HarmonyParameterKind.ResultRef;
            case "__state": return HarmonyParameterKind.State;
            case "__exception": return HarmonyParameterKind.Exception;
            case "__args": return HarmonyParameterKind.ArgsArray;
            case "__originalMethod": return HarmonyParameterKind.OriginalMethod;
            case "__runOriginal": return HarmonyParameterKind.RunOriginal;
            default: break;
        }

        if (name.StartsWith("___", StringComparison.Ordinal)) {
            return HarmonyParameterKind.Field;
        }

        if (!name.StartsWith("__", StringComparison.Ordinal)) {
            return HarmonyParameterKind.TargetArgument;
        }

        return AllDigits(name.Substring(2)) ? HarmonyParameterKind.Positional : HarmonyParameterKind.Unknown;
    }

    internal static bool CanCopyBody(
        MethodBase target,
        MethodInfo patch,
        HarmonyPatchRole role,
        out string? refusal,
        out List<int>? scratchArgs,
        out List<int>? addressArgs,
        out int stateArg,
        bool resultIsBound = false,
        bool instanceIsBound = false,
        bool fieldsAreBound = false,
        bool passThrough = false,
        bool originalMethodIsBound = false) {
        scratchArgs = null;
        addressArgs = null;
        stateArg = -1;
        string name = (patch.DeclaringType?.Name ?? "<global>") + "." + patch.Name;

        if (!patch.IsStatic) {
            refusal = Role(role) + " '" + name + "' is an instance method. Harmony patch methods are static.";
            return false;
        }

        ParameterInfo[] parameters = patch.GetParameters();
        if (parameters.Length == 0) {
            refusal = Role(role) + " '" + name + "' takes no parameters, so nothing needs binding.";
            return false;
        }

        if (role == HarmonyPatchRole.Finalizer) {
            refusal = "Finalizer '" + name + "' takes '" + parameters[0].Name +
                      "'. A finalizer runs inside the catch protocol, which Concord reaches through a shim, and a shim binds no target argument.";
            return false;
        }

        if (patch.ReturnType != typeof(void) && !passThrough && !(role == HarmonyPatchRole.Prefix && patch.ReturnType == typeof(bool))) {
            refusal = Role(role) + " '" + name + "' takes '" + Names(parameters) + "' and returns '" + patch.ReturnType.Name + "'. Only a void " +
                      Role(role).ToLowerInvariant() + " can be woven by copying its body; a return value means cancellation or pass-through, which runs through a shim that cannot bind '" +
                      parameters[0].Name + "'.";
            return false;
        }

        if (patch.GetCustomAttributes(typeof(HarmonyArgument), false).Length > 0) {
            refusal = Role(role) + " '" + name + "' carries [HarmonyArgument], which renames a target argument for the whole method. Concord binds by the parameter's own name, so [HarmonyArgument] is refused.";
            return false;
        }

        if (patch.DeclaringType?.GetCustomAttributes(typeof(HarmonyArgument), true).Length > 0) {
            refusal = Role(role) + " '" + name + "' is declared on a type that carries [HarmonyArgument], which renames a target argument for every patch on that type. Concord binds by the parameter's own name, so [HarmonyArgument] is refused.";
            return false;
        }

        ParameterInfo[] targetParams = target.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            ParameterInfo parameter = parameters[i];
            if (passThrough && i == 0) {
                continue;
            }

            HarmonyParameterKind kind = KindOf(parameter);
            if (resultIsBound && kind == HarmonyParameterKind.Result) {
                continue;
            }

            if (instanceIsBound && kind == HarmonyParameterKind.Instance) {
                continue;
            }

            if (fieldsAreBound && kind == HarmonyParameterKind.Field) {
                continue;
            }

            if (originalMethodIsBound && kind == HarmonyParameterKind.OriginalMethod) {
                continue;
            }

            if (kind == HarmonyParameterKind.State) {
                stateArg = i;
                continue;
            }

            if (kind is HarmonyParameterKind.RunOriginal or HarmonyParameterKind.ArgsArray) {
                continue;
            }

            if (kind == HarmonyParameterKind.Positional) {
                int position = PositionalIndex(parameter.Name!);
                if (position < 0 || position >= targetParams.Length) {
                    refusal = Role(role) + " '" + name + "' takes '" + parameter.Name + "' but '" + target.Name + "' declares no parameter at that index.";
                    return Refuse(out scratchArgs, out addressArgs, out stateArg);
                }

                if (parameter.ParameterType == targetParams[position].ParameterType) {
                    continue;
                }

                if (parameter.ParameterType.IsByRef && parameter.ParameterType.GetElementType() == targetParams[position].ParameterType) {
                    addressArgs ??= new List<int>();
                    addressArgs.Add(i);
                    continue;
                }

                refusal = Role(role) + " '" + name + "' takes '" + Describe(parameter.ParameterType) + " " + parameter.Name + "' but '" + target.Name +
                          "' declares '" + Describe(targetParams[position].ParameterType) + "' at that index. Harmony adapts the type; Concord binds by type, so a widened positional argument is refused.";
                return Refuse(out scratchArgs, out addressArgs, out stateArg);
            }

            if (kind != HarmonyParameterKind.TargetArgument) {
                refusal = Role(role) + " '" + name + "' takes '" + parameter.Name + "', which Harmony binds as " + Gloss(kind) +
                          ". Concord binds a copied patch body only to the target's own parameters, by name and type, so " + Token(kind) + " is refused.";
                return false;
            }

            if (MatchesByAddress(parameter, targetParams)) {
                addressArgs ??= new List<int>();
                addressArgs.Add(i);
                continue;
            }

            if (!Matches(parameter, targetParams)) {
                refusal = Role(role) + " '" + name + "' takes '" + Describe(parameter.ParameterType) + " " + parameter.Name + "' but '" + target.Name +
                          "' declares no parameter of that name and type. Harmony binds a target argument by name alone and adapts the type; Concord binds by name and type, so a renamed, widened or by-ref-adapted argument is refused.";
                return false;
            }
        }

        string? unreadable = WrittenArguments(
            patch, parameters, addressArgs, stateArg, out scratchArgs, out string? reassigned, out string? stateTaken);
        if (unreadable != null) {
            refusal = Role(role) + " '" + name + "' could not be scanned for argument writes (" + unreadable +
                      "), so a copied body cannot be proven safe and this patch is refused.";
            return Refuse(out scratchArgs, out addressArgs, out stateArg);
        }

        if (reassigned != null) {
            refusal = Role(role) + " '" + name + "' rebinds its by-ref parameter '" + reassigned +
                      "'. The target passes that argument by value, so Concord binds the parameter to the argument's own address; there is no reference slot to rebind.";
            return Refuse(out scratchArgs, out addressArgs, out stateArg);
        }

        if (stateTaken != null) {
            refusal = Role(role) + " '" + name + "' reassigns the parameter '" + stateTaken +
                      "'. Concord binds __state to a shared slot on the wrapper, which is read and written through the parameter itself.";
            return Refuse(out scratchArgs, out addressArgs, out stateArg);
        }

        if (passThrough) {
            scratchArgs?.Remove(0);
            if (scratchArgs?.Count == 0) {
                scratchArgs = null;
            }
        }

        refusal = null;
        return true;
    }

    internal static string? ArgsArrayIsReadOnly(string role, string name, MethodInfo patch, int argsArg) {
        int offset = patch.IsStatic ? 0 : 1;
        byte[]? il = patch.GetMethodBody()?.GetILAsByteArray();
        if (il == null) {
            return role + " '" + name + "' takes __args but its body could not be read, so the array cannot be proven read-only.";
        }

        int at = 0;
        while (at < il.Length) {
            short code = il[at++];
            if (code == 0xFE) {
                if (at >= il.Length) {
                    return role + " '" + name + "' takes __args but its body ends mid-instruction, so the array cannot be proven read-only.";
                }

                code = (short)(0xFE00 | il[at++]);
            }

            if (!Opcodes.TryGetValue(code, out OpCode op)) {
                return role + " '" + name + "' takes __args but its body carries an opcode Concord cannot read, so the array cannot be proven read-only.";
            }

            int size = OperandSize(op, il, at);
            if (size < 0 || at + size > il.Length) {
                return role + " '" + name + "' takes __args but its body ends mid-instruction, so the array cannot be proven read-only.";
            }

            int index = TouchedArgumentIndex(op, il, at);
            if (index == argsArg + offset && !LoadsAnElement(il, at + size)) {
                return role + " '" + name + "' reads __args somewhere other than a plain element load. Harmony writes the array back into the target's arguments afterwards, " +
                       "and a copied body has no place to put that write-back, so only an array Concord can prove read-only is bound.";
            }

            at += size;
        }

        return null;
    }

    private static bool LoadsAnElement(byte[] il, int at) {
        for (int step = 0; step < 4 && at < il.Length; step++) {
            short code = il[at++];
            if (code == 0xFE) {
                if (at >= il.Length) {
                    return false;
                }

                code = (short)(0xFE00 | il[at++]);
            }

            if (!Opcodes.TryGetValue(code, out OpCode op)) {
                return false;
            }

            int size = OperandSize(op, il, at);
            if (size < 0 || at + size > il.Length) {
                return false;
            }

            if (op == OpCodes.Ldlen || op.Name?.StartsWith("ldelem", StringComparison.Ordinal) == true) {
                return true;
            }

            if (!PushesAnIndex(op)) {
                return false;
            }

            at += size;
        }

        return false;
    }

    private static bool PushesAnIndex(OpCode op) {
        return op.StackBehaviourPush == StackBehaviour.Pushi &&
               (op.StackBehaviourPop == StackBehaviour.Pop0 || op.StackBehaviourPop == StackBehaviour.Popi);
    }

    private static bool Refuse(out List<int>? scratchArgs, out List<int>? addressArgs, out int stateArg) {
        scratchArgs = null;
        addressArgs = null;
        stateArg = -1;
        return false;
    }

    private static bool Matches(ParameterInfo parameter, ParameterInfo[] targetParams) {
        foreach (ParameterInfo candidate in targetParams) {
            if (candidate.Name != parameter.Name) {
                continue;
            }

            if (candidate.ParameterType == parameter.ParameterType) {
                return true;
            }

            if (!candidate.ParameterType.IsByRef && Widens(parameter.ParameterType, candidate.ParameterType)) {
                return true;
            }

            if (candidate.ParameterType.IsByRef && !parameter.ParameterType.IsByRef &&
                Widens(parameter.ParameterType, candidate.ParameterType.GetElementType()!)) {
                return true;
            }

            if (!candidate.ParameterType.IsByRef && Narrows(parameter.ParameterType, candidate.ParameterType)) {
                return true;
            }
        }

        return false;
    }

    private static bool Widens(Type declared, Type candidate) {
        if (declared == candidate) {
            return true;
        }

        return !declared.IsValueType && !declared.IsByRef && !candidate.IsValueType && !candidate.IsByRef && declared.IsAssignableFrom(candidate);
    }

    private static bool Narrows(Type declared, Type candidate) {
        if (declared == candidate) {
            return false;
        }

        return !declared.IsValueType && !declared.IsByRef && !candidate.IsValueType && !candidate.IsByRef &&
               candidate.IsAssignableFrom(declared);
    }

    private static bool MatchesByAddress(ParameterInfo parameter, ParameterInfo[] targetParams) {
        if (!parameter.ParameterType.IsByRef) {
            return false;
        }

        Type element = parameter.ParameterType.GetElementType()!;
        foreach (ParameterInfo candidate in targetParams) {
            if (candidate.Name == parameter.Name && candidate.ParameterType == element) {
                return true;
            }
        }

        return false;
    }

    private static string? WrittenArguments(
        MethodInfo patch,
        ParameterInfo[] parameters,
        List<int>? addressArgs,
        int stateArg,
        out List<int>? written,
        out string? reassigned,
        out string? stateTaken) {
        written = null;
        reassigned = null;
        stateTaken = null;
        byte[]? il = patch.GetMethodBody()?.GetILAsByteArray();
        if (il == null) {
            return "<unreadable body>";
        }

        int at = 0;
        while (at < il.Length) {
            short code = il[at++];
            if (code == 0xFE) {
                if (at >= il.Length) {
                    return "<truncated IL>";
                }

                code = (short)(0xFE00 | il[at++]);
            }

            if (!Opcodes.TryGetValue(code, out OpCode op)) {
                return "<unknown opcode>";
            }

            int size = OperandSize(op, il, at);
            if (size < 0 || at + size > il.Length) {
                return "<truncated IL>";
            }

            int index = ArgumentIndex(op, il, at);
            if (index >= 0 && index < parameters.Length && addressArgs is not null && addressArgs.Contains(index)) {
                reassigned = parameters[index].Name;
                return null;
            }

            if (index >= 0 && index == stateArg && index < parameters.Length &&
                (op == OpCodes.Starg || op == OpCodes.Starg_S)) {
                stateTaken = parameters[index].Name;
                return null;
            }

            if (index >= 0 && index < parameters.Length && NeedsScratch(parameters[index]) &&
                Reaches(op, parameters[index].ParameterType, patch.Module, il, at + size)) {
                written ??= new List<int>();
                if (!written.Contains(index)) {
                    written.Add(index);
                }
            }

            at += size;
        }

        return null;
    }

    private static bool NeedsScratch(ParameterInfo parameter) {
        HarmonyParameterKind kind = KindOf(parameter);
        return kind == HarmonyParameterKind.TargetArgument || kind == HarmonyParameterKind.Instance;
    }

    private static bool Reaches(OpCode op, Type parameterType, Module module, byte[] il, int nextAt) {
        if (parameterType.IsByRef) {
            return false;
        }

        if (op == OpCodes.Starg || op == OpCodes.Starg_S) {
            return true;
        }

        if (!parameterType.IsPrimitive && !parameterType.IsEnum) {
            return true;
        }

        return !FollowedByCallOnOwnType(module, il, nextAt, parameterType);
    }

    private static bool FollowedByCallOnOwnType(Module module, byte[] il, int at, Type parameterType) {
        if (at >= il.Length) {
            return false;
        }

        short code = il[at++];
        if (code == 0xFE) {
            if (at >= il.Length) {
                return false;
            }

            code = (short)(0xFE00 | il[at++]);
        }

        if (!Opcodes.TryGetValue(code, out OpCode op)) {
            return false;
        }

        int size = OperandSize(op, il, at);
        if (size < 0 || at + size > il.Length) {
            return false;
        }

        if (op == OpCodes.Constrained) {
            return ResolveType(module, il, at) == parameterType;
        }

        if (op == OpCodes.Call || op == OpCodes.Callvirt) {
            MethodBase? callee = ResolveMethod(module, il, at);
            return callee is not null && !callee.IsStatic && callee.DeclaringType == parameterType;
        }

        return false;
    }

    private static Type? ResolveType(Module module, byte[] il, int at) {
        try {
            return module.ResolveType(ReadToken(il, at));
        } catch (ArgumentException) {
            return null;
        }
    }

    private static MethodBase? ResolveMethod(Module module, byte[] il, int at) {
        try {
            return module.ResolveMethod(ReadToken(il, at));
        } catch (ArgumentException) {
            return null;
        }
    }

    private static int ReadToken(byte[] il, int at) {
        return il[at] | (il[at + 1] << 8) | (il[at + 2] << 16) | (il[at + 3] << 24);
    }

    private static int TouchedArgumentIndex(OpCode op, byte[] il, int at) {
        if (op == OpCodes.Ldarg_0) {
            return 0;
        }

        if (op == OpCodes.Ldarg_1) {
            return 1;
        }

        if (op == OpCodes.Ldarg_2) {
            return 2;
        }

        if (op == OpCodes.Ldarg_3) {
            return 3;
        }

        if (op == OpCodes.Ldarg_S) {
            return il[at];
        }

        if (op == OpCodes.Ldarg) {
            return il[at] | (il[at + 1] << 8);
        }

        return ArgumentIndex(op, il, at);
    }

    private static int ArgumentIndex(OpCode op, byte[] il, int at) {
        if (op == OpCodes.Starg_S || op == OpCodes.Ldarga_S) {
            return il[at];
        }

        if (op == OpCodes.Starg || op == OpCodes.Ldarga) {
            return il[at] | (il[at + 1] << 8);
        }

        return -1;
    }

    private static int OperandSize(OpCode op, byte[] il, int at) {
        switch (op.OperandType) {
            case OperandType.InlineNone:
                return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                return 1;
            case OperandType.InlineVar:
                return 2;
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineI:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR:
                return 4;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                return 8;
            case OperandType.InlineSwitch:
                if (at + 4 > il.Length) {
                    return -1;
                }

                int count = il[at] | (il[at + 1] << 8) | (il[at + 2] << 16) | (il[at + 3] << 24);
                return count < 0 || count > (il.Length - at - 4) / 4 ? -1 : 4 + (count * 4);
            default:
                return -1;
        }
    }

    private static Dictionary<short, OpCode> BuildOpcodes() {
        Dictionary<short, OpCode> map = new Dictionary<short, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (field.GetValue(null) is OpCode op) {
                map[op.Value] = op;
            }
        }

        return map;
    }

    private static int PositionalIndex(string name) {
        if (!name.StartsWith("__", StringComparison.Ordinal)) {
            return -1;
        }

        string digits = name.Substring(2);
        return AllDigits(digits) ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : -1;
    }

    private static bool AllDigits(string text) {
        if (text.Length == 0) {
            return false;
        }

        foreach (char c in text) {
            if (c < '0' || c > '9') {
                return false;
            }
        }

        return true;
    }

    private static string Names(ParameterInfo[] parameters) {
        string[] names = new string[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            names[i] = parameters[i].Name ?? "<unnamed>";
        }

        return string.Join(", ", names);
    }

    private static string Role(HarmonyPatchRole role) {
        return role == HarmonyPatchRole.Prefix ? "Prefix" : role == HarmonyPatchRole.Postfix ? "Postfix" : "Finalizer";
    }

    private static string Describe(Type type) {
        return type.IsByRef ? "ref " + type.GetElementType()!.Name : type.Name;
    }

    private static string Token(HarmonyParameterKind kind) {
        switch (kind) {
            case HarmonyParameterKind.Instance: return "__instance";
            case HarmonyParameterKind.Result: return "__result";
            case HarmonyParameterKind.ResultRef: return "__resultRef";
            case HarmonyParameterKind.State: return "__state";
            case HarmonyParameterKind.Exception: return "__exception";
            case HarmonyParameterKind.Field: return "___field";
            case HarmonyParameterKind.Positional: return "__N";
            case HarmonyParameterKind.ArgsArray: return "__args";
            case HarmonyParameterKind.OriginalMethod: return "__originalMethod";
            case HarmonyParameterKind.RunOriginal: return "__runOriginal";
            case HarmonyParameterKind.Renamed: return "[HarmonyArgument]";
            default: return "that name";
        }
    }

    private static string Gloss(HarmonyParameterKind kind) {
        switch (kind) {
            case HarmonyParameterKind.Instance: return "__instance, the target instance";
            case HarmonyParameterKind.Result: return "__result, the target's return value";
            case HarmonyParameterKind.ResultRef: return "__resultRef, a by-ref return";
            case HarmonyParameterKind.State: return "__state, a per-call slot shared with the same patch class";
            case HarmonyParameterKind.Exception: return "__exception, the exception a finalizer caught";
            case HarmonyParameterKind.Field: return "___field, a field on the target's declaring type";
            case HarmonyParameterKind.Positional: return "a positional __N argument";
            case HarmonyParameterKind.ArgsArray: return "__args, every argument boxed into an array";
            case HarmonyParameterKind.OriginalMethod: return "__originalMethod, the patched method itself";
            case HarmonyParameterKind.RunOriginal: return "__runOriginal, whether a prefix has cancelled";
            case HarmonyParameterKind.Renamed: return "a [HarmonyArgument]-renamed argument";
            default: return "an unrecognised Harmony parameter";
        }
    }
}
