using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class PatchHandleTests {
    [Fact]
    public void Dispose_DisposesEveryDetour_AndFlipsIsApplied() {
        FakeHandle a = new FakeHandle();
        FakeHandle b = new FakeHandle();
        PatchHandle handle = new PatchHandle([a, b], null);

        Assert.True(handle.IsApplied);

        handle.Dispose();

        Assert.False(handle.IsApplied);
        Assert.True(a.Disposed);
        Assert.True(b.Disposed);
    }

    [Fact]
    public void Dispose_RunsOnDisposeCallbackOnce() {
        int calls = 0;
        PatchHandle handle = new PatchHandle([], () => calls++);

        handle.Dispose();
        handle.Dispose();

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Detours_ExposesUnderlyingHandles() {
        FakeHandle a = new FakeHandle();
        PatchHandle handle = new PatchHandle([a], null);

        IReadOnlyList<IDetourHandle> detours = handle.Detours;

        Assert.Single(detours);
        Assert.Same(a, detours[0]);
    }

    [Fact]
    public void IsApplied_ReflectsUnderlyingDetours() {
        FakeHandle applied = new FakeHandle();
        PatchHandle handle = new PatchHandle([applied], null);

        Assert.True(handle.IsApplied);

        handle.Dispose();
        Assert.False(handle.IsApplied);
    }

    [Fact]
    public void IsApplied_ReturnsFalseWhenAnyDetourNotApplied() {
        FakeHandle applied1 = new FakeHandle();
        FakeHandle applied2 = new FakeHandle();
        PatchHandle handle = new PatchHandle([applied1, applied2], null);

        Assert.True(handle.IsApplied);

        applied2.Disposed = true;
        Assert.False(handle.IsApplied);
    }

    [Fact]
    public void IsApplied_ReturnsFalseWhenEmpty() {
        PatchHandle handle = new PatchHandle([], null);
        Assert.False(handle.IsApplied);
    }

    private sealed class FakeHandle : IDetourHandle {
        public bool Disposed { get; set; }

        public MethodBase Original => typeof(object).GetMethod(nameof(ToString))!;

        public bool IsApplied => !Disposed;

        public void Dispose() {
            Disposed = true;
        }
    }
}
