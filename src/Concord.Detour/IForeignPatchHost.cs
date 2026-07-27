using System.Reflection;
using Concord.Emit;

namespace Concord.Detour;

/// <summary>
///     A foreign patching library that Concord can share a method with. When another library already owns
///     a target's entry point, Concord hands its injections to the host instead of installing a second
///     detour, because two detours cannot both control one entry point.
/// </summary>
public interface IForeignPatchHost {
    /// <summary>
    ///     Takes the host's own patching lock, so Concord can establish one global lock order and avoid
    ///     deadlocking against a host rebuild running on another thread. Reentrant on the same thread.
    /// </summary>
    /// <returns>A scope that releases the lock when disposed, or null when the host has no reachable lock.</returns>
    IDisposable? EnterHostLock();

    /// <summary>
    ///     Reports whether the host could take over a target, without changing anything. Used before a
    ///     live promotion, so a refusal never has to unwind a detour that was already removed.
    /// </summary>
    /// <param name="target">The method to test.</param>
    /// <param name="added">The injections that would be handed over.</param>
    /// <returns>Null when the target is routable, otherwise the reason it is not.</returns>
    string? ValidateRoute(MethodBase target, IReadOnlyList<Injection> added);

    /// <summary>
    ///     Asks the host whether it needs to take over <paramref name="target" />, and hands over the
    ///     injections when it does.
    /// </summary>
    /// <param name="target">The method Concord wants to patch.</param>
    /// <param name="added">The injections Concord contributes to the target.</param>
    /// <param name="forceRoute">Route even when no foreign patcher has claimed the target.</param>
    /// <returns>Whether the target was routed, left alone, or refused.</returns>
    ForeignRouteResult TryRoute(MethodBase target, IReadOnlyList<Injection> added, bool forceRoute);

    /// <summary>
    ///     Hands injections to the host by contributing to a rebuild that is already in flight, rather
    ///     than starting a new one. Used during promotion, where the host is mid-rebuild and will discard
    ///     anything registered by a nested call.
    /// </summary>
    /// <param name="target">The method being rebuilt.</param>
    /// <param name="added">The injections Concord contributes.</param>
    /// <param name="hostPatchState">The host's patch record for the in-flight rebuild.</param>
    /// <returns>Routed when the host accepted the contribution, otherwise the reason it did not.</returns>
    ForeignRouteResult RouteInto(MethodBase target, IReadOnlyList<Injection> added, object hostPatchState);

    /// <summary>
    ///     Adds more injections to a target the host has already taken over.
    /// </summary>
    /// <param name="target">The routed method.</param>
    /// <param name="added">The injections to add.</param>
    /// <returns>A handle that removes only these injections when disposed.</returns>
    IDetourHandle ApplyToRouted(MethodBase target, IReadOnlyList<Injection> added);

    /// <summary>
    ///     Lists the identifiers of foreign patch owners on a target, ignoring Concord's own.
    /// </summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>The foreign owner identifiers, empty when none.</returns>
    IReadOnlyList<string> ForeignOwners(MethodBase target);

    /// <summary>
    ///     Hooks the host so it notifies <paramref name="observer" /> before it rebuilds any method.
    ///     Without this, Concord only learns about a foreign patch by polling after the fact.
    /// </summary>
    /// <param name="observer">The observer to notify.</param>
    /// <param name="rawBackend">
    ///     The plain backend to install the hook with. This must not be the routing backend: routing the
    ///     foreign library's own internals through the foreign library is circular.
    /// </param>
    /// <returns>True when the hook installed; false leaves Concord on poll-based detection.</returns>
    bool TryInstallNotifier(IForeignPatchObserver observer, IDetourBackend rawBackend);
}
