using System.Reflection;
using Xunit;

namespace Concord.Detour.Tests;

public sealed class ReplaceInnerTests {
    [Fact]
    public void ReplaceInner_ForwardsToTheNewBackend() {
        FakeDetourBackend first = new FakeDetourBackend();
        FakeDetourBackend second = new FakeDetourBackend();
        RoutingDetourBackend router = new RoutingDetourBackend(first, _ => { });

        router.ReplaceInner(second);

        MethodBase target = typeof(ReplaceInnerTests).GetMethod(nameof(Subject))!;
        router.ApplyComposed(target, []);

        Assert.Empty(first.ComposedDetours);
        Assert.Single(second.ComposedDetours);
        Assert.Same(second, router.Inner);
    }

    [Fact]
    public void ReplaceInner_KeepsTheStateAReplacementRouterWouldLose() {
        FakeDetourBackend first = new FakeDetourBackend();
        RoutingDetourBackend router = new RoutingDetourBackend(first, _ => { });
        router.RouteEverything = true;

        router.ReplaceInner(new FakeDetourBackend());

        Assert.True(router.RouteEverything);

        RoutingDetourBackend rebuilt = new RoutingDetourBackend(new FakeDetourBackend(), _ => { });
        Assert.False(rebuilt.RouteEverything);
    }

    [Fact]
    public void ReplaceInner_RefusesNull() {
        RoutingDetourBackend router = new RoutingDetourBackend(new FakeDetourBackend(), _ => { });

        Assert.Throws<ArgumentNullException>(() => router.ReplaceInner(null!));
    }

    public static int Subject(int seed) {
        return seed + 1;
    }
}
