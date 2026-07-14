using System.Reflection;

namespace Concord.Emit;

internal static class InjectedMemberResolver {
    private const string CodeCONC070 = "CONC070";
    private const string CodeCONC071 = "CONC071";
    private const string CodeCONC072 = "CONC072";
    private const string CodeCONC073 = "CONC073";
    private const string CodeCONC074 = "CONC074";
    private const string InjectedPropertyDeclaration = "Injected property declaration '";
    private const string HasType = "' has type '";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011", Justification = "Concord resolves private target members by design; signatures are validated at resolve time.")]
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011", Justification = "Concord resolves private target members by design; signatures are validated at resolve time.")]
    private const BindingFlags TargetMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static InjectedMemberMap Build(Type declarationType, MethodBase target) {
        Type targetType = target.DeclaringType!;
        Dictionary<string, FieldInfo> fields = ShadowResolver.BuildFieldMap(declarationType, targetType);
        Dictionary<string, MethodInfo?> methods = new Dictionary<string, MethodInfo?>();

        AddInjectedInstance(declarationType, target, methods);
        AddInjectedFields(declarationType, targetType, fields);
        AddInjectedProperties(declarationType, targetType, methods);
        AddInjectedMethods(declarationType, targetType, methods);

        return new InjectedMemberMap(fields, methods);
    }

    private static void AddInjectedInstance(Type declarationType, MethodBase target, Dictionary<string, MethodInfo?> methods) {
        Type targetType = target.DeclaringType!;
        PropertyInfo? instanceProperty = null;

        foreach (PropertyInfo property in declarationType.GetProperties(DeclaredMembers)) {
            if (property.GetCustomAttribute<InjectInstanceAttribute>() is null) {
                continue;
            }

            if (instanceProperty is not null) {
                throw Error(CodeCONC070, "Only one [InjectInstance] property is allowed on declaration '" + declarationType.Name + "'.");
            }

            instanceProperty = property;
        }

        if (instanceProperty is null) {
            return;
        }

        MethodInfo? getter = instanceProperty.GetGetMethod(true);
        if (getter is null || instanceProperty.GetSetMethod(true) is not null || getter.IsStatic) {
            throw Error(CodeCONC070, "[InjectInstance] on '" + declarationType.Name + "." + instanceProperty.Name + "' must be a non-static get-only property.");
        }

        if (target.IsStatic) {
            throw Error(CodeCONC074, "[InjectInstance] cannot be used when patching static target method '" + targetType.Name + "." + target.Name + "'.");
        }

        if (targetType.IsValueType) {
            throw Error(CodeCONC074, "[InjectInstance] does not support value-type target '" + targetType.Name + "'.");
        }

        if (!instanceProperty.PropertyType.IsAssignableFrom(targetType)) {
            throw Error(
                CodeCONC072,
                "[InjectInstance] property '" + declarationType.Name + "." + instanceProperty.Name + HasType +
                instanceProperty.PropertyType.FullName +
                "', which cannot receive target instance type '" +
                targetType.FullName +
                "'.");
        }

        methods[InjectedMemberMap.MethodKey(getter)] = null;
    }

    private static void AddInjectedFields(Type declarationType, Type targetType, Dictionary<string, FieldInfo> fields) {
        foreach (FieldInfo declarationField in declarationType.GetFields(DeclaredMembers)) {
            InjectFieldAttribute? attribute = declarationField.GetCustomAttribute<InjectFieldAttribute>();
            if (attribute is null) {
                continue;
            }

            string targetName = attribute.TargetName ?? declarationField.Name;
            FieldInfo targetField = ResolveField(declarationType, targetType, declarationField, targetName);
            if (targetField.FieldType != declarationField.FieldType || targetField.IsStatic != declarationField.IsStatic) {
                throw Error(
                    CodeCONC072,
                    "Injected field declaration '" + declarationType.Name + "." + declarationField.Name + HasType +
                    declarationField.FieldType.FullName +
                    "' / static=" +
                    declarationField.IsStatic +
                    ", but target field '" +
                    targetType.Name +
                    "." +
                    targetName +
                    HasType +
                    targetField.FieldType.FullName +
                    "' / static=" +
                    targetField.IsStatic +
                    ". Signatures must match exactly.");
            }

            fields[declarationField.Name] = targetField;
        }
    }

    private static void AddInjectedProperties(Type declarationType, Type targetType, Dictionary<string, MethodInfo?> methods) {
        foreach (PropertyInfo declarationProperty in declarationType.GetProperties(DeclaredMembers)) {
            InjectPropertyAttribute? attribute = declarationProperty.GetCustomAttribute<InjectPropertyAttribute>();
            if (attribute is null) {
                continue;
            }

            string targetName = attribute.TargetName ?? declarationProperty.Name;
            Type[] indexTypes = ParameterTypes(declarationProperty.GetIndexParameters());
            PropertyInfo targetProperty = ResolveProperty(declarationType, targetType, declarationProperty, targetName, indexTypes);

            if (targetProperty.PropertyType != declarationProperty.PropertyType) {
                throw Error(
                    CodeCONC072,
                    InjectedPropertyDeclaration + declarationType.Name + "." + declarationProperty.Name + HasType +
                    declarationProperty.PropertyType.FullName +
                    "', but target property '" +
                    targetType.Name +
                    "." +
                    targetName +
                    HasType +
                    targetProperty.PropertyType.FullName +
                    "'. Signatures must match exactly.");
            }

            MapAccessor(declarationType, targetType, declarationProperty, targetProperty, targetName, true, methods);
            MapAccessor(declarationType, targetType, declarationProperty, targetProperty, targetName, false, methods);
        }
    }

