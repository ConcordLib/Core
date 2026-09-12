using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class PrecompileTarget {
    public static int Compute(int x) {
        return x * 2;
    }
}

public static class PrecompileInjections {
    public static void Head() {
    }

    public static void Tail() {
    }
}

public sealed class WrapperPrecompileTests : IDisposable {
    private readonly List<string> events = [];

    public WrapperPrecompileTests() {
        Patcher.UseCompileGuard(() => {
            events.Add("open");
            return new Scope(events);
        });
    }

    public void Dispose() {
        Patcher.PrecompileWrappers = false;
        Patcher.UseCompileGuard(null);
    }

    [Fact]
    public void Disabled_NeverOpensTheGuard() {
        Patcher.PrecompileWrappers = false;

        using (Patch(nameof(PrecompileInjections.Head), new InjectAt.Head())) {
            Assert.Equal(4, PrecompileTarget.Compute(2));
        }

        Assert.Empty(events);
    }

    [Fact]
    public void Enabled_OpensAndClosesTheGuardAroundEachCompose() {
        Patcher.PrecompileWrappers = true;

        using (Patch(nameof(PrecompileInjections.Head), new InjectAt.Head())) {
            Assert.Equal(4, PrecompileTarget.Compute(2));
        }

        Assert.Equal(["open", "close"], events);
    }

    [Fact]
    public void Enabled_OpensTheGuardAgainWhenAnotherPatchRecomposesTheTarget() {
        Patcher.PrecompileWrappers = true;

        IPatchHandle first = Patch(nameof(PrecompileInjections.Head), new InjectAt.Head());
        try {
            events.Clear();

            using (Patch(nameof(PrecompileInjections.Tail), new InjectAt.Tail())) {
                Assert.Equal(4, PrecompileTarget.Compute(2));
            }

            Assert.Contains("open", events);
        } finally {
            first.Dispose();
        }
    }

    private static IPatchHandle Patch(string injectionName, InjectAt at) {
        MethodBase target = typeof(PrecompileTarget).GetMethod(nameof(PrecompileTarget.Compute))!;
        MethodBase injectionMethod = typeof(PrecompileInjections).GetMethod(injectionName)!;

        return Patcher.PatchInjection(target, new Injection(injectionMethod, at, "test." + injectionName, 0));
    }

    private sealed class Scope(List<string> events) : IDisposable {
        public void Dispose() {
            events.Add("close");
        }
    }
}
