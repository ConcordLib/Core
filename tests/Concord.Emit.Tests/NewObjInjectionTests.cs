using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class Order {
    public Order(int id) {
        Id = id;
    }

    public int Id { get; }

    public virtual int Score() => Id;
}

public sealed class PricedOrder : Order {
    public PricedOrder(int id) : base(id) { }

    public override int Score() => Id * 10;
}

public class NewObjHost {
    public int Checkout(int id) {
        Order order = new Order(id);
        return order.Score();
    }
}

public class NewObjMethods {
    public Order SwapInstance(int id, Operation<int, Order> original) {
        original.Invoke(id);
        return new PricedOrder(id);
    }

    public int BumpArg(int original) {
        return original + 1;
    }
}

public sealed class NewObjInjectionTests {
    [Fact]
    public void Around_ReplacesTheConstructedInstance() {
        MethodBase target = typeof(NewObjHost).GetMethod(nameof(NewObjHost.Checkout))!;
        MethodBase injection = typeof(NewObjMethods).GetMethod(nameof(NewObjMethods.SwapInstance))!;
        Injection around = new Injection(injection, new InjectAt.NewObj(typeof(Order), At.Around, 0, [typeof(int)]), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [around]);
        System.Func<NewObjHost, int, int> run = result.Wrapper.CreateDelegate<System.Func<NewObjHost, int, int>>();

        Assert.Equal(30, run(new NewObjHost(), 3));
    }

    [Fact]
    public void Argument_RewritesAConstructorArgument() {
        MethodBase target = typeof(NewObjHost).GetMethod(nameof(NewObjHost.Checkout))!;
        MethodBase injection = typeof(NewObjMethods).GetMethod(nameof(NewObjMethods.BumpArg))!;
        Injection rewrite = new Injection(injection, new InjectAt.NewObj(typeof(Order), At.Argument, 0, [typeof(int)], 1), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [rewrite]);
        System.Func<NewObjHost, int, int> run = result.Wrapper.CreateDelegate<System.Func<NewObjHost, int, int>>();

        Assert.Equal(4, run(new NewObjHost(), 3));
    }
}
