using System.Reflection;
using MonoMod.Core;
using Xunit;

namespace Concord.Detour.Tests;

public class MonoModSpikeTests {
    [Fact]
    public void MonoMod_redirects_then_restores() {
        MethodInfo src = typeof(Targets).GetMethod(nameof(Targets.OriginalB))!;
        MethodInfo dst = typeof(Targets).GetMethod(nameof(Targets.ReplacementB))!;

        Assert.Equal(10, Targets.OriginalB());

        ICoreDetour detour = DetourFactory.Current.CreateDetour(src, dst);
        try {
            Assert.True(detour.IsApplied);
            Assert.Equal(20, Targets.OriginalB());
        } finally {
            detour.Undo();
            detour.Dispose();
        }

        Assert.Equal(10, Targets.OriginalB());
    }
}
