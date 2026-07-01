using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class Seeded {
    private readonly int _seed = 7;

    public int Probe() {
        return _seed;
    }
}

#pragma warning disable CS0109, CS0414
public class SeededMatchInjectionMethod : Seeded {
    private new int _seed;
    public void Head(ControlHandle ch) { _seed = 99; }
}

public class SeededMismatchInjectionMethod : Seeded {
    private new long _seed;
    public void Head(ControlHandle ch) { _seed = 99L; }
}
#pragma warning restore CS0109, CS0414

public sealed class ShadowTests {
    [Fact]
    public void Compose_MatchingShadow_WritesRealField() {
        MethodBase target = typeof(Seeded).GetMethod(nameof(Seeded.Probe))!;
        MethodBase injectionMethod = typeof(SeededMatchInjectionMethod).GetMethod(nameof(SeededMatchInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        Seeded instance = new Seeded();
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        result.Wrapper.Invoke(null, [instance]);

        Assert.Equal(99, instance.Probe());
    }

    [Fact]
    public void Compose_MismatchedShadow_ThrowsCONC002() {
        MethodBase target = typeof(Seeded).GetMethod(nameof(Seeded.Probe))!;
        MethodBase injectionMethod = typeof(SeededMismatchInjectionMethod).GetMethod(nameof(SeededMismatchInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC002", ex.Code);
    }
}
