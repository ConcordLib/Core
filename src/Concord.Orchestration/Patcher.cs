using System.Diagnostics;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Concord.Orchestration;

namespace Concord;

/// <summary>
///     The high-level entry point for applying Concord patches. Wraps the scan + compose + detour-apply
///     pipeline so mod authors apply an assembly's <see cref="PatchAttribute" /> declarations, or a single
///     explicit patch, with one call and revert by disposing the returned <see cref="IPatchHandle" />.
/// </summary>
public static class Patcher {
    private static readonly object Gate = new object();
    private static readonly Dictionary<Assembly, IPatchHandle> Applied = [];
    private static readonly HashSet<IPatchHandle> Live = [];
    private static readonly AttachedPropertyStore Properties = new AttachedPropertyStore();

    /// <summary>
    ///     Applies every <see cref="PatchAttribute" /> declaration in <paramref name="assembly" />, composing each
    ///     as a detour and registering each declared field as an attached property. Idempotent: a repeated call
    ///     with the same assembly returns the same handle without applying twice.
    /// </summary>
    /// <param name="assembly">The assembly whose declarations are applied.</param>
    /// <returns>A handle that reverts every applied detour when disposed.</returns>
    public static IPatchHandle Apply(Assembly assembly) {
        lock (Gate) {
            if (Applied.TryGetValue(assembly, out IPatchHandle? existing)) {
                return existing;
            }

            CollectingPatchApplier applier = new CollectingPatchApplier();
            try {
                PatchDeclarationScanner.ScanAssembly(assembly, applier, Properties);
            } catch {
                foreach (IDetourHandle handle in applier.Handles) {
                    try {
                        handle.Dispose();
                    } catch (Exception ex) {
                        Debug.WriteLine("[Concord] rollback dispose failed: " + ex.Message);
                    }
                }

                throw;
            }

            PatchHandle patchHandle = new PatchHandle(applier.Handles, () => Remove(assembly));
            Applied[assembly] = patchHandle;
            return patchHandle;
        }
    }

    /// <summary>
    ///     Applies a single explicit patch without scanning for attributes: composes
    ///     <paramref name="injectionMethod" /> onto <paramref name="target" /> at the given position.
    /// </summary>
    /// <remarks>
    ///     The returned handle is retained in a static registry, so the detour stays applied even when the
    ///     caller discards the handle. Dispose the handle to revert the detour and release it from the registry.
    /// </remarks>
    /// <param name="target">The method to patch.</param>
    /// <param name="injectionMethod">The injection method supplying the injected body.</param>
    /// <param name="at">The injection position where the injection method runs relative to the target.</param>
    /// <returns>A handle that reverts the applied detour when disposed.</returns>
    public static IPatchHandle Patch(MethodBase target, MethodBase injectionMethod, At at) {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        Injection injection = new Injection(injectionMethod, ToInjectAt(at), injectionMethod.DeclaringType!.FullName!, 0);
        applier.ApplyPatch(target, injection);
        return RegisterLive(applier.Handles);
    }

    /// <summary>
    ///     Creates a fluent <see cref="PatchBuilder" /> targeting the given method.
    /// </summary>
    /// <param name="target">The method to patch.</param>
    /// <returns>A <see cref="PatchBuilder" /> configured for <paramref name="target" />.</returns>
    public static PatchBuilder For(MethodBase target) {
        return new PatchBuilder(target);
    }

    /// <summary>
    ///     Creates a fluent <see cref="PatchBuilder" /> targeting a method by name on the given type.
    /// </summary>
    /// <param name="type">The type that declares the method.</param>
    /// <param name="method">The name of the method to target.</param>
    /// <returns>A <see cref="PatchBuilder" /> configured for the resolved method.</returns>
    /// <exception cref="ConcordDeclarationException">Thrown when the method is not found or is ambiguous.</exception>
    public static PatchBuilder For(Type type, string method) {
        return new PatchBuilder(ResolveMethod(type, method));
    }

    /// <summary>
    ///     Creates a fluent <see cref="PatchBuilder" /> targeting a method by name on the given type,
    ///     disambiguating by parameter types.
    /// </summary>
    /// <param name="type">The type that declares the method.</param>
    /// <param name="method">The name of the method to target.</param>
    /// <param name="parameterTypes">The parameter types used to select a specific overload.</param>
    /// <returns>A <see cref="PatchBuilder" /> configured for the resolved method.</returns>
    /// <exception cref="ConcordDeclarationException">Thrown when the method is not found.</exception>
    public static PatchBuilder For(Type type, string method, Type[] parameterTypes) {
        return new PatchBuilder(ResolveMethod(type, method, parameterTypes));
    }

    /// <summary>
    ///     Creates a fluent <see cref="PatchBuilder" /> targeting a method by name on <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">The type that declares the method.</typeparam>
    /// <param name="method">The name of the method to target.</param>
    /// <returns>A <see cref="PatchBuilder" /> configured for the resolved method.</returns>
    /// <exception cref="ConcordDeclarationException">Thrown when the method is not found or is ambiguous.</exception>
    public static PatchBuilder For<T>(string method) {
        return For(typeof(T), method);
    }

    /// <summary>
    ///     Creates a fluent <see cref="PatchBuilder" /> targeting a method by name on <typeparamref name="T" />,
    ///     disambiguating by parameter types.
    /// </summary>
    /// <typeparam name="T">The type that declares the method.</typeparam>
    /// <param name="method">The name of the method to target.</param>
    /// <param name="parameterTypes">The parameter types used to select a specific overload.</param>
    /// <returns>A <see cref="PatchBuilder" /> configured for the resolved method.</returns>
    /// <exception cref="ConcordDeclarationException">Thrown when the method is not found.</exception>
    public static PatchBuilder For<T>(string method, Type[] parameterTypes) {
        return For(typeof(T), method, parameterTypes);
    }

