using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class PropOccupant {
    public float Age;
    public int stored;

    public float AgeYears => Age;

    public int Stored {
        get {
            return stored;
        }
        set {
            stored = value;
        }
    }
}

public class PropHost {
    public PropOccupant occupant = new PropOccupant();

    public bool ShouldEject() {
        return occupant.AgeYears >= 18f;
    }

    public void Store() {
        occupant.Stored = 42;
    }
}

public class PropInjectionMethods {
    public float ShimAge(Operation<float> age) {
        return age.Invoke() - 2f;
    }

    public void ShimStore(VoidOperation<int> set) {
        set.Invoke(7);
    }
}

public sealed class PropertyNameTargetingTests {
    [Fact]
    public void PropertyName_ResolvesGetterFromOperationShape() {
        MethodBase target = typeof(PropHost).GetMethod(nameof(PropHost.ShouldEject))!;
        MethodBase injection = typeof(PropInjectionMethods).GetMethod(nameof(PropInjectionMethods.ShimAge))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(PropOccupant), nameof(PropOccupant.AgeYears), At.Around, 0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [wrap]);
        System.Func<PropHost, bool> run = result.Wrapper.CreateDelegate<System.Func<PropHost, bool>>();

        PropHost host = new PropHost();
        host.occupant.Age = 19f;
        Assert.False(run(host));
    }

    [Fact]
    public void BothAccessors_ResolvesSetterFromVoidOperation() {
        MethodBase target = typeof(PropHost).GetMethod(nameof(PropHost.Store))!;
        MethodBase injection = typeof(PropInjectionMethods).GetMethod(nameof(PropInjectionMethods.ShimStore))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(PropOccupant), nameof(PropOccupant.Stored), At.Around, 0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [wrap]);
        System.Action<PropHost> run = result.Wrapper.CreateDelegate<System.Action<PropHost>>();

        PropHost host = new PropHost();
        run(host);
        Assert.Equal(7, host.occupant.stored);
    }

    [Fact]
    public void BothAccessors_HeadShift_FailsWithConc036() {
        MethodBase target = typeof(PropHost).GetMethod(nameof(PropHost.Store))!;
        MethodBase injection = typeof(PropInjectionMethods).GetMethod(nameof(PropInjectionMethods.ShimStore))!;
        Injection head = new Injection(injection, new InjectAt.Invoke(typeof(PropOccupant), nameof(PropOccupant.Stored), At.Head, 0), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [head]));
        Assert.Equal("CONC036", ex.Code);
        Assert.Contains("get_Stored", ex.Message);
    }
}
