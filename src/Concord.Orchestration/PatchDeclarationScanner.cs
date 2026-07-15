using System.Diagnostics;
using System.Reflection;
using Concord.Emit;

namespace Concord.Orchestration;

/// <summary>
///     The high-level Concord scanner. Reads classes marked with <see cref="PatchAttribute" /> as a unified
///     patch declarations, then forwards their <c>[Inject]</c> methods for patch application and their
///     declared fields for attached-property registration.
/// </summary>
public static class PatchDeclarationScanner {
    private const string InjectOnPrefix = "[Inject] on ";

    /// <summary>
    ///     Scans every type in <paramref name="assembly" /> and processes each <see cref="PatchAttribute" />
    ///     declaration. Per-type <see cref="ConcordDeclarationException" />s are swallowed so one bad
    ///     declaration cannot abort scanning the rest of the assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="patches">The applier that applies patches.</param>
    /// <param name="props">The registry that registers attached-properties.</param>
    public static void ScanAssembly(Assembly assembly, IPatchApplier patches, IAttachedPropertyRegistry props) {
        ScanDeclarations(assembly.GetTypes(), patches, props);
    }

    /// <summary>
    ///     Scans the given declaration types, tolerating per-declaration failures exactly like
    ///     <see cref="ScanAssembly" />.
    /// </summary>
    /// <param name="declarations">The declaration types to scan.</param>
    /// <param name="patches">The applier that applies patches.</param>
    /// <param name="props">The registry that registers attached-properties.</param>
    public static void ScanDeclarations(IEnumerable<Type> declarations, IPatchApplier patches, IAttachedPropertyRegistry props) {
        foreach (Type type in declarations) {
            try {
                ScanType(type, patches, props);
            } catch (ConcordDeclarationException ex) {
                Debug.WriteLine("[Concord] declaration error in " + type.FullName + ": " + ex.Message);
                Console.Error.WriteLine("[Concord] declaration error in " + type.FullName + ": " + ex.Message);
            }
        }
    }

    /// <summary>
    ///     Resolves the assembly's generated patch registry, when one is present.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <param name="declarations">The registry's declaration list, or empty when absent.</param>
    /// <returns><see langword="true" /> when a registry was found and resolved.</returns>
    /// <exception cref="ConcordDeclarationException">
    ///     Thrown when the registry type does not implement <see cref="IPatchDeclarationProvider" />.
    /// </exception>
    public static bool TryGetRegistryDeclarations(Assembly assembly, out IReadOnlyList<Type> declarations) {
        PatchRegistryAttribute? attribute = assembly.GetCustomAttribute<PatchRegistryAttribute>();
        if (attribute is null) {
            declarations = [];
            return false;
        }

        if (Activator.CreateInstance(attribute.RegistryType) is not IPatchDeclarationProvider provider) {
            throw new ConcordDeclarationException(
                "[PatchRegistry] type '" + attribute.RegistryType.FullName + "' does not implement IPatchDeclarationProvider.");
        }

        declarations = provider.Declarations;
        return true;
    }

    /// <summary>
    ///     Scans a single declaration: its <c>[Inject]</c> methods become patches and its declared fields
    ///     become attached-properties on the resolved target base type. No-op when the type is not marked
    ///     with <see cref="PatchAttribute" />.
    /// </summary>
    /// <param name="declaration">The declaration class.</param>
    /// <param name="patches">The applier that applies patches.</param>
    /// <param name="props">The registry that registers attached-properties.</param>
    /// <exception cref="ConcordDeclarationException">
    ///     Thrown when an explicit target disagrees with the class's base type, or a bare declaration has no
    ///     usable target base type.
    /// </exception>
    public static void ScanType(Type declaration, IPatchApplier patches, IAttachedPropertyRegistry props) {
        if (!declaration.IsClass) {
            return;
        }

        if (!TryReadPatchAttribute(declaration, out PatchAttribute? attribute)) {
            return;
        }

        Type? explicitTarget = attribute!.Target;
        if (explicitTarget == null && attribute.TargetTypeName != null) {
            explicitTarget = ResolveTypeByName(declaration, attribute.TargetTypeName);
        }

        Type baseType = ResolveBaseType(declaration, explicitTarget);
        bool debug = declaration.GetCustomAttribute<PatchDebugAttribute>() is not null;
        string[] beforeOwners = ReadOrderOwners(
            declaration,
            declaration.GetCustomAttributes<PatchBeforeAttribute>().Select(static order => order.Owner),
            "[PatchBefore]");
        string[] afterOwners = ReadOrderOwners(
            declaration,
            declaration.GetCustomAttributes<PatchAfterAttribute>().Select(static order => order.Owner),
            "[PatchAfter]");

        List<(MethodBase Target, Injection Injection)> resolved =
            ResolveInjections(declaration, baseType, debug, beforeOwners, afterOwners);

        foreach ((MethodBase target, Injection injection) in resolved) {
            patches.ApplyPatch(target, injection);
        }

        RegisterAttachedProperties(declaration, baseType, props);
    }

