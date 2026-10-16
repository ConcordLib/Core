using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Detour.Tests;

public static class DeciderTargets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Raw() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Promoted() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Lost() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Held() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Untouched() {
        return 1;
    }
}

public sealed class SuppressionDeciderTests {
    [Fact]
    public void TheRouterAnswersTheSuppressionDecision() {
        Assert.IsAssignableFrom<IForeignPatchDecider>(new RoutingDetourBackend(new MonoModDetourBackend(), _ => { }));
    }

    [Fact]
    public void ARawTargetMayBeKept() {
        MethodBase target = Target(nameof(DeciderTargets.Raw));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), _ => { });

        using IDetourHandle handle = router.ApplyComposed(target, [Bump()]);

        Assert.Equal(RouteState.Raw, router.GetRoute(target));
        Assert.True(((IForeignPatchDecider)router).TrySuppressForeignRebuild(target, new object()));
    }

    [Fact]
    public void AnUndecidedTargetIsKept() {
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), _ => { });

        Assert.True(((IForeignPatchDecider)router).TrySuppressForeignRebuild(Target(nameof(DeciderTargets.Untouched)), new object()));
    }

    [Fact]
    public void ARoutedTargetMayNotBeKept() {
        MethodBase target = Target(nameof(DeciderTargets.Promoted));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), _ => { });
        router.ActivateHost(new RoutingHost());

        try {
            using IDetourHandle handle = router.ApplyComposed(target, [Bump()]);
            router.OnForeignPatchPending(target, new object());

            Assert.Equal(RouteState.Bridge, router.GetRoute(target));
            Assert.False(((IForeignPatchDecider)router).TrySuppressForeignRebuild(target, new object()));
        } finally {
            PatchOwners.Host = null;
        }
    }

    [Fact]
    public void ATargetLostToTheHostMayNotBeKept() {
        MethodBase target = Target(nameof(DeciderTargets.Lost));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), _ => { });
        RoutingHost host = new RoutingHost { RefuseWith = "the host refuses this route" };
        router.ActivateHost(host);

        try {
            using IDetourHandle handle = router.ApplyComposed(target, [Bump()]);
            router.OnForeignPatchPending(target, new object());

            Assert.Equal(RouteState.ContestedLost, router.GetRoute(target));
            Assert.False(((IForeignPatchDecider)router).TrySuppressForeignRebuild(target, new object()));
        } finally {
            PatchOwners.Host = null;
        }
    }

    [Fact]
    public void ATargetHeldAgainstTheForeignRebuildPinsRawInsteadOfAskingTheHost() {
        MethodBase target = Target(nameof(DeciderTargets.Held));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), _ => { });
        router.ActivateHost(new RoutingHost());
        SuppressedRebuilds.Mark(target);

        try {
            using IDetourHandle handle = router.ApplyComposed(target, [Bump()]);

            Assert.Equal(RouteState.Raw, router.GetRoute(target));
        } finally {
            SuppressedRebuilds.Forget(target);
            PatchOwners.Host = null;
        }
    }

    private static MethodBase Target(string name) {
        return typeof(DeciderTargets).GetMethod(name)!;
    }

    private static Injection Bump() {
        MethodInfo body = typeof(OwnedBodyInjections).GetMethod(nameof(OwnedBodyInjections.Bump))!;
        return new Injection(body, new InjectAt.Tail(), "decider", 0);
    }

    private sealed class RoutingHost : IForeignPatchHost {
        public string? RefuseWith { get; set; }

        public IDisposable? EnterHostLock() {
            return null;
        }

        public string? ValidateRoute(MethodBase target, IReadOnlyList<Injection> added) {
            return RefuseWith;
        }

        public string? RevalidateRouted(MethodBase target, object hostPatchState) {
            return null;
        }

        public ForeignRouteResult TryRoute(MethodBase target, IReadOnlyList<Injection> added, bool forceRoute) {
            return ForeignRouteResult.NotContested();
        }

        public ForeignRouteResult RouteInto(MethodBase target, IReadOnlyList<Injection> added, object hostPatchState) {
            return ForeignRouteResult.Routed(new Handle(target));
        }

        public IDetourHandle ApplyToRouted(MethodBase target, IReadOnlyList<Injection> added) {
            return new Handle(target);
        }

        public IReadOnlyList<string> ForeignOwners(MethodBase target) {
            return [];
        }

        public IReadOnlyList<string> ConcordOwners(MethodBase target) {
            return [];
        }

        public bool TryInstallNotifier(IForeignPatchObserver observer, IDetourBackend rawBackend) {
            return true;
        }

        private sealed class Handle(MethodBase original) : IDetourHandle {
            public MethodBase Original { get; } = original;

            public bool IsApplied { get; private set; } = true;

            public void Dispose() {
                IsApplied = false;
            }
        }
    }
}
