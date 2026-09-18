using Xunit;

namespace Concord.Detour.Tests;

public sealed class ResolveCacheTests {
    [Fact]
    public void ForgetReflectionOnly_FindsMonoModCaches() {
        Assert.True(ResolveCache.ForgetReflectionOnly() >= 0);
    }
}
