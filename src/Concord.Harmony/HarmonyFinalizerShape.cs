using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Concord.Harmony;

internal delegate Exception? FinalizeResult<T>(Exception? current, ref T result, object?[]? args, object? state);

internal delegate Exception? FinalizeVoid(Exception? current, object?[]? args, object? state);

internal delegate Exception? FinalizeResultOn<T>(Exception? current, ref T result, object? instance, object?[]? args, object? state);

internal delegate Exception? FinalizeVoidOn(Exception? current, object? instance, object?[]? args, object? state);

internal sealed class HarmonyFinalizerShape {
    internal const string ExceptionName = "__exception";

    internal const string ResultName = "__result";

    internal const string InstanceName = "__instance";

    internal const string StateName = "__state";

    internal const string FieldPrefix = "___";

    private HarmonyFinalizerShape(
        MethodInfo method, int exceptionIndex, int resultIndex, Type? resultType, int instanceIndex, int stateIndex, Type? stateType,
        Dictionary<int, FieldInfo>? fields, Dictionary<int, int>? targetArgs) {
        Method = method;
        Fields = fields;
        TargetArgs = targetArgs;
        ExceptionIndex = exceptionIndex;
        ResultIndex = resultIndex;
        ResultType = resultType;
        InstanceIndex = instanceIndex;
        StateIndex = stateIndex;
        StateType = stateType;
    }

    internal MethodInfo Method { get; }

    internal int ExceptionIndex { get; }

    internal int ResultIndex { get; }

    internal Type? ResultType { get; }

    internal int InstanceIndex { get; }

    internal int StateIndex { get; }

    internal Type? StateType { get; }

    internal Dictionary<int, FieldInfo>? Fields { get; }

    internal Dictionary<int, int>? TargetArgs { get; }

    internal bool NeedsArgs {
        get { return TargetArgs != null; }
    }

    internal bool NeedsReceiver {
        get {
            if (InstanceIndex >= 0) {
                return true;
            }

            if (Fields == null) {
                return false;
            }

            foreach (FieldInfo bound in Fields.Values) {
                if (!bound.IsStatic) {
                    return true;
                }
            }

            return false;
        }
    }

    internal bool ReturnsException {
        get { return Method.ReturnType != typeof(void); }
    }

    internal static string? Validate(MethodBase target, MethodInfo finalizer) {
        if (finalizer == null) {
            return "Finalizer method is null.";
        }

        string name = (finalizer.DeclaringType?.Name ?? "<global>") + "." + finalizer.Name;
        if (!finalizer.IsStatic) {
            return "Finalizer '" + name + "' is an instance method. Harmony patch methods are static.";
        }

        if (finalizer.ReturnType != typeof(void) && !typeof(Exception).IsAssignableFrom(finalizer.ReturnType)) {
            return "Finalizer '" + name + "' returns '" + finalizer.ReturnType.Name + "'. Harmony stores a finalizer's return value into the exception slot, so it must return void or an Exception.";
        }

        Type returnType = ReturnTypeOf(target);
        if (returnType.IsByRef) {
            return "Finalizer '" + name + "' patches '" + target.Name + "', which returns by-ref. Harmony routes a by-ref return through __resultRef, which this mapping does not cover.";
        }

        ParameterInfo[] parameters = finalizer.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            ParameterInfo parameter = parameters[i];
            if (parameter.Name == ExceptionName) {
                if (parameter.ParameterType.IsByRef) {
                    return "Finalizer '" + name + "' takes __exception by reference. Harmony passes it by value and reads the finalizer's return value to replace or swallow it.";
                }

                if (!parameter.ParameterType.IsAssignableFrom(typeof(Exception))) {
                    return "Finalizer '" + name + "' declares __exception as '" + parameter.ParameterType.Name + "', which cannot hold an Exception.";
                }

                continue;
            }

            if (parameter.Name == ResultName) {
                if (returnType == typeof(void)) {
                    return "Finalizer '" + name + "' declares __result but '" + target.Name + "' returns void.";
                }

                Type declared = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
                if (parameter.ParameterType.IsByRef && declared != returnType) {
                    return "Finalizer '" + name + "' declares 'ref " + declared.Name + " __result' but '" + target.Name + "' returns '" + returnType.Name + "'. A by-ref __result must match the return type exactly.";
                }

                if (!declared.IsAssignableFrom(returnType)) {
                    return "Finalizer '" + name + "' cannot receive the '" + returnType.Name + "' return of '" + target.Name + "' as __result of type '" + declared.Name + "'.";
                }

                continue;
            }

            if (parameter.Name == InstanceName) {
                string? refusal = RefuseInstance(target, parameter, name);
                if (refusal != null) {
                    return refusal;
                }

                continue;
            }

            if (parameter.Name == StateName) {
                if (finalizer.DeclaringType == null) {
                    return "Finalizer '" + name + "' takes __state but is not declared on a type, so there is no declaration to key its state slot to.";
                }

                if (parameter.ParameterType.IsByRef) {
                    return "Finalizer '" + name + "' takes __state by reference. Concord hands a finalizer a copy of the declaration's state slot, so a write through it would reach nothing.";
                }

                continue;
            }

            if (parameter.Name != null && parameter.Name.StartsWith(FieldPrefix, StringComparison.Ordinal)) {
                string? refusal = RefuseField(target, parameter, name);
                if (refusal != null) {
                    return refusal;
                }

                continue;
            }

            string? argRefusal = RefuseTargetArgument(target, parameter, name);
            if (argRefusal != null) {
                return argRefusal;
            }
        }