    private static void AddInjectedMethods(Type declarationType, Type targetType, Dictionary<string, MethodInfo?> methods) {
        foreach (MethodInfo declarationMethod in declarationType.GetMethods(DeclaredMembers)) {
            InjectMethodAttribute? attribute = declarationMethod.GetCustomAttribute<InjectMethodAttribute>();
            if (attribute is null) {
                continue;
            }

            string targetName = attribute.TargetName ?? declarationMethod.Name;
            Type[] parameterTypes = ParameterTypes(declarationMethod.GetParameters());
            MethodInfo targetMethod = ResolveMethod(declarationType, targetType, declarationMethod, targetName, parameterTypes);

            if (targetMethod.ReturnType != declarationMethod.ReturnType ||
                targetMethod.IsStatic != declarationMethod.IsStatic ||
                targetMethod.ContainsGenericParameters != declarationMethod.ContainsGenericParameters) {
                throw Error(
                    CodeCONC072,
                    "[InjectMethod] declaration '" + declarationType.Name + "." + declarationMethod.Name + "' does not match target method '" +
                    targetType.Name +
                    "." +
                    targetName +
                    "'. Return type, static-ness, generic arity, and parameter types must match exactly.");
            }

            methods[InjectedMemberMap.MethodKey(declarationMethod)] = targetMethod;
        }
    }

    private static FieldInfo ResolveField(Type declarationType, Type targetType, FieldInfo declarationField, string targetName) {
        try {
            FieldInfo? targetField = targetType.GetField(targetName, TargetMembers);
            if (targetField is not null) {
                return targetField;
            }
        } catch (AmbiguousMatchException) {
            throw Error(CodeCONC073, "Injected field declaration '" + declarationType.Name + "." + declarationField.Name + "' ambiguously resolves target field '" + targetType.Name + "." + targetName + "'.");
        }

        throw Error(CodeCONC071, "Injected field declaration '" + declarationType.Name + "." + declarationField.Name + "' could not find target field '" + targetType.Name + "." + targetName + "'.");
    }

    private static PropertyInfo ResolveProperty(Type declarationType, Type targetType, PropertyInfo declarationProperty, string targetName, Type[] indexTypes) {
        try {
            PropertyInfo? targetProperty = targetType.GetProperty(targetName, TargetMembers, null, declarationProperty.PropertyType, indexTypes, null);
            if (targetProperty is not null) {
                return targetProperty;
            }
        } catch (AmbiguousMatchException) {
            throw Error(CodeCONC073, InjectedPropertyDeclaration + declarationType.Name + "." + declarationProperty.Name + "' ambiguously resolves target property '" + targetType.Name + "." + targetName + "'.");
        }

        throw Error(CodeCONC071, InjectedPropertyDeclaration + declarationType.Name + "." + declarationProperty.Name + "' could not find target property '" + targetType.Name + "." + targetName + "'.");
    }

    private static MethodInfo ResolveMethod(Type declarationType, Type targetType, MethodInfo declarationMethod, string targetName, Type[] parameterTypes) {
        try {
            MethodInfo? targetMethod = targetType.GetMethod(targetName, TargetMembers, null, parameterTypes, null);
            if (targetMethod is not null) {
                return targetMethod;
            }
        } catch (AmbiguousMatchException) {
            throw Error(CodeCONC073, "[InjectMethod] declaration '" + declarationType.Name + "." + declarationMethod.Name + "' ambiguously resolves target method '" + targetType.Name + "." + targetName + "'.");
        }

        throw Error(CodeCONC071, "[InjectMethod] declaration '" + declarationType.Name + "." + declarationMethod.Name + "' could not find target method '" + targetType.Name + "." + targetName + "'.");
    }

    private static void MapAccessor(
        Type declarationType,
        Type targetType,
        PropertyInfo declarationProperty,
        PropertyInfo targetProperty,
        string targetName,
        bool getter,
        Dictionary<string, MethodInfo?> methods) {
        MethodInfo? declarationAccessor = getter ? declarationProperty.GetGetMethod(true) : declarationProperty.GetSetMethod(true);
        if (declarationAccessor is null) {
            return;
        }

        MethodInfo? targetAccessor = getter ? targetProperty.GetGetMethod(true) : targetProperty.GetSetMethod(true);
        if (targetAccessor is null) {
            throw Error(
                "CONC071",
                "Injected property '" +
                declarationType.Name +
                "." +
                declarationProperty.Name +
                "' requires a " +
                (getter ? "getter" : "setter") +
                ", but target property '" +
                targetType.Name +
                "." +
                targetName +
                "' does not expose one.");
        }

        if (targetAccessor.IsStatic != declarationAccessor.IsStatic) {
            throw Error(
                CodeCONC072,
                InjectedPropertyDeclaration + declarationType.Name + "." + declarationProperty.Name + "' static-ness does not match target property '" +
                targetType.Name +
                "." +
                targetName +
                "'.");
        }

        methods[InjectedMemberMap.MethodKey(declarationAccessor)] = targetAccessor;
    }

    private static Type[] ParameterTypes(ParameterInfo[] parameters) {
        Type[] result = new Type[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            result[i] = parameters[i].ParameterType;
        }

        return result;
    }

    private static ConcordEmitException Error(string code, string message) {
        return new ConcordEmitException(code, message);
    }
}