    /// <summary>
    ///     Creates a fluent <see cref="PatchBuilder" /> targeting a method by name on a type resolved by name.
    /// </summary>
    /// <param name="typeName">The full name of the type that declares the method.</param>
    /// <param name="method">The name of the method to target.</param>
    /// <returns>A <see cref="PatchBuilder" /> configured for the resolved method.</returns>
    /// <exception cref="ConcordDeclarationException">Thrown when the type or method is not found or is ambiguous.</exception>
    public static PatchBuilder For(string typeName, string method) {
        Type type = PatchDeclarationScanner.ResolveTypeByName(typeName);
        return new PatchBuilder(ResolveMethod(type, method));
    }

    /// <summary>
    ///     Creates a fluent <see cref="PatchBuilder" /> targeting a method by name on a type resolved by name,
    ///     disambiguating by parameter types.
    /// </summary>
    /// <param name="typeName">The full name of the type that declares the method.</param>
    /// <param name="method">The name of the method to target.</param>
    /// <param name="parameterTypes">The parameter types used to select a specific overload.</param>
    /// <returns>A <see cref="PatchBuilder" /> configured for the resolved method.</returns>
    /// <exception cref="ConcordDeclarationException">Thrown when the type or method is not found.</exception>
    public static PatchBuilder For(string typeName, string method, Type[] parameterTypes) {
        Type type = PatchDeclarationScanner.ResolveTypeByName(typeName);
        return new PatchBuilder(ResolveMethod(type, method, parameterTypes));
    }

    /// <summary>
    ///     Creates a fluent <see cref="PatchBuilder" /> targeting a specific instance constructor on
    ///     <paramref name="type" />, selected by parameter types.
    /// </summary>
    /// <param name="type">The type that declares the constructor.</param>
    /// <param name="parameterTypes">
    ///     The parameter types of the constructor to target. Pass an empty array to select the
    ///     parameterless constructor. Only instance constructors are supported.
    /// </param>
    /// <returns>A <see cref="PatchBuilder" /> configured for the resolved constructor.</returns>
    /// <exception cref="ConcordDeclarationException">Thrown when no matching instance constructor is found.</exception>
    public static PatchBuilder ForConstructor(Type type, Type[] parameterTypes) {
        return new PatchBuilder(ResolveConstructor(type, parameterTypes));
    }

    /// <summary>
    ///     Creates a fluent <see cref="PatchBuilder" /> targeting a specific instance constructor on
    ///     <typeparamref name="T" />, selected by parameter types.
    /// </summary>
    /// <typeparam name="T">The type that declares the constructor.</typeparam>
    /// <param name="parameterTypes">
    ///     The parameter types of the constructor to target. Pass an empty array to select the
    ///     parameterless constructor. Only instance constructors are supported.
    /// </param>
    /// <returns>A <see cref="PatchBuilder" /> configured for the resolved constructor.</returns>
    /// <exception cref="ConcordDeclarationException">Thrown when no matching instance constructor is found.</exception>
    public static PatchBuilder ForConstructor<T>(Type[] parameterTypes) {
        return ForConstructor(typeof(T), parameterTypes);
    }

    internal static IPatchHandle RegisterLive(IReadOnlyList<IDetourHandle> detours) {
        PatchHandle handle = null!;
        handle = new PatchHandle(detours, () => Unregister(handle));
        lock (Gate) {
            Live.Add(handle);
        }

        return handle;
    }

    private static void Remove(Assembly assembly) {
        lock (Gate) {
            Applied.Remove(assembly);
        }
    }

    private static void Unregister(IPatchHandle handle) {
        lock (Gate) {
            Live.Remove(handle);
        }
    }

    private static InjectAt ToInjectAt(At at) {
        return at switch {
            At.Return => new InjectAt.Return(0),
            At.Tail => new InjectAt.Tail(),
            At.Around => new InjectAt.Around(),
            _ => new InjectAt.Head(),
        };
    }

    private static MethodBase ResolveMethod(Type type, string method) {
        MethodInfo? resolved;
        try {
            resolved = type.GetMethod(
                method,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); // NOSONAR concord reaches private target members by design; validated at resolve time
        } catch (AmbiguousMatchException) {
            throw new ConcordDeclarationException(
                "Method '" +
                method +
                "' on " +
                type.FullName +
                " is ambiguous (multiple overloads); use the parameterTypes overload to disambiguate.");
        }

        if (resolved == null) {
            throw new ConcordDeclarationException("Method '" + method + "' not found on " + type.FullName + ".");
        }

        return resolved;
    }

    private static MethodBase ResolveMethod(Type type, string method, Type[] parameterTypes) {
        MethodInfo? resolved = type.GetMethod(
            method,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, // NOSONAR concord reaches private target members by design; validated at resolve time
            null,
            parameterTypes,
            null);
        if (resolved == null) {
            throw new ConcordDeclarationException(
                "Method '" + method + "' with specified parameter types not found on " + type.FullName + ".");
        }

        return resolved;
    }

    private static ConstructorInfo ResolveConstructor(Type type, Type[] parameterTypes) {
        ConstructorInfo? resolved = type.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, // NOSONAR concord reaches private target members by design; validated at resolve time
            null,
            parameterTypes,
            null);
        if (resolved == null) {
            throw new ConcordDeclarationException(
                "No instance constructor with the specified parameter types was found on " +
                type.FullName +
                ". Only instance constructors are supported; static constructors (.cctor) cannot be patched.");
        }

        return resolved;
    }
}
