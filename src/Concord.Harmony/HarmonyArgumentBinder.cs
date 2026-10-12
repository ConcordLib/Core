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

    internal static bool CanCopyBody(MethodBase target, MethodInfo patch, HarmonyPatchRole role, out string? refusal) {
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

        if (patch.ReturnType != typeof(void)) {
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
        foreach (ParameterInfo parameter in parameters) {
            HarmonyParameterKind kind = KindOf(parameter);
            if (kind != HarmonyParameterKind.TargetArgument) {
                refusal = Role(role) + " '" + name + "' takes '" + parameter.Name + "', which Harmony binds as " + Gloss(kind) +
                          ". Concord binds a copied patch body only to the target's own parameters, by name and type, so " + Token(kind) + " is refused.";
                return false;
            }

            if (!Matches(parameter, targetParams)) {
                refusal = Role(role) + " '" + name + "' takes '" + Describe(parameter.ParameterType) + " " + parameter.Name + "' but '" + target.Name +
                          "' declares no parameter of that name and type. Harmony binds a target argument by name alone and adapts the type; Concord binds by name and type, so a renamed, widened or by-ref-adapted argument is refused.";
                return false;
            }
        }

        if (role == HarmonyPatchRole.Prefix) {
            foreach (ParameterInfo parameter in parameters) {
                if (parameter.ParameterType.IsByRef || parameter.IsOut || !parameter.ParameterType.IsValueType) {
                    refusal = "Prefix '" + name + "' takes '" + Describe(parameter.ParameterType) + " " + parameter.Name +
                              "', so Harmony treats it as affecting the original and skips it once an earlier prefix has cancelled. A copied body runs unconditionally, so this prefix is refused.";
                    return false;
                }
            }
        }

        if (WritesOwnArgument(patch, parameters, out string? written)) {
            refusal = Role(role) + " '" + name + "' assigns to or takes the address of '" + written +
                      "', which Harmony passes by value. A copied body writes the target's own argument instead of a local copy, so this patch is refused.";
            return false;
        }

        refusal = null;
        return true;
    }

    private static bool Matches(ParameterInfo parameter, ParameterInfo[] targetParams) {
        foreach (ParameterInfo candidate in targetParams) {
            if (candidate.Name == parameter.Name && candidate.ParameterType == parameter.ParameterType) {
                return true;
            }
        }

        return false;
    }

    private static bool WritesOwnArgument(MethodInfo patch, ParameterInfo[] parameters, out string? written) {
        written = null;
        byte[]? il = patch.GetMethodBody()?.GetILAsByteArray();
        if (il == null) {
            written = "<unreadable body>";
            return true;
        }

        int at = 0;
        while (at < il.Length) {
            short code = il[at++];
            if (code == 0xFE) {
                if (at >= il.Length) {
                    written = "<truncated IL>";
                    return true;
                }

                code = (short)(0xFE00 | il[at++]);
            }

            if (!Opcodes.TryGetValue(code, out OpCode op)) {
                written = "<unknown opcode>";
                return true;
            }

            int size = OperandSize(op, il, at);
            if (size < 0 || at + size > il.Length) {
                written = "<truncated IL>";
                return true;
            }

            int index = ArgumentIndex(op, il, at);
            if (index >= 0 && index < parameters.Length && Reaches(op, parameters[index].ParameterType, patch.Module, il, at + size)) {
                written = parameters[index].Name;
                return true;
            }

            at += size;
        }

        return false;
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
