using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Concord.Emit;

namespace Concord.Harmony;

internal delegate bool PrefixResult<T>(ref T result);

internal delegate bool PrefixVoid();

internal delegate bool PrefixInstanceResult<in TInstance, T>(TInstance instance, ref T result);

internal delegate bool PrefixInstanceVoid<in TInstance>(TInstance instance);

internal static class BoundParameters {
    internal const string ResultName = "__result";

    internal const string InstanceName = "__instance";

    internal static bool NeedsInstance(MethodInfo patch) {
        foreach (ParameterInfo parameter in patch.GetParameters()) {
            if (parameter.Name == InstanceName) {
                return true;
            }
        }

        return false;
    }

    internal static Type InstanceTypeOf(MethodBase target) {
        return target.DeclaringType!;
    }

    internal static bool CanBindInstance(MethodBase target) {
        Type? declaringType = target.DeclaringType;
        return !target.IsStatic && declaringType is not null && !declaringType.IsValueType && !declaringType.ContainsGenericParameters;
    }

    internal static ParameterExpression[] LambdaParameters(ParameterExpression? instance, ParameterExpression? result) {
        if (instance == null) {
            return result == null ? Array.Empty<ParameterExpression>() : new[] { result };
        }

        return result == null ? new[] { instance } : new[] { instance, result };
    }

    internal static Expression Argument(ParameterInfo parameter, Type returnType, ParameterExpression? result, ParameterExpression? instance) {
        if (parameter.Name == InstanceName) {
            return parameter.ParameterType == instance!.Type ? instance : Expression.Convert(instance, parameter.ParameterType);
        }

        Type declared = parameter.ParameterType;
        return declared.IsByRef || declared == returnType ? result! : Expression.Convert(result!, declared);
    }

    internal static string? ValidateResult(string role, string name, ParameterInfo parameter, MethodBase target, Type returnType) {
        if (returnType == typeof(void)) {
            return role + " '" + name + "' declares __result but '" + target.Name + "' returns void.";
        }

        Type declared = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        if (parameter.ParameterType.IsByRef && declared != returnType) {
            return role + " '" + name + "' declares 'ref " + declared.Name + " __result' but '" + target.Name + "' returns '" + returnType.Name + "'. A by-ref __result must match the return type exactly.";
        }

        if (!declared.IsAssignableFrom(returnType)) {
            return role + " '" + name + "' cannot receive the '" + returnType.Name + "' return of '" + target.Name + "' as __result of type '" + declared.Name + "'.";
        }

        return null;
    }

    internal static string? ValidateInstance(string role, string name, ParameterInfo parameter, MethodBase target) {
        if (target.IsStatic) {
            return role + " '" + name + "' takes __instance but '" + target.Name + "' is static, so there is no instance to bind.";
        }

        Type? declaringType = target.DeclaringType;
        if (declaringType == null) {
            return role + " '" + name + "' takes __instance but '" + target.Name + "' has no declaring type to bind.";
        }

        if (declaringType.IsValueType) {
            return role + " '" + name + "' takes __instance and '" + declaringType.Name + "' is a struct. Concord binds the instance by value, so a prefix writing through 'ref __instance' would not reach the caller's copy; this patch is left to Harmony.";
        }

        if (declaringType.ContainsGenericParameters) {
            return role + " '" + name + "' takes __instance and '" + declaringType.Name + "' is an open generic type, which Concord cannot close to bind the instance; this patch is left to Harmony.";
        }

        if (parameter.ParameterType.IsByRef) {
            return role + " '" + name + "' takes __instance by reference. Concord binds the instance by value, so a write through it would not reach the caller; this patch is left to Harmony.";
        }

        if (!parameter.ParameterType.IsAssignableFrom(declaringType)) {
            return role + " '" + name + "' declares __instance as '" + parameter.ParameterType.Name + "', which cannot hold an instance of '" + declaringType.Name + "'.";
        }

        return null;
    }

    internal static bool ShimCarries(MethodInfo patch) {
        foreach (ParameterInfo parameter in patch.GetParameters()) {
            HarmonyParameterKind kind = HarmonyArgumentBinder.KindOf(parameter);
            if (kind != HarmonyParameterKind.Result && kind != HarmonyParameterKind.Instance) {
                return false;
            }
        }

        return true;
    }
}

