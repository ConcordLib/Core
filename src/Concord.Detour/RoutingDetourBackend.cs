using System.Reflection;
using Concord.Emit;

namespace Concord.Detour;

/// <summary>
///     A detour backend that decides, per target, whether Concord installs its own detour or hands its
///     injections to a foreign patch host. Two libraries cannot both own one entry point, so a target another
///     patcher has already claimed goes to the host instead, and Concord's injections ride along inside it.
///     When the host can notify Concord before it rebuilds a method, a target Concord already detoured is
///     promoted onto the host rather than silently losing its injections.
/// </summary>
public sealed class RoutingDetourBackend : IDetourBackend, IForeignPatchObserver {
    private readonly object gate = new object();
    private readonly IDetourBackend inner;
    private readonly Action<string> log;
    private readonly Dictionary<MethodBase, RouteState> routes = [];
    private readonly Dictionary<MethodBase, string> rejectionReasons = [];
    private readonly Dictionary<MethodBase, List<RoutedHandle>> rawApplies = [];
    private readonly HashSet<MethodBase> rawInventory = [];
    private volatile IForeignPatchHost? host;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RoutingDetourBackend" /> class.
    /// </summary>
    /// <param name="inner">The backend used for targets Concord detours itself.</param>
    /// <param name="log">Writes coexistence messages to the host game's log.</param>
    public RoutingDetourBackend(IDetourBackend inner, Action<string> log) {
        this.inner = inner;
        this.log = log;
    }

    /// <summary>
    ///     Routes every target through the host once one is active, not only contested ones.
    /// </summary>
    public bool RouteEverything { get; set; }

    /// <summary>
    ///     Whether the host's notifier hook is installed. When false, late contention is only detectable
    ///     by polling and is not recoverable.
    /// </summary>
    public bool NotifierInstalled { get; private set; }

    /// <summary>
    ///     Attaches the foreign patch host and tries to install its notifier hook. Only the first call
    ///     takes effect.
    /// </summary>
    /// <param name="host">The host to route contested targets through.</param>
    public void ActivateHost(IForeignPatchHost host) {
        lock (gate) {
            if (this.host != null) {
                return;
            }

            this.host = host;
            PatchOwners.Host = host;
        }

        try {
            NotifierInstalled = host.TryInstallNotifier(this, inner);
        } catch (Exception ex) {
            NotifierInstalled = false;
            log(CoexistenceLogMarkers.HookUnavailable + " " + ex.Message);
        }
    }

    /// <inheritdoc />
    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        original = MethodIdentity.Normalize(original);
        MethodBase routeKey = MethodIdentity.SharedBodyKey(original);

