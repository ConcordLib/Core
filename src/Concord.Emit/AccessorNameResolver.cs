using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Resolves property names to accessor method names where Concord accepts a method name.
/// </summary>
internal static class AccessorNameResolver {
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static; // NOSONAR concord reaches private target members by design

    /// <summary>
    ///     Maps a declared name to the effective method name, resolving property names to accessors.
    /// </summary>
    /// <param name="declaringType">The type the name is declared against.</param>
    /// <param name="name">The declared method or property name.</param>
    /// <param name="injectionMethod">The injection method, used to disambiguate via its Operation parameter; may be <see langword="null" />.</param>
    /// <param name="allowOperationDisambiguation">Whether an Operation parameter may pick the accessor (around-invoke only).</param>
    /// <returns>The effective method name.</returns>
    /// <exception cref="ConcordEmitException">Thrown with <c>CONC036</c> when a two-accessor property cannot be disambiguated.</exception>
    internal static string ResolveAccessorName(Type declaringType, string name, MethodBase? injectionMethod, bool allowOperationDisambiguation) {
        if (HasMethodNamed(declaringType, name)) {
            return name;
        }

        PropertyInfo? property = declaringType.GetProperty(name, Declared);
        if (property is null) {
            return name;
        }

        bool hasGetter = property.GetGetMethod(true) is not null;
        bool hasSetter = property.GetSetMethod(true) is not null;

        if (hasGetter && !hasSetter) {
            return "get_" + name;
        }

        if (hasSetter && !hasGetter) {
            return "set_" + name;
        }

        if (allowOperationDisambiguation && injectionMethod is not null) {
            int operationArgIndex = ControlHandleLowering.FindOperationArgIndex(injectionMethod);
            if (operationArgIndex >= 0) {
                int offset = injectionMethod.IsStatic ? 0 : 1;
                Type declared = injectionMethod.GetParameters()[operationArgIndex - offset].ParameterType;
                return IsVoidFamily(declared) ? "set_" + name : "get_" + name;
            }
        }

        throw new ConcordEmitException(
            "CONC036",
            $"'{declaringType.Name}.{name}' is a property with both accessors and nothing selects one. Write 'get_{name}' or 'set_{name}' explicitly.");
    }

    private static bool IsVoidFamily(Type type) {
        if (!type.IsGenericType) {
            return false;
        }

        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(VoidOperation<>) || definition == typeof(VoidOperation<,>);
    }

    private static bool HasMethodNamed(Type declaringType, string name) {
        foreach (MethodInfo method in declaringType.GetMethods(Declared)) {
            if (method.Name == name) {
                return true;
            }
        }

        return false;
    }
}
