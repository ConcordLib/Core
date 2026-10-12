using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Concord.Emit;

namespace Concord.Harmony;

internal delegate void PostfixResult<T>(ref T result);

internal delegate void PostfixVoid();

internal delegate void PostfixInstanceResult<in TInstance, T>(TInstance instance, ref T result);

internal delegate void PostfixInstanceVoid<in TInstance>(TInstance instance);

internal static class PostfixMapping {
    internal const string ResultName = "__result";

    internal const string InstanceName = "__instance";

    private static readonly object Gate = new object();

    private static readonly Dictionary<(MethodInfo Postfix, Type ReturnType, Type? InstanceType), int> Slots =
        new Dictionary<(MethodInfo Postfix, Type ReturnType, Type? InstanceType), int>();

    private static Delegate[] adapters = Array.Empty<Delegate>();

    internal static string? Validate(MethodBase target, MethodInfo postfix) {
        if (postfix == null) {
            return "Postfix method is null.";
        }

        string name = (postfix.DeclaringType?.Name ?? "<global>") + "." + postfix.Name;
        if (!postfix.IsStatic) {
            return "Postfix '" + name + "' is an instance method. Harmony patch methods are static.";
        }

        Type returnType = ReturnTypeOf(target);
        if (postfix.ReturnType != typeof(void)) {
            return "Postfix '" + name + "' returns '" + postfix.ReturnType.Name + "'. Harmony treats that as a pass-through postfix, which this mapping does not cover.";
        }

        if (returnType.IsByRef) {
            return "Postfix '" + name + "' patches '" + target.Name + "', which returns by-ref. Harmony routes a by-ref return through __resultRef, which this mapping does not cover.";
        }

        if (!BoundParameters.ShimCarries(postfix)) {
            HarmonyArgumentBinder.CanCopyBody(target, postfix, HarmonyPatchRole.Postfix, out string? copyRefusal);
            return copyRefusal;
        }

        foreach (ParameterInfo parameter in postfix.GetParameters()) {
            string? reason = parameter.Name == ResultName
                ? BoundParameters.ValidateResult("Postfix", name, parameter, target, returnType)
                : BoundParameters.ValidateInstance("Postfix", name, parameter, target);
            if (reason != null) {
                return reason;
            }
        }

        return null;
    }

    internal static IReadOnlyList<Injection> Map(MethodBase target, MethodInfo postfix, string owner, int harmonyPriority, string[]? before = null, string[]? after = null) {
        string? reason = Validate(target, postfix);
        if (reason != null) {
            throw new InvalidOperationException(reason);
        }

        if (!BoundParameters.ShimCarries(postfix)) {
            return new[] {
                new Injection(postfix, new InjectAt.Return(0), owner, -harmonyPriority) {
                    ForeignOrigin = true,
                    BeforeOwners = before ?? Array.Empty<string>(),
                    AfterOwners = after ?? Array.Empty<string>(),
                },
            };
        }

        Type returnType = ReturnTypeOf(target);
        Type? instanceType = BoundParameters.NeedsInstance(postfix) ? BoundParameters.InstanceTypeOf(target) : null;
        int slot = Register(postfix, returnType, instanceType);

        return new[] {
            new Injection(Shim(returnType, instanceType), new InjectAt.Return(0), owner, -harmonyPriority) {
                ForeignOrigin = true,
                BoundArguments = new Dictionary<string, object?> { ["slot"] = slot },
                BeforeOwners = before ?? Array.Empty<string>(),
                AfterOwners = after ?? Array.Empty<string>(),
            },
        };
    }

    internal static void OnReturn<T>(ControlHandle<T> control, [Bound] int slot) {
        T result = control.ReturnValue;
        ((PostfixResult<T>)adapters[slot])(ref result);
        control.ReturnValue = result;
    }

    internal static void OnReturnVoid([Bound] int slot) {
        ((PostfixVoid)adapters[slot])();
    }

    internal static Type ReturnTypeOf(MethodBase target) {
        return target is MethodInfo info ? info.ReturnType : typeof(void);
    }

    private static int Register(MethodInfo postfix, Type returnType, Type? instanceType) {
        (MethodInfo, Type, Type?) key = (postfix, returnType, instanceType);
        lock (Gate) {
            if (Slots.TryGetValue(key, out int existing)) {
                return existing;
            }

            Delegate adapter = BuildAdapter(postfix, returnType, instanceType);
            Delegate[] grown = new Delegate[adapters.Length + 1];
            Array.Copy(adapters, grown, adapters.Length);
            grown[adapters.Length] = adapter;
            adapters = grown;
            Slots[key] = grown.Length - 1;
            return grown.Length - 1;
        }
    }

    private static Delegate BuildAdapter(MethodInfo postfix, Type returnType, Type? instanceType) {
        bool isVoid = returnType == typeof(void);
        Type delegateType = DelegateTypeFor(returnType, instanceType);
        ParameterExpression? result = isVoid ? null : Expression.Parameter(returnType.MakeByRefType(), "result");
        ParameterExpression? instance = instanceType == null ? null : Expression.Parameter(instanceType, "instance");

        ParameterInfo[] parameters = postfix.GetParameters();
        Expression[] arguments = new Expression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            arguments[i] = BoundParameters.Argument(parameters[i], returnType, result, instance);
        }

        return Expression.Lambda(delegateType, Expression.Call(postfix, arguments), BoundParameters.LambdaParameters(instance, result)).Compile();
    }

    private static Type DelegateTypeFor(Type returnType, Type? instanceType) {
        if (instanceType == null) {
            return returnType == typeof(void) ? typeof(PostfixVoid) : typeof(PostfixResult<>).MakeGenericType(returnType);
        }

        return returnType == typeof(void)
            ? typeof(PostfixInstanceVoid<>).MakeGenericType(instanceType)
            : typeof(PostfixInstanceResult<,>).MakeGenericType(instanceType, returnType);
    }

    private static MethodInfo Shim(Type returnType, Type? instanceType) {
        if (instanceType != null) {
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type shimType = typeof(InstanceShim<>).MakeGenericType(instanceType);
            if (returnType == typeof(void)) {
                return shimType.GetMethod(nameof(InstanceShim<object>.OnReturnVoid), instanceFlags)!;
            }

            return shimType.GetMethod(nameof(InstanceShim<object>.OnReturn), instanceFlags)!.MakeGenericMethod(returnType);
        }

        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        if (returnType == typeof(void)) {
            return typeof(PostfixMapping).GetMethod(nameof(OnReturnVoid), flags)!;
        }

        return typeof(PostfixMapping).GetMethod(nameof(OnReturn), flags)!.MakeGenericMethod(returnType);
    }

    internal abstract class InstanceShim<TInstance>
        where TInstance : class {
        [InjectInstance]
        internal TInstance Self => null!;

        internal void OnReturn<T>(ControlHandle<T> control, [Bound] int slot) {
            T result = control.ReturnValue;
            ((PostfixInstanceResult<TInstance, T>)adapters[slot])(Self, ref result);
            control.ReturnValue = result;
        }

        internal void OnReturnVoid([Bound] int slot) {
            ((PostfixInstanceVoid<TInstance>)adapters[slot])(Self);
        }
    }
}
