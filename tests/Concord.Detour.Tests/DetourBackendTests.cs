using Xunit;

namespace Concord.Detour.Tests;

public class DetourBackendTests {
    [Fact]
    public void Default_is_the_monomod_backend() {
        Assert.IsType<MonoModDetourBackend>(DetourBackend.Current);
    }

    [Fact]
    public void Current_can_be_swapped_and_reset() {
        IDetourBackend original = DetourBackend.Current;
        try {
            FakeDetourBackend fake = new FakeDetourBackend();
            DetourBackend.Current = fake;
            Assert.Same(fake, DetourBackend.Current);
        } finally {
            DetourBackend.Current = original;
        }

        Assert.IsType<MonoModDetourBackend>(DetourBackend.Current);
    }

    [Fact]
    public void Current_rejects_null() {
        Assert.Throws<ArgumentNullException>(() => DetourBackend.Current = null!);
    }
}