        using (EnterHostLock()) {
            lock (gate) {
                RouteState state = RouteOf(routeKey);

                if (state == RouteState.Bridge) {
                    throw new InvalidOperationException(
                        "Concord.Detour.RoutingDetourBackend.Apply is not coexistence-aware and cannot be used on a target routed to the foreign patch host: " +
                        DescribeTarget(original));
                }

                if (state == RouteState.Rejected) {
                    throw new InvalidOperationException(rejectionReasons[routeKey]);
                }

                if (state == RouteState.Unpinned) {
                    IDetourHandle handle = inner.Apply(original, replacement);
                    PinRaw(routeKey);
                    return handle;
                }

                return inner.Apply(original, replacement);
            }
        }
    }

    /// <inheritdoc />
    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        target = MethodIdentity.Normalize(target);

        added = WrapperComposer.TagRequestedInstantiation(target, added);
        MethodBase routeKey = MethodIdentity.SharedBodyKey(target);

        // The host lock is taken before `gate`, always. The notifier fires while the host already holds
        // its lock and then takes `gate`, so acquiring them in the other order here would deadlock.
        using (EnterHostLock()) {
            lock (gate) {
                RoutedHandle handle = new RoutedHandle(this, target, routeKey, added);
                handle.Attach(ApplyComposedRouted(target, routeKey, added, handle));
                return handle;
            }
        }
    }

    /// <inheritdoc />
    public void OnForeignPatchPending(MethodBase target, object hostPatchState) {
        // Nothing may escape into the foreign library: an exception thrown out of its rebuild breaks an
        // unrelated caller's patch, which is exactly the blast radius this design exists to avoid.
        try {
            MethodBase normalized = MethodIdentity.Normalize(target);
            MethodBase routeKey = MethodIdentity.SharedBodyKey(normalized);

            lock (gate) {
                if (host == null || RouteOf(routeKey) != RouteState.Raw) {
                    return;
                }

                Promote(routeKey, normalized, hostPatchState);
            }
        } catch (Exception ex) {
            log(CoexistenceLogMarkers.PromoteFailed + " notifier failed for " + DescribeTarget(target) + ": " + ex.Message);
        }
    }

    /// <summary>
    ///     Reports how a target's detour is currently owned.
    /// </summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>The target's route state, <see cref="RouteState.Unpinned" /> when undecided.</returns>
    public RouteState GetRoute(MethodBase target) {
        MethodBase routeKey = MethodIdentity.SharedBodyKey(MethodIdentity.Normalize(target));

        lock (gate) {
            return RouteOf(routeKey);
        }
    }

    /// <summary>
    ///     Lists every target Concord currently owns with its own detour.
    /// </summary>
    /// <returns>A snapshot of the raw-detoured targets.</returns>
    public IReadOnlyCollection<MethodBase> RawPinnedTargets() {
        lock (gate) {
            return new List<MethodBase>(rawInventory);
        }
    }

    /// <summary>
    ///     Lists every target whose promotion to the host failed, so its Concord injections are not running.
    /// </summary>
    /// <returns>A snapshot of the lost targets.</returns>
    public IReadOnlyCollection<MethodBase> ContestedLostTargets() {
        lock (gate) {
            List<MethodBase> lost = [];
            foreach (KeyValuePair<MethodBase, RouteState> entry in routes) {
                if (entry.Value == RouteState.ContestedLost) {
                    lost.Add(entry.Key);
                }
            }

            return lost;
        }
    }

    private static string DescribeTarget(MethodBase target) {
        return (target.DeclaringType?.Name ?? "<module>") + "." + target.Name;
    }

    private IDisposable? EnterHostLock() {
        IForeignPatchHost? current = host;
        return current?.EnterHostLock();
    }

    private RouteState RouteOf(MethodBase target) {
        return routes.TryGetValue(target, out RouteState existing) ? existing : RouteState.Unpinned;
    }

    private IDetourHandle ApplyComposedRouted(MethodBase target, MethodBase routeKey, IReadOnlyList<Injection> added, RoutedHandle handle) {
        RouteState state = RouteOf(routeKey);

        if (state == RouteState.Raw) {
            IDetourHandle raw = inner.ApplyComposed(target, added);
            TrackRaw(routeKey, handle);
            return raw;
        }

        if (state == RouteState.Bridge) {
            return host!.ApplyToRouted(target, added);
        }

        if (state == RouteState.Rejected) {
            throw new InvalidOperationException(rejectionReasons[routeKey]);
        }

        // ContestedLost: the host owns the entry point. Apply anyway so the injections are live if the
        // host ever gives the method back, and so behaviour matches a target that was never contested.
        if (state == RouteState.ContestedLost) {
            return inner.ApplyComposed(target, added);
        }

        if (host == null) {
            IDetourHandle raw = inner.ApplyComposed(target, added);
            PinRaw(routeKey);
            TrackRaw(routeKey, handle);
            return raw;
        }

        ForeignRouteResult result = host.TryRoute(target, added, RouteEverything);

        if (result.Kind == ForeignRouteKind.NotContested) {
            IDetourHandle raw = inner.ApplyComposed(target, added);
            PinRaw(routeKey);
            TrackRaw(routeKey, handle);
            return raw;
        }

        if (result.Kind == ForeignRouteKind.Routed) {
            routes[routeKey] = RouteState.Bridge;
            log(CoexistenceLogMarkers.RoutedContested + " " + DescribeTarget(target));
            return result.Handle!;
        }

        routes[routeKey] = RouteState.Rejected;
        rejectionReasons[routeKey] = result.Reason!;
        log(result.Reason!);
        throw new InvalidOperationException(result.Reason);
    }

    private void Promote(MethodBase routeKey, MethodBase hostTarget, object hostPatchState) {
        if (!rawApplies.TryGetValue(routeKey, out List<RoutedHandle>? applies) || applies.Count == 0) {
            return;
        }

        List<Injection> all = [];
        foreach (RoutedHandle held in applies) {
            all.AddRange(held.Injections);
        }

        // Validation is pure, so a refusal never has to unwind a detour that was already removed. The
        // notifier runs at the head of the host's rebuild, so anything re-applied on the refusal path
        // would be clobbered by the rebuild that follows.
        string? reason = host!.ValidateRoute(hostTarget, all);
        if (reason != null) {
            routes[routeKey] = RouteState.ContestedLost;
            rawInventory.Remove(routeKey);
            log(CoexistenceLogMarkers.PromoteRejected + " " + DescribeTarget(hostTarget) + ": " + reason);
            return;
        }

        foreach (RoutedHandle held in applies) {
            held.DropInner();
        }

        try {
            // Contribute to the rebuild already in flight. Starting a fresh one here would be undone:
            // the host stores its own patch record after this returns, discarding anything a nested
            // call registered.
            foreach (RoutedHandle held in applies) {
                ForeignRouteResult result = host.RouteInto(hostTarget, held.Injections, hostPatchState);
                if (result.Kind != ForeignRouteKind.Routed) {
                    throw new InvalidOperationException(result.Reason ?? "host declined the in-flight route");
                }

                held.Swap(result.Handle!);
            }

            routes[routeKey] = RouteState.Bridge;
            rawInventory.Remove(routeKey);
            rawApplies.Remove(routeKey);
            log(CoexistenceLogMarkers.Promoted + " " + DescribeTarget(hostTarget));
        } catch (Exception ex) {
            RestoreRawAfterFailedPromotion(applies);
            routes[routeKey] = RouteState.ContestedLost;
            rawInventory.Remove(routeKey);
            log(CoexistenceLogMarkers.PromoteFailed + " " + DescribeTarget(hostTarget) + ": " + ex.Message);
        }
    }

    // Best effort only. The host's rebuild runs immediately after this and is expected to replace these
    // detours; they survive only if that rebuild also fails, which is exactly when they are wanted.
    private void RestoreRawAfterFailedPromotion(List<RoutedHandle> applies) {
        foreach (RoutedHandle held in applies) {
            try {
                held.Swap(inner.ApplyComposed(held.Original, held.Injections));
            } catch (Exception ex) {
                log(CoexistenceLogMarkers.PromoteFailed + " could not restore the detour for " + DescribeTarget(held.Original) + ": " + ex.Message);
            }
        }
    }

    private void PinRaw(MethodBase target) {
        routes[target] = RouteState.Raw;
        rawInventory.Add(target);
    }

    private void TrackRaw(MethodBase target, RoutedHandle handle) {
        if (!rawApplies.TryGetValue(target, out List<RoutedHandle>? applies)) {
            applies = [];
            rawApplies[target] = applies;
        }

        applies.Add(handle);
    }

    private void ReleaseRaw(MethodBase target, RoutedHandle handle) {
        if (rawApplies.TryGetValue(target, out List<RoutedHandle>? applies)) {
            applies.Remove(handle);
            if (applies.Count == 0) {
                rawApplies.Remove(target);
            }
        }
    }

    // A stable handle the caller keeps across a promotion. Promotion disposes the detour underneath it
    // and installs a host-owned one, so the caller's reference must not be the real handle.
    private sealed class RoutedHandle : IDetourHandle {
        private readonly RoutingDetourBackend owner;
        private readonly MethodBase target;
        private readonly MethodBase routeKey;
        private IDetourHandle? real;
        private bool disposed;

        public RoutedHandle(RoutingDetourBackend owner, MethodBase target, MethodBase routeKey, IReadOnlyList<Injection> injections) {
            this.owner = owner;
            this.target = target;
            this.routeKey = routeKey;
            Injections = injections;
        }

        public IReadOnlyList<Injection> Injections { get; }

        public MethodBase Original => target;

        public bool IsApplied {
            get {
                lock (owner.gate) {
                    return real is { IsApplied: true };
                }
            }
        }

        public void Attach(IDetourHandle handle) {
            real = handle;

            if (disposed) {
                real.Dispose();
            }
        }

        // Promotion already owns `gate`, so these two do not re-take it.
        public void DropInner() {
            real?.Dispose();
            real = null;
        }

        // Same operation as Attach; the separate name marks the promotion/rebuild call sites, which
        // replace an already-installed handle rather than installing the first one.
        public void Swap(IDetourHandle handle) {
            Attach(handle);
        }

        public void Dispose() {
            lock (owner.gate) {
                if (disposed) {
                    return;
                }

                disposed = true;
                owner.ReleaseRaw(routeKey, this);
                real?.Dispose();
            }
        }
    }
}
