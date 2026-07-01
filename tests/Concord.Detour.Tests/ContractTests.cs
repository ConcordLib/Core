using System.Reflection;
using Xunit;

namespace Concord.Detour.Tests;

public class ContractTests {
    private static readonly MethodInfo Src = typeof(Targets).GetMethod(nameof(Targets.Original))!;
    private static readonly MethodInfo Dst = typeof(Targets).GetMethod(nameof(Targets.Replacement))!;

    [Fact]
    public void Apply_returns_an_applied_handle_for_the_original() {
        IDetourBackend backend = new FakeDetourBackend();
        IDetourHandle handle = backend.Apply(Src, Dst);
        Assert.Same(Src, handle.Original);
        Assert.True(handle.IsApplied);
    }

    [Fact]
    public void Disposing_the_handle_marks_it_not_applied() {
        IDetourBackend backend = new FakeDetourBackend();
        IDetourHandle handle = backend.Apply(Src, Dst);
        handle.Dispose();
        Assert.False(handle.IsApplied);
    }

    [Fact]
    public void Double_dispose_is_safe() {
        IDetourHandle handle = new FakeDetourBackend().Apply(Src, Dst);
        handle.Dispose();
        handle.Dispose();
        Assert.False(handle.IsApplied);
    }
}
