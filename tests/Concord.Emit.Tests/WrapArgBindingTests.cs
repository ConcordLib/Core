using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class BindingRules {
    public static int Apply(int basePrice) {
        return basePrice + 10;
    }
}

public class BindingHost {
    public int Total(int listed) {
        return BindingRules.Apply(listed + 5) + 1;
    }
}

public class BindingInjectionMethods {
    public int DoubleTheRealArg(int basePrice, Operation<int, int> op) {
        return op.Invoke(basePrice * 2);
    }
}

public sealed class BindingReceiver {
    public int Scale;

    public BindingReceiver(int scale) {
        Scale = scale;
    }

    public int Apply(int basePrice) {
        return basePrice * Scale;
    }
}

public interface IBindingShop {
    int Price(int basePrice);
}

public sealed class BindingShop : IBindingShop {
    public int Price(int basePrice) {
        return basePrice + 1;
    }
}

public class BindingReceiverHost {
    public int Total(BindingReceiver receiver, int listed) {
        return receiver.Apply(listed) + 1;
    }

    public int Shopped(IBindingShop shop, int listed) {
        return shop.Price(listed) + 1;
    }
}

public class BoundSlotInjectionMethods {
    public static int[] Slots = { 3, 7 };

    public int ScaleBySlot(int basePrice, Operation<int, int> op, [Bound] int slot) {
        return op.Invoke(basePrice * Slots[slot]);
    }
}

public class ReceiverInjectionMethods {
    public int ReadsReceiver(BindingReceiver self, int basePrice, Operation<int, int> op) {
        return op.Invoke(basePrice + self.Scale);
    }

    public int ReadsInterfaceReceiver(IBindingShop self, int basePrice, Operation<int, int> op) {
        return op.Invoke(basePrice) + (self is BindingShop ? 100 : 0);
    }
}

public sealed class WrapArgBindingTests {
    [Fact]
    public void LeadingParameter_ReadsCallSiteArgument_NotTargetParameter() {
        MethodBase target = typeof(BindingHost).GetMethod(nameof(BindingHost.Total))!;
        MethodBase injection = typeof(BindingInjectionMethods).GetMethod(nameof(BindingInjectionMethods.DoubleTheRealArg))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(BindingRules), nameof(BindingRules.Apply), At.Around, 0), "test", 0);
        ComposeResult result = WrapperComposer.Compose(target, [wrap]);
        System.Func<BindingHost, int, int> run = result.Wrapper.CreateDelegate<System.Func<BindingHost, int, int>>();

        Assert.Equal(41, run(new BindingHost(), 10));
    }

    [Fact]
    public void BoundParameter_IsAvailableToAnAroundInvokeInjection() {
        MethodBase target = typeof(BindingHost).GetMethod(nameof(BindingHost.Total))!;
        MethodBase injection = typeof(BoundSlotInjectionMethods).GetMethod(nameof(BoundSlotInjectionMethods.ScaleBySlot))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(BindingRules), nameof(BindingRules.Apply), At.Around, 0), "test", 0) {
            BoundArguments = new System.Collections.Generic.Dictionary<string, object?> { ["slot"] = 1 },
        };
        ComposeResult result = WrapperComposer.Compose(target, [wrap]);
        System.Func<BindingHost, int, int> run = result.Wrapper.CreateDelegate<System.Func<BindingHost, int, int>>();

        Assert.Equal(116, run(new BindingHost(), 10));
    }

    [Fact]
    public void LeadingParameter_BindsTheCallSiteReceiver() {
        MethodBase target = typeof(BindingReceiverHost).GetMethod(nameof(BindingReceiverHost.Total))!;
        MethodBase injection = typeof(ReceiverInjectionMethods).GetMethod(nameof(ReceiverInjectionMethods.ReadsReceiver))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(BindingReceiver), nameof(BindingReceiver.Apply), At.Around, 0), "test", 0);
        ComposeResult result = WrapperComposer.Compose(target, [wrap]);
        System.Func<BindingReceiverHost, BindingReceiver, int, int> run =
            result.Wrapper.CreateDelegate<System.Func<BindingReceiverHost, BindingReceiver, int, int>>();

        Assert.Equal(16, run(new BindingReceiverHost(), new BindingReceiver(3), 2));
    }

    [Fact]
    public void LeadingParameter_BindsAnInterfaceCallSiteReceiver() {
        MethodBase target = typeof(BindingReceiverHost).GetMethod(nameof(BindingReceiverHost.Shopped))!;
        MethodBase injection = typeof(ReceiverInjectionMethods).GetMethod(nameof(ReceiverInjectionMethods.ReadsInterfaceReceiver))!;
        Injection wrap = new Injection(injection, new InjectAt.Invoke(typeof(IBindingShop), nameof(IBindingShop.Price), At.Around, 0), "test", 0);
        ComposeResult result = WrapperComposer.Compose(target, [wrap]);
        System.Func<BindingReceiverHost, IBindingShop, int, int> run =
            result.Wrapper.CreateDelegate<System.Func<BindingReceiverHost, IBindingShop, int, int>>();

        Assert.Equal(112, run(new BindingReceiverHost(), new BindingShop(), 10));
    }
}
