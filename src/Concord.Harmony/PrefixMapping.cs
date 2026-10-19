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

internal readonly struct CopyBinding {
    internal CopyBinding(
        int resultArg,
        int instanceArg,
        IReadOnlyDictionary<int, FieldInfo>? fields,
        IReadOnlyCollection<int>? scratch,
        IReadOnlyCollection<int>? address,
        int stateArg,
        Type? stateType,
        bool passThrough,
        int originalMethodArg,
        int runOriginalArg,
        int argsArg) {
        ResultArg = resultArg;
        InstanceArg = instanceArg;
        Fields = fields;
        Scratch = scratch;
        Address = address;
        StateArg = stateArg;
        StateType = stateType;
        PassThrough = passThrough;
        OriginalMethodArg = originalMethodArg;
        RunOriginalArg = runOriginalArg;
        ArgsArg = argsArg;
    }

    internal int ResultArg { get; }

    internal int InstanceArg { get; }

    internal IReadOnlyDictionary<int, FieldInfo>? Fields { get; }

    internal IReadOnlyCollection<int>? Scratch { get; }

    internal IReadOnlyCollection<int>? Address { get; }

    internal int StateArg { get; }

    internal Type? StateType { get; }

    internal int? State => StateArg >= 0 ? StateArg : null;

    internal bool PassThrough { get; }

    internal int? Result => ResultArg >= 0 ? ResultArg : null;

    internal int? Instance => InstanceArg >= 0 ? InstanceArg : null;

    internal int OriginalMethodArg { get; }

    internal int? OriginalMethod => OriginalMethodArg >= 0 ? OriginalMethodArg : null;

    internal int RunOriginalArg { get; }

    internal int? RunOriginal => RunOriginalArg >= 0 ? RunOriginalArg : null;

    internal int ArgsArg { get; }

    internal int? Args => ArgsArg >= 0 ? ArgsArg : null;
}

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

    internal static Type InstanceTypeOf(MethodBase target, MethodInfo patch) {
        Type declaringType = target.DeclaringType!;
        foreach (ParameterInfo parameter in patch.GetParameters()) {
            if (parameter.Name != InstanceName || parameter.ParameterType.IsByRef || parameter.ParameterType.IsValueType) {
                continue;
            }

            if (parameter.ParameterType != declaringType && declaringType.IsAssignableFrom(parameter.ParameterType)) {
                return parameter.ParameterType;
            }
        }

        return declaringType;
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
            if (instance == null) {
                return Expression.Constant(null, parameter.ParameterType);
            }

            return parameter.ParameterType == instance.Type ? instance : Expression.Convert(instance, parameter.ParameterType);
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
        if (parameter.ParameterType.IsByRef) {
            return role + " '" + name + "' takes __instance by reference. Concord binds the instance by value, so a write through it would not reach the caller; this patch is left to Harmony.";
        }

        if (target.IsStatic) {
            if (parameter.ParameterType.IsValueType) {
                return role + " '" + name + "' takes __instance as '" + parameter.ParameterType.Name + "' but '" + target.Name +
                       "' is static, so Harmony binds null there and a value type cannot hold it.";
            }

            return null;
        }

        Type? declaringType = target.DeclaringType;
        if (declaringType == null) {
            return role + " '" + name + "' takes __instance but '" + target.Name + "' has no declaring type to bind.";
        }

        if (declaringType.IsValueType && parameter.ParameterType != declaringType) {
            return role + " '" + name + "' declares __instance as '" + parameter.ParameterType.Name + "' and '" + declaringType.Name +
                   "' is a struct, which only binds to its own exact type; this patch is left to Harmony.";
        }

        if (declaringType.ContainsGenericParameters) {
            return role + " '" + name + "' takes __instance and '" + declaringType.Name + "' is an open generic type, which Concord cannot close to bind the instance; this patch is left to Harmony.";
        }

        if (!parameter.ParameterType.IsAssignableFrom(declaringType) && !declaringType.IsAssignableFrom(parameter.ParameterType)) {
            return role + " '" + name + "' declares __instance as '" + parameter.ParameterType.Name + "', which is unrelated to '" + declaringType.Name + "'.";
        }

        return null;
    }

    internal static int ResultIndex(MethodInfo patch) {
        return IndexOf(patch, HarmonyParameterKind.Result);
    }

    internal static int InstanceIndex(MethodInfo patch) {
        return IndexOf(patch, HarmonyParameterKind.Instance);
    }

    internal static int OriginalMethodIndex(MethodInfo patch) {
        return IndexOf(patch, HarmonyParameterKind.OriginalMethod);
    }

    internal static int RunOriginalIndex(MethodInfo patch) {
        return IndexOf(patch, HarmonyParameterKind.RunOriginal);
    }

    internal static int ArgsIndex(MethodInfo patch) {
        return IndexOf(patch, HarmonyParameterKind.ArgsArray);
    }

    internal static string? ValidateArgs(string role, string name, ParameterInfo parameter, MethodBase target, MethodInfo patch, int argsArg) {
        if (parameter.ParameterType != typeof(object[])) {
            return role + " '" + name + "' declares __args as '" + parameter.ParameterType.Name +
                   "'. It carries every target argument boxed into an array, so it must be object[] passed by value.";
        }

        foreach (ParameterInfo candidate in target.GetParameters()) {
            if (candidate.IsOut || candidate.IsRetval) {
                return role + " '" + name + "' takes __args, but '" + target.Name + "' declares the out parameter '" + candidate.Name +
                       "'. Harmony zeroes an out parameter when it builds the array, which a copied body cannot do without changing what the target already wrote.";
            }
        }

        return HarmonyArgumentBinder.ArgsArrayIsReadOnly(role, name, patch, argsArg);
    }

    internal static string? ValidateRunOriginal(string role, string name, ParameterInfo parameter) {
        if (parameter.ParameterType.IsByRef) {
            return role + " '" + name + "' takes __runOriginal by reference. Concord reads it from the wrapper's own cancel flag, so a write through it would reach nothing.";
        }

        if (parameter.ParameterType != typeof(bool)) {
            return role + " '" + name + "' declares __runOriginal as '" + parameter.ParameterType.Name + "'. It carries whether the original body still runs, so it must be bool.";
        }

        return null;
    }

    internal static string? ValidateOriginalMethod(string role, string name, ParameterInfo parameter, MethodBase target) {
        if (parameter.ParameterType.IsByRef) {
            return role + " '" + name + "' takes __originalMethod by reference. Concord binds it to a copy of the patched method handle, so a write through it would reach nothing.";
        }

        if (!parameter.ParameterType.IsInstanceOfType(target)) {
            return role + " '" + name + "' declares __originalMethod as '" + parameter.ParameterType.Name + "', which cannot hold '" + target.GetType().Name + "'.";
        }

        return null;
    }

    internal static string? PlanCopy(
        string role,
        string name,
        MethodBase target,
        MethodInfo patch,
        Type returnType,
        HarmonyPatchRole kind,
        bool bindResult,
        out CopyBinding binding,
        bool passThrough = false) {
        binding = default;
        ParameterInfo[] parameters = patch.GetParameters();
        int resultArg = passThrough ? 0 : bindResult ? ResultIndex(patch) : -1;
        int instanceArg = InstanceIndex(patch);
        int originalMethodArg = OriginalMethodIndex(patch);
        int runOriginalArg = RunOriginalIndex(patch);
        int argsArg = ArgsIndex(patch);

        if (argsArg >= 0) {
            string? argsReason = ValidateArgs(role, name, parameters[argsArg], target, patch, argsArg);
            if (argsReason != null) {
                return argsReason;
            }
        }

        if (runOriginalArg >= 0) {
            string? runOriginalReason = ValidateRunOriginal(role, name, parameters[runOriginalArg]);
            if (runOriginalReason != null) {
                return runOriginalReason;
            }
        }

        if (originalMethodArg >= 0) {
            string? originalMethodReason = ValidateOriginalMethod(role, name, parameters[originalMethodArg], target);
            if (originalMethodReason != null) {
                return originalMethodReason;
            }
        }

        if (resultArg >= 0) {
            string? resultReason = ValidateResult(role, name, parameters[resultArg], target, returnType);
            if (resultReason != null) {
                return resultReason;
            }
        }

        if (instanceArg >= 0) {
            string? instanceReason = ValidateInstance(role, name, parameters[instanceArg], target);
            if (instanceReason != null) {
                return instanceReason;
            }
        }

        string? fieldReason = PlanFields(role, name, target, parameters, out Dictionary<int, FieldInfo>? fields);
        if (fieldReason != null) {
            return fieldReason;
        }

        HarmonyArgumentBinder.CanCopyBody(
            target, patch, kind, out string? refusal, out List<int>? scratch, out List<int>? address, out int stateArg,
            resultArg >= 0, instanceArg >= 0, fields != null, passThrough, originalMethodArg >= 0);
        if (refusal != null) {
            return refusal;
        }

        if (originalMethodArg >= 0) {
            scratch?.Remove(originalMethodArg);
        }

        if (runOriginalArg >= 0) {
            scratch?.Remove(runOriginalArg);
        }

        if (argsArg >= 0) {
            scratch?.Remove(argsArg);
        }

        if (scratch?.Count == 0) {
            scratch = null;
        }

        Type? stateType = stateArg >= 0 ? Unwrap(parameters[stateArg].ParameterType) : null;
        binding = new CopyBinding(resultArg, instanceArg, fields, scratch, address, stateArg, stateType, passThrough, originalMethodArg, runOriginalArg, argsArg);
        return null;
    }

    internal static bool ShimCarries(MethodBase target, MethodInfo patch) {
        bool structTarget = !target.IsStatic && target.DeclaringType?.IsValueType == true;
        foreach (ParameterInfo parameter in patch.GetParameters()) {
            HarmonyParameterKind kind = HarmonyArgumentBinder.KindOf(parameter);
            if (kind == HarmonyParameterKind.Instance) {
                if (structTarget) {
                    return false;
                }

                continue;
            }

            if (kind != HarmonyParameterKind.Result) {
                return false;
            }
        }

        return true;
    }

    private static string? PlanFields(
        string role, string name, MethodBase target, ParameterInfo[] parameters, out Dictionary<int, FieldInfo>? fields) {
        fields = null;
        Type? declaringType = target.DeclaringType;
        for (int i = 0; i < parameters.Length; i++) {
            if (HarmonyArgumentBinder.KindOf(parameters[i]) != HarmonyParameterKind.Field) {
                continue;
            }

            string fieldName = parameters[i].Name!.Substring(3);
            FieldInfo? field = declaringType == null ? null : HarmonyLib.AccessTools.Field(declaringType, fieldName);
            if (field == null) {
                return role + " '" + name + "' takes '" + parameters[i].Name + "' but '" + (declaringType?.Name ?? "<global>") +
                       "' declares no field '" + fieldName + "'.";
            }

            if (!field.IsStatic && target.IsStatic) {
                return role + " '" + name + "' binds the instance field '" + fieldName + "' but '" + target.Name +
                       "' is static, so there is no instance to read it from.";
            }

            Type declared = parameters[i].ParameterType.IsByRef ? parameters[i].ParameterType.GetElementType()! : parameters[i].ParameterType;
            if (declared != field.FieldType) {
                return role + " '" + name + "' declares '" + parameters[i].Name + "' as '" + declared.Name + "' but field '" + fieldName +
                       "' is '" + field.FieldType.Name + "'. Concord binds a field by its exact type.";
            }

            fields ??= new Dictionary<int, FieldInfo>();
            fields[i] = field;
        }

        return null;
    }

    private static Type Unwrap(Type type) {
        return type.IsByRef ? type.GetElementType()! : type;
    }

    private static int IndexOf(MethodInfo patch, HarmonyParameterKind wanted) {
        ParameterInfo[] parameters = patch.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            if (HarmonyArgumentBinder.KindOf(parameters[i]) == wanted) {
                return i;
            }
        }

        return -1;
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

        if (!BoundParameters.ShimCarries(target, prefix)) {
            return BoundParameters.PlanCopy("Prefix", name, target, prefix, returnType, HarmonyPatchRole.Prefix, true, out _);
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

        if (!BoundParameters.ShimCarries(target, prefix)) {
            BoundParameters.PlanCopy(
                "Prefix",
                PostfixMapping.NameOf(prefix),
                target,
                prefix,
                HarmonyFinalizerShape.ReturnTypeOf(target),
                HarmonyPatchRole.Prefix,
                true,
                out CopyBinding binding);
            return new Injection(prefix, new InjectAt.Head(), owner, 0) {
                ForeignOrigin = true,
                ResultArgIndex = binding.Result,
                InstanceArgIndex = binding.Instance,
                FieldArgs = binding.Fields,
                ScratchArgs = binding.Scratch,
                AddressArgs = binding.Address,
                StateArgIndex = binding.State,
                OriginalMethodArgIndex = binding.OriginalMethod,
                RunOriginalArgIndex = binding.RunOriginal,
                ArgsArgIndex = binding.Args,
                StateType = binding.StateType,
                CancelsOnFalse = prefix.ReturnType == typeof(bool),
                AffectsOriginal = AffectsOriginal(prefix),
                BeforeOwners = before ?? Array.Empty<string>(),
                AfterOwners = after ?? Array.Empty<string>(),
            };
        }

        Type returnType = HarmonyFinalizerShape.ReturnTypeOf(target);
        Type? instanceType = BoundParameters.CanBindInstance(target) ? BoundParameters.InstanceTypeOf(target, prefix) : null;
        int slot = Register(prefix, returnType, instanceType);
        return new Injection(Shim(returnType, instanceType), new InjectAt.Head(), owner, 0) {
            ForeignOrigin = true,
            BoundArguments = new Dictionary<string, object?> { ["slot"] = slot },
            AffectsOriginal = AffectsOriginal(prefix),
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

            HarmonyParameterKind kind = HarmonyArgumentBinder.KindOf(parameter);
            if (kind == HarmonyParameterKind.State || kind == HarmonyParameterKind.OriginalMethod) {
                continue;
            }

            if (parameter.IsOut || parameter.IsRetval || parameter.ParameterType.IsByRef || !parameter.ParameterType.IsValueType) {
                return true;
            }
        }

        return false;
    }

    internal static void Run<T>(ControlHandle<T> control, [Bound] int slot) {
        T result = control.ReturnValue;
        bool runOriginal = ((PrefixResult<T>)adapters[slot])(ref result);
        control.ReturnValue = result;
        if (runOriginal) {
            return;
        }

        control.Cancel(true);
    }

    internal static void RunVoid(ControlHandle control, [Bound] int slot) {
        if (((PrefixVoid)adapters[slot])()) {
            return;
        }

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

    internal abstract class InstanceShim<TInstance>
        where TInstance : class {
        [InjectInstance]
        internal TInstance Self => null!;

        internal void Run<T>(ControlHandle<T> control, [Bound] int slot) {
            T result = control.ReturnValue;
            bool runOriginal = ((PrefixInstanceResult<TInstance, T>)adapters[slot])(Self, ref result);
            control.ReturnValue = result;
            if (runOriginal) {
                return;
            }

            control.Cancel(true);
        }

        internal void RunVoid(ControlHandle control, [Bound] int slot) {
            if (((PrefixInstanceVoid<TInstance>)adapters[slot])(Self)) {
                return;
            }

            control.Cancel(true);
        }
    }
}
