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

public readonly struct Coin {
    public Coin(int value) {
        Value = value;
    }

    public int Value { get; }
}

// `Coin coin = new Coin(seed); return coin.Value;` compiles to `ldloca.s` followed by a direct
// call to `Coin::.ctor`, never `newobj` - the C# compiler always initializes a struct local in
// place, regardless of how trivial or complex the constructor body is. Consuming the constructor
// result as an expression, without ever storing it in a local first, is what forces a real
// `newobj Coin::.ctor` into the IL, which is what this fixture needs to exercise.
public class StructHost {
    public int Mint(int seed) {
        return new Coin(seed).Value;
    }
}

public class ForeignHost {
    public int Order(int seed) {
        Concord.Emit.Tests.ForeignTargets.ForeignOrder o = new Concord.Emit.Tests.ForeignTargets.ForeignOrder(seed);
        return o.Id;
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

    [Fact]
    public void NewObj_OnAStruct_RewritesTheArgument() {
        MethodBase target = typeof(StructHost).GetMethod(nameof(StructHost.Mint))!;
        MethodBase injection = typeof(NewObjMethods).GetMethod(nameof(NewObjMethods.BumpArg))!;
        Injection rewrite = new Injection(injection, new InjectAt.NewObj(typeof(Coin), At.Argument, 0, [typeof(int)], 1), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [rewrite]);
        System.Func<StructHost, int, int> run = result.Wrapper.CreateDelegate<System.Func<StructHost, int, int>>();

        Assert.Equal(4, run(new StructHost(), 3));
    }

    [Fact]
    public void NewObj_OnAForeignType_RewritesTheArgument() {
        MethodBase target = typeof(ForeignHost).GetMethod(nameof(ForeignHost.Order))!;
        MethodBase injection = typeof(NewObjMethods).GetMethod(nameof(NewObjMethods.BumpArg))!;
        Injection rewrite = new Injection(
            injection,
            new InjectAt.NewObj(typeof(Concord.Emit.Tests.ForeignTargets.ForeignOrder), At.Argument, 0, [typeof(int)], 1),
            "test",
            0);

        ComposeResult result = WrapperComposer.Compose(target, [rewrite]);
        System.Func<ForeignHost, int, int> run = result.Wrapper.CreateDelegate<System.Func<ForeignHost, int, int>>();

        Assert.Equal(4, run(new ForeignHost(), 3));
    }
}