        return null;
    }

    internal static HarmonyFinalizerShape Create(MethodBase target, MethodInfo finalizer) {
        string? reason = Validate(target, finalizer);
        if (reason != null) {
            throw new InvalidOperationException(reason);
        }

        int exceptionIndex = -1;
        int resultIndex = -1;
        int instanceIndex = -1;
        int stateIndex = -1;
        Type? resultType = null;
        Type? stateType = null;
        Dictionary<int, FieldInfo>? fields = null;
        Dictionary<int, int>? targetArgs = null;
        ParameterInfo[] targetParams = target.GetParameters();
        ParameterInfo[] parameters = finalizer.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].Name == ExceptionName) {
                exceptionIndex = i;
            } else if (parameters[i].Name == ResultName) {
                resultIndex = i;
                resultType = parameters[i].ParameterType;
            } else if (parameters[i].Name == InstanceName) {
                instanceIndex = i;
            } else if (parameters[i].Name == StateName) {
                stateIndex = i;
                stateType = parameters[i].ParameterType;
            } else if (parameters[i].Name != null && parameters[i].Name!.StartsWith(FieldPrefix, StringComparison.Ordinal)) {
                fields ??= new Dictionary<int, FieldInfo>();
                fields[i] = FindField(target, parameters[i].Name!)!;
            } else if (resultIndex != i) {
                for (int slot = 0; slot < targetParams.Length; slot++) {
                    if (targetParams[slot].Name == parameters[i].Name) {
                        targetArgs ??= new Dictionary<int, int>();
                        targetArgs[i] = slot;
                        break;
                    }
                }
            }
        }

        return new HarmonyFinalizerShape(finalizer, exceptionIndex, resultIndex, resultType, instanceIndex, stateIndex, stateType, fields, targetArgs);
    }

    internal static Type ReturnTypeOf(MethodBase target) {
        return target is MethodInfo info ? info.ReturnType : typeof(void);
    }

    internal Delegate BuildAdapter(Type returnType) {
        bool isVoid = returnType == typeof(void);
        bool onInstance = NeedsReceiver;
        Type delegateType = DelegateTypeFor(returnType, isVoid, onInstance);
        ParameterExpression current = Expression.Parameter(typeof(Exception), "current");
        ParameterExpression? result = isVoid ? null : Expression.Parameter(returnType.MakeByRefType(), "result");
        ParameterExpression? instance = onInstance ? Expression.Parameter(typeof(object), "instance") : null;
        ParameterExpression args = Expression.Parameter(typeof(object[]), "args");
        ParameterExpression state = Expression.Parameter(typeof(object), "state");

        ParameterInfo[] parameters = Method.GetParameters();
        Expression[] arguments = new Expression[parameters.Length];
        List<ParameterExpression> temps = new List<ParameterExpression>();
        for (int i = 0; i < parameters.Length; i++) {
            if (i == ExceptionIndex) {
                Type declared = parameters[i].ParameterType;
                arguments[i] = declared == typeof(Exception) ? current : Expression.Convert(current, declared);
                continue;
            }

            if (i == InstanceIndex) {
                arguments[i] = Expression.Convert(instance!, parameters[i].ParameterType);
                continue;
            }

            if (i == StateIndex) {
                arguments[i] = parameters[i].ParameterType == typeof(object) ? state : Expression.Convert(state, parameters[i].ParameterType);
                continue;
            }

            if (Fields != null && Fields.TryGetValue(i, out FieldInfo? field)) {
                Expression access = field.IsStatic
                    ? Expression.Field(null, field)
                    : Expression.Field(Expression.Convert(instance!, field.DeclaringType!), field);
                Type declaredField = parameters[i].ParameterType;
                arguments[i] = declaredField.IsByRef || declaredField == field.FieldType ? access : Expression.Convert(access, declaredField);
                continue;
            }

            if (TargetArgs != null && TargetArgs.TryGetValue(i, out int targetIndex)) {
                arguments[i] = Expression.Convert(Expression.ArrayIndex(args, Expression.Constant(targetIndex)), parameters[i].ParameterType);
                continue;
            }

            arguments[i] = ResultType!.IsByRef || ResultType == returnType ? result! : Expression.Convert(result!, ResultType);
        }

        Expression call = Expression.Call(Method, arguments);
        List<Expression> steps = new List<Expression>();
        Expression outcome;
        if (!ReturnsException) {
            steps.Add(call);
            outcome = current;
        } else {
            ParameterExpression thrown = Expression.Variable(typeof(Exception), "thrown");
            temps.Add(thrown);
            steps.Add(Expression.Assign(thrown, Method.ReturnType == typeof(Exception) ? call : Expression.Convert(call, typeof(Exception))));
            outcome = thrown;
        }

        steps.Add(outcome);
        Expression body = Expression.Block(typeof(Exception), temps, steps);

        return Expression.Lambda(delegateType, body, LambdaParameters(current, result, instance, args, state)).Compile();
    }

    private static string? RefuseInstance(MethodBase target, ParameterInfo parameter, string name) {
        if (parameter.ParameterType.IsByRef) {
            return "Finalizer '" + name + "' takes __instance by reference. Concord binds the target instance read-only, so a by-ref __instance is refused.";
        }

        if (target.IsStatic) {
            return "Finalizer '" + name + "' takes __instance but '" + target.Name + "' is static, so there is no receiver to bind.";
        }

        Type? declaringType = target.DeclaringType;
        if (declaringType == null || declaringType.IsValueType) {
            return "Finalizer '" + name + "' takes __instance on value-type target '" + (declaringType?.Name ?? "<global>") +
                "'. Concord's instance binding does not cover a value-type receiver.";
        }

        if (!parameter.ParameterType.IsAssignableFrom(declaringType)) {
            return "Finalizer '" + name + "' declares __instance as '" + parameter.ParameterType.Name +
                "', which cannot receive target instance type '" + declaringType.Name + "'.";
        }

        if (declaringType.ContainsGenericParameters) {
            return "Finalizer '" + name + "' takes __instance on open generic target type '" + declaringType.Name +
                "', which Concord cannot close its instance shim over.";
        }

        return null;
    }

    private static string? RefuseField(MethodBase target, ParameterInfo parameter, string name) {
        string fieldName = parameter.Name!.Substring(FieldPrefix.Length);
        if (fieldName.Length == 0) {
            return "Finalizer '" + name + "' takes '" + parameter.Name + "', which names no field.";
        }

        FieldInfo? field = FindField(target, parameter.Name!);
        if (field == null) {
            return "Finalizer '" + name + "' takes '" + parameter.Name + "' but '" + (target.DeclaringType?.Name ?? "<global>") +
                "' has no field named '" + fieldName + "'.";
        }

        Type declared = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        if (parameter.ParameterType.IsByRef && declared != field.FieldType) {
            return "Finalizer '" + name + "' declares 'ref " + declared.Name + " " + parameter.Name + "' but field '" + fieldName +
                "' is '" + field.FieldType.Name + "'. A by-ref field binding must match the field type exactly.";
        }

        if (!declared.IsAssignableFrom(field.FieldType)) {
            return "Finalizer '" + name + "' cannot receive field '" + fieldName + "' of type '" + field.FieldType.Name +
                "' as '" + declared.Name + "'.";
        }

        if (!field.IsStatic && FinalizerParticipant.ReceiverFor(target) == null) {
            return "Finalizer '" + name + "' reads instance field '" + fieldName + "' but '" + target.Name +
                "' has no receiver Concord can bind, so the field cannot be reached.";
        }

        return null;
    }

    private static FieldInfo? FindField(MethodBase target, string parameterName) {
        string fieldName = parameterName.Substring(FieldPrefix.Length);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        for (Type? type = target.DeclaringType; type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(fieldName, flags);
            if (field != null) {
                return field;
            }
        }

        return null;
    }

    private static Type DelegateTypeFor(Type returnType, bool isVoid, bool onInstance) {
        if (isVoid) {
            return onInstance ? typeof(FinalizeVoidOn) : typeof(FinalizeVoid);
        }

        return onInstance
            ? typeof(FinalizeResultOn<>).MakeGenericType(returnType)
            : typeof(FinalizeResult<>).MakeGenericType(returnType);
    }

    private static ParameterExpression[] LambdaParameters(
        ParameterExpression current, ParameterExpression? result, ParameterExpression? instance, ParameterExpression args, ParameterExpression state) {
        if (result == null) {
            return instance == null ? new[] { current, args, state } : new[] { current, instance, args, state };
        }

        return instance == null ? new[] { current, result, args, state } : new[] { current, result, instance, args, state };
    }

    private static string? RefuseTargetArgument(MethodBase target, ParameterInfo parameter, string name) {
        string? declaredName = parameter.Name;
        if (declaredName == null || declaredName.StartsWith("__", StringComparison.Ordinal)) {
            return "Finalizer '" + name + "' takes '" + declaredName +
                "'. Only __exception, __result, __instance, __state, ___field and a target argument are mapped here. __args, __originalMethod and __runOriginal have no binding, so this finalizer is refused and left to Harmony.";
        }

        ParameterInfo? match = null;
        foreach (ParameterInfo candidate in target.GetParameters()) {
            if (candidate.Name == declaredName) {
                match = candidate;
                break;
            }
        }

        if (match == null) {
            return "Finalizer '" + name + "' takes '" + declaredName + "' but '" + target.Name +
                "' declares no parameter of that name.";
        }

        if (parameter.ParameterType.IsByRef) {
            return "Finalizer '" + name + "' takes '" + declaredName +
                "' by reference. Concord hands a finalizer a copy of each target argument, so a write through it would reach nothing.";
        }

        Type matchType = match.ParameterType.IsByRef ? match.ParameterType.GetElementType()! : match.ParameterType;
        if (parameter.ParameterType != matchType) {
            return "Finalizer '" + name + "' declares '" + declaredName + "' as '" + parameter.ParameterType.Name + "' but '" + target.Name +
                "' declares it as '" + matchType.Name + "'. Harmony loads the argument unchecked, so only an exact type match is bound.";
        }

        foreach (ParameterInfo candidate in target.GetParameters()) {
            if (candidate.IsOut) {
                return "Finalizer '" + name + "' takes '" + declaredName + "', but '" + target.Name + "' declares the out parameter '" + candidate.Name +
                    "'. An out parameter has no defined value when the body throws, so Concord cannot hand a finalizer the arguments of this target.";
            }
        }

        return null;
    }
}
