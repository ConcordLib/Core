using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class VoidBox {
    public int Calls;
    public int LastSet;

    public int Value {
        set {
            LastSet = value;
        }
    }

    public void Bump() {
        Calls++;
    }
}

public class VoidHost {
    public VoidBox box = new VoidBox();

    public void Run() {
        box.Bump();
        box.Value = 42;
    }
}

public class VoidInjectionMethods {
    public static bool AllowBump;

    public void WrapBump(Operation bump) {
        if (AllowBump) {
            bump.Invoke();
        }
    }

    public void WrapSet(VoidOperation<int> set) {
        set.Invoke(99);
    }
}

public sealed class VoidWrapTests {
    [Fact]
    public void VoidZeroArgSite_SkipsAndInvokes() {
        MethodBase target = typeof(VoidHost).GetMethod(nameof(VoidHost.Run))!;
        MethodBase injection = typeof(VoidInjectionMethods).GetMethod(nameof(VoidInjectionMethods.WrapBump))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(VoidBox), nameof(VoidBox.Bump), At.Around, 0), "test", 0);
        ComposeResult result = WrapperComposer.Compose(target, [wrap]);
        System.Action<VoidHost> run = result.Wrapper.CreateDelegate<System.Action<VoidHost>>();

        VoidHost host = new VoidHost();
        VoidInjectionMethods.AllowBump = false;
        run(host);
        Assert.Equal(0, host.box.Calls);

        VoidInjectionMethods.AllowBump = true;
        run(host);
        Assert.Equal(1, host.box.Calls);
    }

    [Fact]
    public void SetterSite_ReplacesAssignedValue() {
        MethodBase target = typeof(VoidHost).GetMethod(nameof(VoidHost.Run))!;
        MethodBase injection = typeof(VoidInjectionMethods).GetMethod(nameof(VoidInjectionMethods.WrapSet))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(VoidBox), "set_Value", At.Around, 0), "test", 0);
        ComposeResult result = WrapperComposer.Compose(target, [wrap]);
        System.Action<VoidHost> run = result.Wrapper.CreateDelegate<System.Action<VoidHost>>();

        VoidHost host = new VoidHost();
        run(host);
        Assert.Equal(99, host.box.LastSet);
    }
}
