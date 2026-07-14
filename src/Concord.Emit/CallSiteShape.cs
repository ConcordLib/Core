using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     The resolved shape of a matched call site: receiver, parameters, and return type.
/// </summary>
/// <param name="HasThis">Whether the call consumes an instance receiver.</param>
/// <param name="ReceiverType">The receiver type for instance calls, or <see langword="null" /> for static calls.</param>
/// <param name="ParameterTypes">The call's parameter types in declaration order.</param>
/// <param name="ReturnType">The call's return type; <see cref="void" /> for void calls.</param>
internal sealed record CallSiteShape(bool HasThis, Type? ReceiverType, Type[] ParameterTypes, Type ReturnType) {
    /// <summary>
    ///     Resolves the shape of a call-site method.
    /// </summary>
    /// <param name="call">The resolved call-site method.</param>
    /// <returns>The call's shape.</returns>
    internal static CallSiteShape Resolve(MethodBase call) {
        bool hasThis = !call.IsStatic;
        Type? receiverType = hasThis ? call.DeclaringType : null;

        ParameterInfo[] parameters = call.GetParameters();
        Type[] parameterTypes = new Type[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            parameterTypes[i] = parameters[i].ParameterType;
        }

        Type returnType = call is MethodInfo info ? info.ReturnType : typeof(void);
        return new CallSiteShape(hasThis, receiverType, parameterTypes, returnType);
    }

    /// <summary>
    ///     Computes the Operation family type an around-invoke injection must declare for this shape.
    /// </summary>
    /// <returns>The expected Operation or VoidOperation type.</returns>
    internal Type ExpectedOperationType() {
        bool isVoid = ReturnType == typeof(void);

        if (isVoid) {
            return ParameterTypes.Length switch {
                0 => typeof(Operation),
                1 => typeof(VoidOperation<>).MakeGenericType(ParameterTypes),
                2 => typeof(VoidOperation<,>).MakeGenericType(ParameterTypes),
                3 => typeof(VoidOperation<,,>).MakeGenericType(ParameterTypes),
                4 => typeof(VoidOperation<,,,>).MakeGenericType(ParameterTypes),
                5 => typeof(VoidOperation<,,,,>).MakeGenericType(ParameterTypes),
                6 => typeof(VoidOperation<,,,,,>).MakeGenericType(ParameterTypes),
                7 => typeof(VoidOperation<,,,,,,>).MakeGenericType(ParameterTypes),
                8 => typeof(VoidOperation<,,,,,,,>).MakeGenericType(ParameterTypes),
                _ => throw new ConcordEmitException(
                    "CONC039",
                    $"Void call sites with {ParameterTypes.Length} arguments exceed the supported VoidOperation arity (8)."),
            };
        }

        Type[] withResult = new Type[ParameterTypes.Length + 1];
        ParameterTypes.CopyTo(withResult, 0);
        withResult[ParameterTypes.Length] = ReturnType;

        return ParameterTypes.Length switch {
            0 => typeof(Operation<>).MakeGenericType(withResult),
            1 => typeof(Operation<,>).MakeGenericType(withResult),
            2 => typeof(Operation<,,>).MakeGenericType(withResult),
            3 => typeof(Operation<,,,>).MakeGenericType(withResult),
            4 => typeof(Operation<,,,,>).MakeGenericType(withResult),
            5 => typeof(Operation<,,,,,>).MakeGenericType(withResult),
            6 => typeof(Operation<,,,,,,>).MakeGenericType(withResult),
            7 => typeof(Operation<,,,,,,,>).MakeGenericType(withResult),
            8 => typeof(Operation<,,,,,,,,>).MakeGenericType(withResult),
            _ => throw new ConcordEmitException(
                "CONC039",
                $"Value call sites with {ParameterTypes.Length} arguments exceed the supported Operation arity (8)."),
        };
    }
}
