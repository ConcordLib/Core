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

public class InstanceIteratorTarget {
    public int Seed = 10;

    public IEnumerable<int> Iterate() {
        yield return Seed;
        yield return Seed + 1;
    }
}

// Shaped against the declared signature, which is what PatchBody.Declared composes onto: the stub
// hands back the IEnumerable, and this replaces it wholesale.
public static class IteratorReturnInjection {
    public static void Rewrite(ControlHandle<IEnumerable<int>> ch) {
        ch.ReturnValue = [9];
    }
}

// Appends to whatever the target produced instead of discarding it - the shape a "add my gizmos to
// the vanilla ones" patch actually needs.
public static class IteratorAppendInjection {
    public static void Append(ControlHandle<IEnumerable<int>> ch) {
        ch.ReturnValue = Concat(ch.ReturnValue, 99);
    }

    private static IEnumerable<int> Concat(IEnumerable<int> source, int extra) {
        foreach (int value in source) {
            yield return value;
        }

        yield return extra;
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
    public void ResolveBodyTarget_Declared_LeavesIteratorEntryAlone() {
        MethodBase target = typeof(IteratorTarget).GetMethod(nameof(IteratorTarget.Iterate))!;

        Assert.Same(target, WrapperComposer.ResolveBodyTarget(target, PatchBody.Declared));
    }

    [Fact]
    public void ResolveBodyTarget_StateMachine_RedirectsIteratorEntryToMoveNext() {
        MethodBase target = typeof(IteratorTarget).GetMethod(nameof(IteratorTarget.Iterate))!;

        MethodBase resolved = WrapperComposer.ResolveBodyTarget(target, PatchBody.StateMachine);

        Assert.Equal("MoveNext", resolved.Name);
        Assert.NotSame(target, resolved);
    }

    [Fact]
    public void ResolveBodyTarget_StateMachine_LeavesPlainMethodAlone() {
        MethodBase target = typeof(PlainTarget).GetMethod(nameof(PlainTarget.Identity))!;

        Assert.Same(target, WrapperComposer.ResolveBodyTarget(target, PatchBody.StateMachine));
    }

    // The whole point of PatchBody.Declared: a static iterator's stub returns the enumerable, so a
    // Return injection can read and replace it, which is the shape most postfixes want.
    [Fact]
    public void Compose_IteratorEntry_ReturnInjectionReplacesTheEnumerable() {
        MethodBase target = typeof(IteratorTarget).GetMethod(nameof(IteratorTarget.Iterate))!;
        MethodBase injectionMethod =
            typeof(IteratorReturnInjection).GetMethod(nameof(IteratorReturnInjection.Rewrite))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret]);

        using IDetourHandle handle = DetourBackend.Current.Apply(target, result.Wrapper);

        Assert.Equal([9], IteratorTarget.Iterate().ToList());
    }

    [Fact]
    public void Compose_InstanceIteratorEntry_ReturnInjectionAppendsToTheEnumerable() {
        MethodBase target = typeof(InstanceIteratorTarget).GetMethod(nameof(InstanceIteratorTarget.Iterate))!;
        MethodBase injectionMethod =
            typeof(IteratorAppendInjection).GetMethod(nameof(IteratorAppendInjection.Append))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [ret]);

        using IDetourHandle handle = DetourBackend.Current.Apply(target, result.Wrapper);

        Assert.Equal([10, 11, 99], new InstanceIteratorTarget().Iterate().ToList());
    }

    // The injection's ControlHandle<T> is shaped against the declared return type, but selecting
    // StateMachine composes it onto MoveNext, which returns bool. That mismatch must be rejected, not
    // emitted as IL the runtime rejects later with InvalidProgramException.
    [Fact]
    public void ValidateBodySelection_StateMachineWithDeclaredShapedReturnHandle_IsRejected() {
        MethodBase target = typeof(IteratorTarget).GetMethod(nameof(IteratorTarget.Iterate))!;
        MethodBase injectionMethod =
            typeof(IteratorReturnInjection).GetMethod(nameof(IteratorReturnInjection.Rewrite))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0) {
            Body = PatchBody.StateMachine,
        };
        MethodBase resolved = WrapperComposer.ResolveBodyTarget(target, ret.Body);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.ValidateBodySelection(target, resolved, ret));

        Assert.Equal("CONC122", ex.Code);
    }

    [Fact]
    public void ValidateBodySelection_DeclaredWithReturnHandle_IsAccepted() {
        MethodBase target = typeof(IteratorTarget).GetMethod(nameof(IteratorTarget.Iterate))!;
        MethodBase injectionMethod =
            typeof(IteratorReturnInjection).GetMethod(nameof(IteratorReturnInjection.Rewrite))!;
        Injection ret = new Injection(injectionMethod, new InjectAt.Return(0), "test", 0);

        WrapperComposer.ValidateBodySelection(target, target, ret);
    }

    // Around, Constant, Argument and the transpilers all need statements that only exist in MoveNext.
    // Pointing them at the stub matches nothing, so it is rejected rather than silently doing nothing.
    [Fact]
    public void ValidateBodySelection_DeclaredWithTranspiler_IsRejected() {
        MethodBase target = typeof(IteratorTarget).GetMethod(nameof(IteratorTarget.Iterate))!;
        MethodBase injectionMethod =
            typeof(IteratorReturnInjection).GetMethod(nameof(IteratorReturnInjection.Rewrite))!;
        Injection transpiler = new Injection(injectionMethod, new InjectAt.Transpiler(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.ValidateBodySelection(target, target, transpiler));

        Assert.Equal("CONC123", ex.Code);
        Assert.Contains("PatchBody.StateMachine", ex.Message);
    }

    [Fact]
    public void ValidateBodySelection_DeclaredWithAround_IsRejected() {
        MethodBase target = typeof(AsyncTarget).GetMethod(nameof(AsyncTarget.Compute))!;
        MethodBase injectionMethod = typeof(AsyncInjectionMethods).GetMethod(nameof(AsyncInjectionMethods.Observe))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.ValidateBodySelection(target, target, around));

        Assert.Equal("CONC123", ex.Code);
    }

    [Fact]
    public void ValidateBodySelection_PlainTargetWithTranspiler_IsAccepted() {
        MethodBase target = typeof(PlainTarget).GetMethod(nameof(PlainTarget.Identity))!;
        MethodBase injectionMethod =
            typeof(IteratorReturnInjection).GetMethod(nameof(IteratorReturnInjection.Rewrite))!;
        Injection transpiler = new Injection(injectionMethod, new InjectAt.Transpiler(), "test", 0);

        WrapperComposer.ValidateBodySelection(target, target, transpiler);
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
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0) {
            Body = PatchBody.StateMachine,
        };
        MethodBase moveNext = WrapperComposer.ResolveBodyTarget(target, head.Body);

        ComposeResult result = WrapperComposer.Compose(moveNext, [head]);

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
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0) {
            Body = PatchBody.StateMachine,
        };
        MethodBase moveNext = WrapperComposer.ResolveBodyTarget(target, head.Body);

        ComposeResult result = WrapperComposer.Compose(moveNext, [head]);

        using IDetourHandle handle = DetourBackend.Current.Apply(moveNext, result.Wrapper);

        AsyncObservations.MoveNextSteps = 0;
        int value = await new AsyncTarget().Compute();

        Assert.Equal(42, value);
        Assert.True(AsyncObservations.MoveNextSteps > 0);
    }
}
