using System.Reflection;
using Concord.Emit;
using Concord.Orchestration;

namespace Concord;

/// <summary>
///     A fluent builder that accumulates one or more injections for a single target method and applies
///     them all in one <see cref="Apply" /> call. Create instances via <see cref="Patcher.For(MethodBase)" />
///     and its overloads.
/// </summary>
public sealed class PatchBuilder {
    private readonly List<Injection> injections = [];
    private readonly MethodBase target;

    internal PatchBuilder(MethodBase target) {
        this.target = target;
    }

    /// <summary>
    ///     Records a head, tail, return, or around injection at the given position using the supplied injection method.
    ///     For an invoke-splice injection use <see cref="Invoke(Type, string, MethodInfo, At, uint)" />.
    /// </summary>
    /// <param name="at">The injection position. <see cref="At.Return" /> targets every return.</param>
    /// <param name="injectionMethod">The method that supplies the injected body.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Inject(At at, MethodInfo injectionMethod) {
        return Inject(ToInjectAt(at), injectionMethod);
    }

    /// <summary>
    ///     Records a head, tail, return, or around injection at the given position, resolving the injection method by
    ///     name on <paramref name="injectionMethodType" />.
    /// </summary>
    /// <param name="at">The injection position. <see cref="At.Return" /> targets every return.</param>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when the injection method is not found.</exception>
    public PatchBuilder Inject(At at, Type injectionMethodType, string injectionMethodName) {
        return Inject(ToInjectAt(at), ResolveInjectionMethod(injectionMethodType, injectionMethodName));
    }

    /// <summary>Records a head-of-method injection using the supplied injection method.</summary>
    /// <param name="injectionMethod">The method that supplies the injected body.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Head(MethodInfo injectionMethod) {
        return Inject(new InjectAt.Head(), injectionMethod);
    }

    /// <summary>
    ///     Records a head-of-method injection, resolving the injection method by name on
    ///     <paramref name="injectionMethodType" />.
    /// </summary>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Head(Type injectionMethodType, string injectionMethodName) {
        return Inject(new InjectAt.Head(), injectionMethodType, injectionMethodName);
    }

    /// <summary>
    ///     Records a head-of-method injection, resolving the injection method by name on
    ///     <paramref name="injectionMethodType" />, disambiguating by parameter types.
    /// </summary>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <param name="parameterTypes">The parameter types used to select a specific overload.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Head(Type injectionMethodType, string injectionMethodName, Type[] parameterTypes) {
        return Inject(new InjectAt.Head(), ResolveInjectionMethod(injectionMethodType, injectionMethodName, parameterTypes));
    }

    /// <summary>Records a tail injection that runs once at the end of the target, using the supplied injection method.</summary>
    /// <param name="injectionMethod">The method that supplies the injected body.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Tail(MethodInfo injectionMethod) {
        return Inject(new InjectAt.Tail(), injectionMethod);
    }

    /// <summary>
    ///     Records a tail injection that runs once at the end of the target, resolving the injection method by name on
    ///     <paramref name="injectionMethodType" />.
    /// </summary>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Tail(Type injectionMethodType, string injectionMethodName) {
        return Inject(new InjectAt.Tail(), injectionMethodType, injectionMethodName);
    }

    /// <summary>
    ///     Records a tail injection that runs once at the end of the target, resolving the injection method by name on
    ///     <paramref name="injectionMethodType" />, disambiguating by parameter types.
    /// </summary>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <param name="parameterTypes">The parameter types used to select a specific overload.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Tail(Type injectionMethodType, string injectionMethodName, Type[] parameterTypes) {
        return Inject(new InjectAt.Tail(), ResolveInjectionMethod(injectionMethodType, injectionMethodName, parameterTypes));
    }

    /// <summary>Records a return-site injection that runs before each return in the target, using the supplied injection method.</summary>
    /// <param name="injectionMethod">The method that supplies the injected body.</param>
    /// <param name="by">The 1-based occurrence of the return to target, or <c>0</c> for every return.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Return(MethodInfo injectionMethod, uint by = 0) {
        return Inject(new InjectAt.Return(by), injectionMethod);
    }

    /// <summary>
    ///     Records a return-site injection that runs before each return in the target, resolving the injection method
    ///     by name on <paramref name="injectionMethodType" />.
    /// </summary>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <param name="by">The 1-based occurrence of the return to target, or <c>0</c> for every return.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Return(Type injectionMethodType, string injectionMethodName, uint by = 0) {
        return Inject(new InjectAt.Return(by), injectionMethodType, injectionMethodName);
    }

    /// <summary>
    ///     Records a return-site injection that runs before each return in the target, resolving the injection method
    ///     by name on <paramref name="injectionMethodType" />, disambiguating by parameter types.
    /// </summary>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <param name="by">The 1-based occurrence of the return to target, or <c>0</c> for every return.</param>
    /// <param name="parameterTypes">The parameter types used to select a specific overload.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Return(Type injectionMethodType, string injectionMethodName, uint by, Type[] parameterTypes) {
        return Inject(new InjectAt.Return(by), ResolveInjectionMethod(injectionMethodType, injectionMethodName, parameterTypes));
    }

    /// <summary>Records an around-call injection using the supplied injection method.</summary>
    /// <param name="injectionMethod">The method that supplies the injected body.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Around(MethodInfo injectionMethod) {
        return Inject(new InjectAt.Around(), injectionMethod);
    }

    /// <summary>
    ///     Records an around-call injection, resolving the injection method by name on
    ///     <paramref name="injectionMethodType" />.
    /// </summary>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Around(Type injectionMethodType, string injectionMethodName) {
        return Inject(new InjectAt.Around(), injectionMethodType, injectionMethodName);
    }

    /// <summary>
    ///     Records an around-call injection, resolving the injection method by name on
    ///     <paramref name="injectionMethodType" />, disambiguating by parameter types.
    /// </summary>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <param name="parameterTypes">The parameter types used to select a specific overload.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Around(Type injectionMethodType, string injectionMethodName, Type[] parameterTypes) {
        return Inject(new InjectAt.Around(), ResolveInjectionMethod(injectionMethodType, injectionMethodName, parameterTypes));
    }

    /// <summary>
    ///     Records an invoke-splice injection on a named member access inside the target, using the supplied
    ///     injection method. Head and Tail also support field reads.
    /// </summary>
    /// <param name="callSiteType">The type that declares the member access to splice.</param>
    /// <param name="callSiteMethod">The method, property, or field name to splice.</param>
    /// <param name="injectionMethod">The method that supplies the injected body.</param>
    /// <param name="shift">
    ///     Where the injection method runs relative to the matched access. Around supports method and property
    ///     calls only.
    /// </param>
    /// <param name="by">The 1-based occurrence to target, or <c>0</c> for every matching access.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Invoke(Type callSiteType, string callSiteMethod, MethodInfo injectionMethod, At shift, uint by = 0) {
        return Inject(new InjectAt.Invoke(callSiteType, callSiteMethod, shift, by), injectionMethod);
    }

    /// <summary>
    ///     Records an invoke-splice injection on a method or property call inside the target, using the supplied
    ///     injection method and disambiguating the call site by parameter types.
    /// </summary>
    /// <param name="callSiteType">The type that declares the call-site method to splice.</param>
    /// <param name="callSiteMethod">The name of the method to splice.</param>
    /// <param name="callSiteParameterTypes">
    ///     The parameter types of the call-site overload to target. Only call sites whose parameters match
    ///     this array are considered.
    /// </param>
    /// <param name="injectionMethod">The method that supplies the injected body.</param>
    /// <param name="shift">
    ///     Where the injection method runs relative to the matched call: <see cref="At.Head" /> before,
    ///     <see cref="At.Tail" /> after, or <see cref="At.Around" /> wrapping it.
    /// </param>
    /// <param name="by">The 1-based occurrence to target, or <c>0</c> for every matching call.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Invoke(Type callSiteType, string callSiteMethod, Type[] callSiteParameterTypes, MethodInfo injectionMethod, At shift, uint by = 0) {
        return Inject(new InjectAt.Invoke(callSiteType, callSiteMethod, shift, by, callSiteParameterTypes), injectionMethod);
    }

    /// <summary>
    ///     Records an invoke-splice injection on a named member access inside the target, resolving the injection
    ///     method by name. Head and Tail also support field reads.
    /// </summary>
    /// <param name="callSiteType">The type that declares the member access to splice.</param>
    /// <param name="callSiteMethod">The method, property, or field name to splice.</param>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <param name="shift">
    ///     Where the injection method runs relative to the matched access. Around supports method and property
    ///     calls only.
    /// </param>
    /// <param name="by">The 1-based occurrence to target, or <c>0</c> for every matching access.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Invoke(Type callSiteType, string callSiteMethod, Type injectionMethodType, string injectionMethodName, At shift, uint by = 0) {
        return Inject(new InjectAt.Invoke(callSiteType, callSiteMethod, shift, by), ResolveInjectionMethod(injectionMethodType, injectionMethodName));
    }

    /// <summary>
    ///     Records an invoke-splice injection on a method or property call inside the target, resolving the
    ///     injection method by name and disambiguating the call site by parameter types.
    /// </summary>
    /// <param name="callSiteType">The type that declares the call-site method to splice.</param>
    /// <param name="callSiteMethod">The name of the method to splice.</param>
    /// <param name="callSiteParameterTypes">
    ///     The parameter types of the call-site overload to target. Only call sites whose parameters match
    ///     this array are considered.
    /// </param>
    /// <param name="injectionMethodType">The type that declares the injection method.</param>
    /// <param name="injectionMethodName">The name of the injection method.</param>
    /// <param name="shift">
    ///     Where the injection method runs relative to the matched call: <see cref="At.Head" /> before,
    ///     <see cref="At.Tail" /> after, or <see cref="At.Around" /> wrapping it.
    /// </param>
    /// <param name="by">The 1-based occurrence to target, or <c>0</c> for every matching call.</param>
    /// <returns>This builder, for chaining.</returns>
    public PatchBuilder Invoke(Type callSiteType, string callSiteMethod, Type[] callSiteParameterTypes, Type injectionMethodType, string injectionMethodName, At shift, uint by = 0) {
        return Inject(new InjectAt.Invoke(callSiteType, callSiteMethod, shift, by, callSiteParameterTypes), ResolveInjectionMethod(injectionMethodType, injectionMethodName));
    }

    /// <summary>
    ///     Composes and applies all recorded injections onto the target method in a single operation,
    ///     returning a handle that reverts them all when disposed.
    /// </summary>
    /// <remarks>
    ///     The returned handle is retained in a static registry, so the detour stays applied even when the
    ///     caller discards the handle. Dispose the handle to revert the detour and release it from the registry.
    /// </remarks>
    /// <returns>A handle that reverts all applied detours when disposed.</returns>
    public IPatchHandle Apply() {
        List<(MethodBase, Injection)> items = new List<(MethodBase, Injection)>(injections.Count);
        foreach (Injection injection in injections) {
            items.Add((target, injection));
        }

        return Patcher.ApplyInjections(items);
    }

    internal PatchBuilder Inject(InjectAt at, MethodInfo injectionMethod) {
        if (injectionMethod.DeclaringType is null) {
            throw new ConcordDeclarationException("Injection method '" + injectionMethod.Name + "' has no declaring type; only methods declared on a type can be used as injections.");
        }

        injections.Add(new Injection(injectionMethod, at, injectionMethod.DeclaringType.FullName!, 0));
        return this;
    }

    internal PatchBuilder Inject(InjectAt at, Type injectionMethodType, string injectionMethodName) {
        return Inject(at, ResolveInjectionMethod(injectionMethodType, injectionMethodName));
    }

    private static MethodInfo ResolveInjectionMethod(Type injectionMethodType, string injectionMethodName) {
        MethodInfo? resolved = injectionMethodType.GetMethod(
            injectionMethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); // NOSONAR concord reaches private target members by design; validated at resolve time
        if (resolved == null) {
            throw new ArgumentException(
                "Injection method '" + injectionMethodName + "' not found on " + injectionMethodType.FullName + ".",
                nameof(injectionMethodName));
        }

        return resolved;
    }

    private static MethodInfo ResolveInjectionMethod(Type injectionMethodType, string injectionMethodName, Type[] parameterTypes) {
        MethodInfo? resolved = injectionMethodType.GetMethod(
            injectionMethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, // NOSONAR concord reaches private target members by design; validated at resolve time
            null,
            parameterTypes,
            null);
        if (resolved == null) {
            throw new ArgumentException(
                "Injection method '" + injectionMethodName + "' with specified parameter types not found on " + injectionMethodType.FullName + ".",
                nameof(injectionMethodName));
        }

        return resolved;
    }

    private static InjectAt ToInjectAt(At at) {
        return at switch {
            At.Return => new InjectAt.Return(0),
            At.Tail => new InjectAt.Tail(),
            At.Around => new InjectAt.Around(),
            _ => new InjectAt.Head(),
        };
    }
}