internal static class PrefixMapping {
    internal const string ResultName = "__result";

    internal const string InstanceName = "__instance";

    private static readonly object Gate = new object();

    private static readonly Dictionary<(MethodInfo Prefix, Type ReturnType, Type? InstanceType), int> Slots =
        new Dictionary<(MethodInfo Prefix, Type ReturnType, Type? InstanceType), int>();

    private static Delegate[] adapters = Array.Empty<Delegate>();

    internal static string? Validate(MethodBase target, MethodInfo? prefix) {
        if (prefix == null) {
            return "Prefix method is null.";
        }

        string name = (prefix.DeclaringType?.Name ?? "<global>") + "." + prefix.Name;
        if (!prefix.IsStatic) {
            return "Prefix '" + name + "' is an instance method. Harmony patch methods are static.";
        }

        if (prefix.ReturnType != typeof(void) && prefix.ReturnType != typeof(bool)) {
            return "Prefix '" + name + "' returns '" + prefix.ReturnType.Name + "'. Harmony only accepts a void or bool prefix.";
        }

        Type returnType = HarmonyFinalizerShape.ReturnTypeOf(target);
        if (returnType.IsByRef) {
            return "Prefix '" + name + "' patches '" + target.Name + "', which returns by-ref. Harmony routes a by-ref return through __resultRef, which this mapping does not cover.";
        }

        if (!BoundParameters.ShimCarries(prefix)) {
            HarmonyArgumentBinder.CanCopyBody(target, prefix, HarmonyPatchRole.Prefix, out string? copyRefusal);
            return copyRefusal;
        }

        foreach (ParameterInfo parameter in prefix.GetParameters()) {
            string? reason = parameter.Name == ResultName
                ? BoundParameters.ValidateResult("Prefix", name, parameter, target, returnType)
                : BoundParameters.ValidateInstance("Prefix", name, parameter, target);
            if (reason != null) {
                return reason;
            }
        }

        return null;
    }

    internal static Injection Map(MethodBase target, MethodInfo prefix, string owner, string[]? before = null, string[]? after = null) {
        string? reason = Validate(target, prefix);
        if (reason != null) {
            throw new InvalidOperationException(reason);
        }

        if (!BoundParameters.ShimCarries(prefix)) {
            return new Injection(prefix, new InjectAt.Head(), owner, 0) {
                ForeignOrigin = true,
                BeforeOwners = before ?? Array.Empty<string>(),
                AfterOwners = after ?? Array.Empty<string>(),
            };
        }

        Type returnType = HarmonyFinalizerShape.ReturnTypeOf(target);
        Type? instanceType = BoundParameters.CanBindInstance(target) ? BoundParameters.InstanceTypeOf(target) : null;
        int slot = Register(prefix, returnType, instanceType);
        return new Injection(Shim(returnType, instanceType), new InjectAt.Head(), owner, 0) {
            ForeignOrigin = true,
            BoundArguments = new Dictionary<string, object?> { ["slot"] = slot, ["guarded"] = AffectsOriginal(prefix) },
            BeforeOwners = before ?? Array.Empty<string>(),
            AfterOwners = after ?? Array.Empty<string>(),
        };
    }

    internal static bool AffectsOriginal(MethodInfo prefix) {
        if (prefix.ReturnType == typeof(bool)) {
            return true;
        }

        foreach (ParameterInfo parameter in prefix.GetParameters()) {
            if (parameter.Name == BoundParameters.InstanceName) {
                continue;
            }

            if (parameter.IsOut || parameter.IsRetval || parameter.ParameterType.IsByRef || !parameter.ParameterType.IsValueType) {
                return true;
            }
        }

        return false;
    }

    internal static void Run<T>(ControlHandle<T> control, [Bound] int slot, [Bound] bool guarded) {
        Pass pass = control.GetState<Pass>();
        if (guarded && pass.Skipped) {
            return;
        }

        T result = control.ReturnValue;
        bool runOriginal = ((PrefixResult<T>)adapters[slot])(ref result);
        control.ReturnValue = result;
        if (runOriginal) {
            return;
        }

        pass.Skipped = true;
        control.SetState(pass);
        control.Cancel(true);
    }

