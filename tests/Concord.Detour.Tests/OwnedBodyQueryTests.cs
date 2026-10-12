using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Detour.Tests;

public static class OwnedBodyTargets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Raw() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Routed() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Untouched() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Released() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Lost() {
        return 1;
    }
}

public static class OwnedBodyInjections {
    public static void Bump(ControlHandle<int> handle) {
        handle.ReturnValue += 1;
    }
}

public sealed class OwnedBodyQueryTests {
    [Fact]
    public void OwnsBody_RawDetour_IsTrue() {
        MethodBase target = Target(nameof(OwnedBodyTargets.Raw));

        using IDetourHandle handle = new MonoModDetourBackend().ApplyComposed(target, [Bump("A")]);

        Assert.True(TargetDetourRegistry.OwnsBody(target));
    }

    [Fact]
    public void OwnsBody_AfterTargetRoutedToForeignHost_IsFalse() {
        MethodBase target = Target(nameof(OwnedBodyTargets.Routed));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), _ => { });
        StubHost host = new StubHost();
        router.ActivateHost(host);

        try {
            using IDetourHandle handle = router.ApplyComposed(target, [Bump("A")]);
            Assert.True(TargetDetourRegistry.OwnsBody(target));

            router.OnForeignPatchPending(target, new object());

            Assert.Equal(RouteState.Bridge, router.GetRoute(target));
            Assert.NotEmpty(PatchOwners.Of(target));
            Assert.False(TargetDetourRegistry.OwnsBody(target));
        } finally {
            PatchOwners.Host = null;
        }
    }

    [Fact]
    public void OwnsBody_AfterTargetLostToForeignHost_IsFalse() {
        MethodBase target = Target(nameof(OwnedBodyTargets.Lost));
        RoutingDetourBackend router = new RoutingDetourBackend(new MonoModDetourBackend(), _ => { });
        StubHost host = new StubHost();
        router.ActivateHost(host);

        try {
            using IDetourHandle handle = router.ApplyComposed(target, [Bump("A")]);
            Assert.True(TargetDetourRegistry.OwnsBody(target));

            host.RefuseWith = "the host refuses this route";
            router.OnForeignPatchPending(target, new object());

            Assert.Equal(RouteState.ContestedLost, router.GetRoute(target));
            Assert.False(TargetDetourRegistry.OwnsBody(target));

            using IDetourHandle late = router.ApplyComposed(target, [Bump("B")]);

            Assert.True(late.IsApplied);
            Assert.False(TargetDetourRegistry.OwnsBody(target));
        } finally {
            PatchOwners.Host = null;
        }
    }

    [Fact]
    public void OwnsBody_NoInjections_IsFalse() {
        Assert.False(TargetDetourRegistry.OwnsBody(Target(nameof(OwnedBodyTargets.Untouched))));
    }

    [Fact]
    public void OwnsBody_DisposedHandle_IsFalse() {
        MethodBase target = Target(nameof(OwnedBodyTargets.Released));
        IDetourHandle handle = new MonoModDetourBackend().ApplyComposed(target, [Bump("A")]);

        handle.Dispose();

        Assert.False(TargetDetourRegistry.OwnsBody(target));
    }

    private static MethodBase Target(string name) {
        return typeof(OwnedBodyTargets).GetMethod(name)!;
    }

    private static Injection Bump(string owner) {
        MethodInfo body = typeof(OwnedBodyInjections).GetMethod(nameof(OwnedBodyInjections.Bump))!;
        return new Injection(body, new InjectAt.Tail(), owner, 0);
    }

    private sealed class StubHost : IForeignPatchHost {
        private readonly List<string> routed = [];

        public IDisposable? EnterHostLock() {
            return null;
        }

        public string? RefuseWith { get; set; }

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
            foreach (Injection injection in added) {
                routed.Add(injection.Owner);
            }

            return ForeignRouteResult.Routed(new StubHandle(target));
        }

        public IDetourHandle ApplyToRouted(MethodBase target, IReadOnlyList<Injection> added) {
            return new StubHandle(target);
        }

        public IReadOnlyList<string> ForeignOwners(MethodBase target) {
            return [];
        }

        public IReadOnlyList<string> ConcordOwners(MethodBase target) {
            return routed;
        }

        public bool TryInstallNotifier(IForeignPatchObserver observer, IDetourBackend rawBackend) {
            return true;
        }

        private sealed class StubHandle(MethodBase original) : IDetourHandle {
            public MethodBase Original { get; } = original;

            public bool IsApplied { get; private set; } = true;

            public void Dispose() {
                IsApplied = false;
            }
        }
    }
}