    /// <summary>
    ///     Resolves a type by its full name across all loaded assemblies.
    /// </summary>
    /// <param name="name">The full type name to resolve.</param>
    /// <returns>The resolved <see cref="Type" />.</returns>
    /// <exception cref="ConcordDeclarationException">
    ///     Thrown when the type cannot be resolved from any loaded assembly.
    /// </exception>
    internal static Type ResolveTypeByName(string name) {
        Type? resolved = Type.GetType(name);
        if (resolved != null) {
            return resolved;
        }

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
            resolved = asm.GetType(name);
            if (resolved != null) {
                return resolved;
            }
        }

        throw new ConcordDeclarationException("Target type '" + name + "' could not be resolved from any loaded assembly.");
    }

    private static List<(MethodBase Target, Injection Injection)> ResolveInjections(
        Type declaration,
        Type baseType,
        bool debug,
        string[] beforeOwners,
        string[] afterOwners) {
        const BindingFlags declared = BindingFlags.Public |
                                      BindingFlags.NonPublic | // NOSONAR concord reaches private target members by design; validated at resolve time
                                      BindingFlags.Instance |
                                      BindingFlags.Static |
                                      BindingFlags.DeclaredOnly;

        List<(MethodBase Target, Injection Injection)> resolved = [];
        foreach (MethodInfo method in declaration.GetMethods(declared)) {
            InjectAttribute? inject = method.GetCustomAttribute<InjectAttribute>();
            if (inject == null) {
                continue;
            }

            ValidateInjectAttribute(declaration, inject);

            MethodBase resolvedTarget = ResolveInjectionTarget(declaration, baseType, inject);
            resolved.Add((resolvedTarget, new Injection(method, inject.ResolvedAt, declaration.FullName!, inject.ResolvedPriority) {
                Debug = debug,
                BeforeOwners = beforeOwners,
                AfterOwners = afterOwners
            }));
        }

        return resolved;
    }

    private static void ValidateInjectAttribute(Type declaration, InjectAttribute inject) {
        if (inject.HasConstant && inject.At != Concord.At.Constant) {
            throw new ConcordDeclarationException(
                InjectOnPrefix + declaration.FullName + " passes a constant but position " + inject.At + "; constant injections require At.Constant.");
        }

        if (!inject.HasConstant &&
            inject.ResolvedAt is not InjectAt.Invoke &&
            (inject.At == Concord.At.Constant || inject.At == Concord.At.Argument)) {
            throw new ConcordDeclarationException(
                InjectOnPrefix + declaration.FullName + " uses position " + inject.At + " without its dedicated constructor form.");
        }
    }

    private static void RegisterAttachedProperties(Type declaration, Type baseType, IAttachedPropertyRegistry props) {
        const BindingFlags declared = BindingFlags.Public |
                                      BindingFlags.NonPublic | // NOSONAR concord reaches private target members by design; validated at resolve time
                                      BindingFlags.Instance |
                                      BindingFlags.Static |
                                      BindingFlags.DeclaredOnly;

        foreach (FieldInfo field in declaration.GetFields(declared)) {
            if (field.IsStatic) {
                continue;
            }

            if (field.GetCustomAttribute<InjectFieldAttribute>() is not null) {
                continue;
            }

            props.RegisterAttachedProperty(baseType, field.Name, field.FieldType);
        }
    }

    private static string[] ReadOrderOwners(Type declaration, IEnumerable<string> owners, string attributeName) {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        List<string> resolved = [];
        foreach (string owner in owners) {
            if (string.IsNullOrWhiteSpace(owner)) {
                throw new ConcordDeclarationException(
                    attributeName + " on " + declaration.FullName + " requires a non-empty owner.");
            }

            if (seen.Add(owner)) {
                resolved.Add(owner);
            }
        }

        return resolved.ToArray();
    }

    private static MethodBase ResolveInjectionTarget(Type declaration, Type baseType, InjectAttribute inject) {
        if (inject.TargetsConstructor) {
            Type[] ctorParams = inject.ParameterTypes ?? Type.EmptyTypes;
            ConstructorInfo? ctor = baseType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, // NOSONAR concord reaches private target members by design; validated at resolve time
                null,
                ctorParams,
                null);
            if (ctor == null) {
                throw new ConcordDeclarationException(
                    InjectOnPrefix +
                    declaration.FullName +
                    " targets a constructor, but no instance constructor with the specified parameter types was found on " +
                    baseType.FullName +
                    ". Only instance constructors are supported.");
            }

            return ctor;
        }

        string methodName;
        try {
            methodName = AccessorNameResolver.ResolveAccessorName(baseType, inject.Method!, null, false);
        } catch (ConcordEmitException ex) {
            throw new ConcordDeclarationException(InjectOnPrefix + declaration.FullName + ": " + ex.Message);
        }

        if (inject.ParameterTypes != null) {
            MethodInfo? baseMethod = baseType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, // NOSONAR concord reaches private target members by design; validated at resolve time
                null,
                inject.ParameterTypes,
                null);
            if (baseMethod == null) {
                throw new ConcordDeclarationException(
                    InjectOnPrefix +
                    declaration.FullName +
                    " target '" +
                    inject.Method +
                    "' with specified parameter types not found on " +
                    baseType.FullName +
                    ".");
            }

            return baseMethod;
        }

        MethodInfo? byName;
        try {
            byName = baseType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); // NOSONAR concord reaches private target members by design; validated at resolve time
        } catch (AmbiguousMatchException) {
            throw new ConcordDeclarationException(
                InjectOnPrefix +
                declaration.FullName +
                " target '" +
                inject.Method +
                "' is ambiguous (multiple overloads); use the parameterTypes overload to disambiguate.");
        }

        if (byName == null) {
            throw new ConcordDeclarationException(
                InjectOnPrefix +
                declaration.FullName +
                " names method '" +
                inject.Method +
                "' which does not exist on " +
                baseType.FullName +
                ".");
        }

        return byName;
    }

    private static bool TryReadPatchAttribute(Type declaration, out PatchAttribute? attribute) {
        attribute = null;

        try {
            attribute = declaration.GetCustomAttribute<PatchAttribute>();
        } catch (TypeLoadException) {
            return false;
        } catch (FileNotFoundException) {
            return false;
        } catch (FileLoadException) {
            return false;
        }

        return attribute != null;
    }

    private static Type ResolveBaseType(Type declaration, Type? explicitTarget) {
        Type? csBase = declaration.BaseType;
        bool hasGameBase = csBase != null && csBase != typeof(object) && !IsBclType(csBase);

        if (explicitTarget != null) {
            if (hasGameBase && csBase != explicitTarget) {
                throw new ConcordDeclarationException(
                    "[Patch] on " +
                    declaration.FullName +
                    " names target " +
                    explicitTarget.FullName +
                    " but the class derives from " +
                    csBase!.FullName +
                    ". Remove the explicit target or " +
                    "make it match the base type.");
            }

            return explicitTarget;
        }

        if (hasGameBase) {
            return csBase!;
        }

        throw new ConcordDeclarationException(
            "[Patch] on " +
            declaration.FullName +
            " has no target base type and no [Patch(typeof(T))] " +
            "or [Patch(\"TypeName\")] target. Either derive from the target type or pass it as " +
            "[Patch(typeof(T))] or [Patch(\"Full.Type.Name\")].");
    }

    private static Type ResolveTypeByName(Type declaration, string name) {
        try {
            return ResolveTypeByName(name);
        } catch (ConcordDeclarationException) {
            throw new ConcordDeclarationException(
                "[Patch] on " + declaration.FullName + ": target type '" + name + "' could not be resolved from any loaded assembly.");
        }
    }

    private static bool IsBclType(Type type) {
        string? ns = type.Namespace;
        if (ns == null) {
            return false;
        }

        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);
    }
}
