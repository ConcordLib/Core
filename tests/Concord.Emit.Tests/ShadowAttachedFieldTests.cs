using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Xunit;

namespace Concord.Emit.Tests;

public class AttachedSeedTarget {
    public int observed;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Compute() {
        return 1;
    }
}

#pragma warning disable CS0109, CS0414
public class AttachedMarkerInjectionMethod : AttachedSeedTarget {
    public int concordAttachedMarker;

    public void Head(ControlHandle<int> ch) {
        ch.ReturnValue = 42;
        ch.Cancel();
    }
}

public class AttachedRealShadowInjectionMethod : AttachedSeedTarget {
    public int extraMarkerField;
    private new int observed;

    public void Head(ControlHandle<int> ch) {
        observed = 7;
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public class AttachedMismatchShadowInjectionMethod : AttachedSeedTarget {
    private new long observed;

    public int otherMarkerField;

    public void Head(ControlHandle<int> ch) {
        observed = 7L;
        ch.ReturnValue = 5;
        ch.Cancel();
    }
}
#pragma warning restore CS0109, CS0414

public sealed class ShadowAttachedFieldTests {
    [Fact]
    public void Compose_InjectionMethodFieldAbsentOnTarget_TreatedAsMarkerNotShadow() {
        MethodBase target = typeof(AttachedSeedTarget).GetMethod(nameof(AttachedSeedTarget.Compute))!;
        MethodBase injectionMethod = typeof(AttachedMarkerInjectionMethod).GetMethod(nameof(AttachedMarkerInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);

        using IDetourHandle handle = DetourBackend.Current.Apply(target, result.Wrapper);

        Assert.Equal(42, new AttachedSeedTarget().Compute());
    }

    [Fact]
    public void Compose_RealShadowAlongsideMarker_WritesRealFieldAndIgnoresMarker() {
        MethodBase target = typeof(AttachedSeedTarget).GetMethod(nameof(AttachedSeedTarget.Compute))!;
        MethodBase injectionMethod = typeof(AttachedRealShadowInjectionMethod).GetMethod(nameof(AttachedRealShadowInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);

        using IDetourHandle handle = DetourBackend.Current.Apply(target, result.Wrapper);

        AttachedSeedTarget instance = new AttachedSeedTarget();
        int value = instance.Compute();

        Assert.Equal(99, value);
        Assert.Equal(7, instance.observed);
    }

    [Fact]
    public void Compose_MismatchedShadowAlongsideMarker_ThrowsCONC002() {
        MethodBase target = typeof(AttachedSeedTarget).GetMethod(nameof(AttachedSeedTarget.Compute))!;
        MethodBase injectionMethod = typeof(AttachedMismatchShadowInjectionMethod).GetMethod(nameof(AttachedMismatchShadowInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC002", ex.Code);
    }
}
