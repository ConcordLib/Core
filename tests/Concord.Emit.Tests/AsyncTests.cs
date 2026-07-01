using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Xunit;

namespace Concord.Emit.Tests;

public static class IteratorTarget {
    public static IEnumerable<int> Iterate() {
        yield return 1;
        yield return 2;
    }
}

public class AsyncTarget {
    public static int SideEffectLog;

    public async Task<int> Compute() {
        SideEffectLog += 1;
        await Task.Yield();
        return 42;
    }
}

public static class AsyncObservations {
    public static int MoveNextSteps;
}

public static class AsyncInjectionMethods {
    public static void Observe(ControlHandle ch) {
        AsyncObservations.MoveNextSteps += 1;
    }
}

public static class PlainTarget {
    public static int Identity(int x) {
        return x;
    }
}

public sealed class AsyncTests {
    [Fact]
    public void ResolveStateMachineTarget_AsyncMethod_ReturnsMoveNextOnStateMachineType() {
        MethodBase target = typeof(AsyncTarget).GetMethod(nameof(AsyncTarget.Compute))!;
        Type stateMachineType = target.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;

        MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(target);

        Assert.Equal("MoveNext", resolved.Name);
        Assert.Equal(stateMachineType, resolved.DeclaringType);
    }

    [Fact]
    public void ResolveStateMachineTarget_IteratorMethod_ReturnsMoveNext() {
        MethodBase target = typeof(IteratorTarget).GetMethod(nameof(IteratorTarget.Iterate))!;
        Type stateMachineType = target.GetCustomAttribute<IteratorStateMachineAttribute>()!.StateMachineType;

        MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(target);

        Assert.Equal("MoveNext", resolved.Name);
        Assert.Equal(stateMachineType, resolved.DeclaringType);
    }

    [Fact]
    public void ResolveStateMachineTarget_PlainMethod_ReturnsItself() {
        MethodBase target = typeof(PlainTarget).GetMethod(nameof(PlainTarget.Identity))!;

        MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(target);

        Assert.Same(target, resolved);
    }

    [Fact]
    public async Task Compose_AsyncTarget_AppliedOverMoveNext_StillReturns42() {
        MethodBase target = typeof(AsyncTarget).GetMethod(nameof(AsyncTarget.Compute))!;
        MethodBase injectionMethod = typeof(AsyncInjectionMethods).GetMethod(nameof(AsyncInjectionMethods.Observe))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);
        MethodBase moveNext = WrapperComposer.ResolveStateMachineTarget(target);

        using IDetourHandle handle = DetourBackend.Current.Apply(moveNext, result.Wrapper);

        AsyncTarget.SideEffectLog = 0;
        int value = await new AsyncTarget().Compute();

        Assert.Equal(42, value);
        Assert.Equal(1, AsyncTarget.SideEffectLog);
    }

    [Fact]
    public async Task Compose_AsyncTarget_HeadObservesMoveNextStep() {
        MethodBase target = typeof(AsyncTarget).GetMethod(nameof(AsyncTarget.Compute))!;
        MethodBase injectionMethod = typeof(AsyncInjectionMethods).GetMethod(nameof(AsyncInjectionMethods.Observe))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);
        MethodBase moveNext = WrapperComposer.ResolveStateMachineTarget(target);

        using IDetourHandle handle = DetourBackend.Current.Apply(moveNext, result.Wrapper);

        AsyncObservations.MoveNextSteps = 0;
        int value = await new AsyncTarget().Compute();

        Assert.Equal(42, value);
        Assert.True(AsyncObservations.MoveNextSteps > 0);
    }
}