    internal static void RunVoid(ControlHandle control, [Bound] int slot, [Bound] bool guarded) {
        Pass pass = control.GetState<Pass>();
        if (guarded && pass.Skipped) {
            return;
        }

        if (((PrefixVoid)adapters[slot])()) {
            return;
        }

        pass.Skipped = true;
        control.SetState(pass);
        control.Cancel(true);
    }

    private static int Register(MethodInfo prefix, Type returnType, Type? instanceType) {
        (MethodInfo, Type, Type?) key = (prefix, returnType, instanceType);
        lock (Gate) {
            if (Slots.TryGetValue(key, out int existing)) {
                return existing;
            }

            Delegate adapter = BuildAdapter(prefix, returnType, instanceType);
            Delegate[] grown = new Delegate[adapters.Length + 1];
            Array.Copy(adapters, grown, adapters.Length);
            grown[adapters.Length] = adapter;
            adapters = grown;
            Slots[key] = grown.Length - 1;
            return grown.Length - 1;
        }
    }

    private static Delegate BuildAdapter(MethodInfo prefix, Type returnType, Type? instanceType) {
        bool isVoid = returnType == typeof(void);
        Type delegateType = DelegateTypeFor(returnType, instanceType);
        ParameterExpression? result = isVoid ? null : Expression.Parameter(returnType.MakeByRefType(), "result");
        ParameterExpression? instance = instanceType == null ? null : Expression.Parameter(instanceType, "instance");

        ParameterInfo[] parameters = prefix.GetParameters();
        Expression[] arguments = new Expression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            arguments[i] = BoundParameters.Argument(parameters[i], returnType, result, instance);
        }

        Expression call = Expression.Call(prefix, arguments);
        Expression body = prefix.ReturnType == typeof(bool) ? call : Expression.Block(call, Expression.Constant(true));
        return Expression.Lambda(delegateType, body, BoundParameters.LambdaParameters(instance, result)).Compile();
    }

    private static Type DelegateTypeFor(Type returnType, Type? instanceType) {
        if (instanceType == null) {
            return returnType == typeof(void) ? typeof(PrefixVoid) : typeof(PrefixResult<>).MakeGenericType(returnType);
        }

        return returnType == typeof(void)
            ? typeof(PrefixInstanceVoid<>).MakeGenericType(instanceType)
            : typeof(PrefixInstanceResult<,>).MakeGenericType(instanceType, returnType);
    }

    private static MethodInfo Shim(Type returnType, Type? instanceType) {
        if (instanceType != null) {
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type shimType = typeof(InstanceShim<>).MakeGenericType(instanceType);
            if (returnType == typeof(void)) {
                return shimType.GetMethod(nameof(InstanceShim<object>.RunVoid), instanceFlags)!;
            }

            return shimType.GetMethod(nameof(InstanceShim<object>.Run), instanceFlags)!.MakeGenericMethod(returnType);
        }

        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        if (returnType == typeof(void)) {
            return typeof(PrefixMapping).GetMethod(nameof(RunVoid), flags)!;
        }

        return typeof(PrefixMapping).GetMethod(nameof(Run), flags)!.MakeGenericMethod(returnType);
    }

    internal struct Pass {
        internal bool Skipped;
    }

    internal abstract class InstanceShim<TInstance>
        where TInstance : class {
        [InjectInstance]
        internal TInstance Self => null!;

        internal void Run<T>(ControlHandle<T> control, [Bound] int slot, [Bound] bool guarded) {
            Pass pass = control.GetState<Pass>();
            if (guarded && pass.Skipped) {
                return;
            }

            T result = control.ReturnValue;
            bool runOriginal = ((PrefixInstanceResult<TInstance, T>)adapters[slot])(Self, ref result);
            control.ReturnValue = result;
            if (runOriginal) {
                return;
            }

            pass.Skipped = true;
            control.SetState(pass);
            control.Cancel(true);
        }

        internal void RunVoid(ControlHandle control, [Bound] int slot, [Bound] bool guarded) {
            Pass pass = control.GetState<Pass>();
            if (guarded && pass.Skipped) {
                return;
            }

            if (((PrefixInstanceVoid<TInstance>)adapters[slot])(Self)) {
                return;
            }

            pass.Skipped = true;
            control.SetState(pass);
            control.Cancel(true);
        }
    }
}
