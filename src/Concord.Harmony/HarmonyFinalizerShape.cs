using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Concord.Harmony;

internal delegate Exception? FinalizeResult<T>(Exception? current, ref T result);

internal delegate Exception? FinalizeVoid(Exception? current);

internal delegate Exception? FinalizeResultOn<T>(Exception? current, ref T result, object? instance);

internal delegate Exception? FinalizeVoidOn(Exception? current, object? instance);

internal sealed class HarmonyFinalizerShape {
    internal const string ExceptionName = "__exception";

    internal const string ResultName = "__result";

    internal const string InstanceName = "__instance";

    private HarmonyFinalizerShape(MethodInfo method, int exceptionIndex, int resultIndex, Type? resultType, int instanceIndex) {
        Method = method;
        ExceptionIndex = exceptionIndex;
        ResultIndex = resultIndex;
        ResultType = resultType;
        InstanceIndex = instanceIndex;
    }

    internal MethodInfo Method { get; }

    internal int ExceptionIndex { get; }

    internal int ResultIndex { get; }

    internal Type? ResultType { get; }

    internal int InstanceIndex { get; }

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

            return "Finalizer '" + name + "' takes '" + parameter.Name +
                "'. Only __exception, __result and __instance are mapped here. __state needs the prefix half of the same declaration to write it, and target arguments, __args, __originalMethod, __runOriginal and ___field have no binding, so this finalizer is refused and left to Harmony.";
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
        Type? resultType = null;
        ParameterInfo[] parameters = finalizer.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].Name == ExceptionName) {
                exceptionIndex = i;
            } else if (parameters[i].Name == ResultName) {
                resultIndex = i;
                resultType = parameters[i].ParameterType;
            } else if (parameters[i].Name == InstanceName) {
                instanceIndex = i;
            }
        }

        return new HarmonyFinalizerShape(finalizer, exceptionIndex, resultIndex, resultType, instanceIndex);
    }

    internal static Type ReturnTypeOf(MethodBase target) {
        return target is MethodInfo info ? info.ReturnType : typeof(void);
    }

    internal Delegate BuildAdapter(Type returnType) {
        bool isVoid = returnType == typeof(void);
        bool onInstance = InstanceIndex >= 0;
        Type delegateType = DelegateTypeFor(returnType, isVoid, onInstance);
        ParameterExpression current = Expression.Parameter(typeof(Exception), "current");
        ParameterExpression? result = isVoid ? null : Expression.Parameter(returnType.MakeByRefType(), "result");
        ParameterExpression? instance = onInstance ? Expression.Parameter(typeof(object), "instance") : null;

        ParameterInfo[] parameters = Method.GetParameters();
        Expression[] arguments = new Expression[parameters.Length];
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

            arguments[i] = ResultType!.IsByRef || ResultType == returnType ? result! : Expression.Convert(result!, ResultType);
        }

        Expression call = Expression.Call(Method, arguments);
        Expression body;
        if (!ReturnsException) {
            body = Expression.Block(call, current);
        } else {
            body = Method.ReturnType == typeof(Exception) ? call : Expression.Convert(call, typeof(Exception));
        }

        return Expression.Lambda(delegateType, body, LambdaParameters(current, result, instance)).Compile();
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

    private static Type DelegateTypeFor(Type returnType, bool isVoid, bool onInstance) {
        if (isVoid) {
            return onInstance ? typeof(FinalizeVoidOn) : typeof(FinalizeVoid);
        }

        return onInstance
            ? typeof(FinalizeResultOn<>).MakeGenericType(returnType)
            : typeof(FinalizeResult<>).MakeGenericType(returnType);
    }

    private static ParameterExpression[] LambdaParameters(ParameterExpression current, ParameterExpression? result, ParameterExpression? instance) {
        if (result == null) {
            return instance == null ? new[] { current } : new[] { current, instance };
        }

        return instance == null ? new[] { current, result } : new[] { current, result, instance };
    }
}
