using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests.NeutralRoundTrip;

public sealed class InstanceRoundTripTests {
    [Fact]
    public void HeadInjection_OnClassInstanceMethod_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(InstanceClassTarget).GetMethod(nameof(InstanceClassTarget.AddToBase))!;
        MethodBase headMethod = typeof(InstanceInjectionMethods).GetMethod(nameof(InstanceInjectionMethods.Head))!;
        Injection head = new Injection(headMethod, new InjectAt.Head(), "spike", 0);

        InstanceInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [head]);
        Func<InstanceClassTarget, int, int> invoke = (Func<InstanceClassTarget, int, int>)wrapper.CreateDelegate(typeof(Func<InstanceClassTarget, int, int>));

        InstanceClassTarget instance = new InstanceClassTarget(10);
        int value = invoke(instance, 5);

        Assert.Equal(15, value);
        Assert.Equal(10, instance.Base);
        Assert.Equal(["head"], InstanceInjectionMethods.Log);
    }

    [Fact]
    public void AroundInjection_OnClassInstanceMethod_KeepsAmbientThis_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(InstanceClassTarget).GetMethod(nameof(InstanceClassTarget.AddToBase))!;
        MethodBase aroundMethod = typeof(InstanceInjectionMethods).GetMethod(nameof(InstanceInjectionMethods.AroundAddToBase))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "spike", 0);

        InstanceInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [around]);
        Func<InstanceClassTarget, int, int> invoke = (Func<InstanceClassTarget, int, int>)wrapper.CreateDelegate(typeof(Func<InstanceClassTarget, int, int>));

        InstanceClassTarget instance = new InstanceClassTarget(10);
        int value = invoke(instance, 5);

        Assert.Equal(15, value);
        Assert.Equal(10, instance.Base);
        Assert.Equal(["pre", "post"], InstanceInjectionMethods.Log);
    }

    [Fact]
    public void ReturnInjection_OnStructInstanceMethod_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(InstanceStructTarget).GetMethod(nameof(InstanceStructTarget.AddToBase))!;
        MethodBase returnMethod = typeof(InstanceInjectionMethods).GetMethod(nameof(InstanceInjectionMethods.Return))!;
        Injection returnInjection = new Injection(returnMethod, new InjectAt.Return(), "spike", 0);

        InstanceInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [returnInjection]);

        InstanceStructTarget instance = new InstanceStructTarget(10);
        object? value = wrapper.Invoke(null, [instance, 5]);

        Assert.Equal(15, value);
        Assert.Equal(["return:15"], InstanceInjectionMethods.Log);
    }

    [Fact]
    public void AroundInjection_OnStructInstanceMethod_RoundTripsThroughNeutralBody() {
        MethodBase target = typeof(InstanceStructTarget).GetMethod(nameof(InstanceStructTarget.AddToBase))!;
        MethodBase aroundMethod = typeof(InstanceInjectionMethods).GetMethod(nameof(InstanceInjectionMethods.AroundAddToBase))!;
        Injection around = new Injection(aroundMethod, new InjectAt.Around(), "spike", 0);

        InstanceInjectionMethods.Log.Clear();
        MethodInfo wrapper = RoundTrip.ComposeThroughNeutralBody(target, [around]);

        InstanceStructTarget instance = new InstanceStructTarget(10);
        object? value = wrapper.Invoke(null, [instance, 5]);

        Assert.Equal(15, value);
        Assert.Equal(["pre", "post"], InstanceInjectionMethods.Log);
    }
}
