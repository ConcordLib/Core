using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class GetExecAssemblyTargets {
    public static Assembly? Observed;

    public static int Plain(int a, int b) {
        return a + b;
    }

    public static Assembly SelfReporting() {
        return Assembly.GetExecutingAssembly();
    }

    public static void Head(ControlHandle ch) {
        Observed = Assembly.GetExecutingAssembly();
    }

    public static int WrapPlain(int a, int b, Operation<int, int, int> original) {
        Observed = Assembly.GetExecutingAssembly();
        return original.Invoke(a, b);
    }
}

public sealed class GetExecutingAssemblyTests {
    [Fact]
    public void Compose_InjectionCallingGetExecutingAssembly_ObservesInjectionAssembly() {
        MethodBase target = typeof(GetExecAssemblyTargets).GetMethod(nameof(GetExecAssemblyTargets.Plain))!;
        MethodBase injectionMethod = typeof(GetExecAssemblyTargets).GetMethod(nameof(GetExecAssemblyTargets.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        GetExecAssemblyTargets.Observed = null;
        ComposeResult result = WrapperComposer.Compose(target, [head]);
        Assert.Equal(5, result.Wrapper.Invoke(null, [2, 3]));

        Assert.Same(typeof(GetExecAssemblyTargets).Assembly, GetExecAssemblyTargets.Observed);
    }

    [Fact]
    public void Compose_AroundInjectionCallingGetExecutingAssembly_ObservesInjectionAssembly() {
        MethodBase target = typeof(GetExecAssemblyTargets).GetMethod(nameof(GetExecAssemblyTargets.Plain))!;
        MethodBase injectionMethod = typeof(GetExecAssemblyTargets).GetMethod(nameof(GetExecAssemblyTargets.WrapPlain))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        GetExecAssemblyTargets.Observed = null;
        ComposeResult result = WrapperComposer.Compose(target, [around]);
        Assert.Equal(5, result.Wrapper.Invoke(null, [2, 3]));

        Assert.Same(typeof(GetExecAssemblyTargets).Assembly, GetExecAssemblyTargets.Observed);
    }

    [Fact]
    public void Compose_TargetBodyCallingGetExecutingAssembly_StillObservesTargetAssembly() {
        MethodBase target = typeof(GetExecAssemblyTargets).GetMethod(nameof(GetExecAssemblyTargets.SelfReporting))!;
        MethodBase injectionMethod = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Bump))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head]);
        object? value = result.Wrapper.Invoke(null, []);

        Assert.Same(typeof(GetExecAssemblyTargets).Assembly, value);
    }
}

public static class GetExecAssemblyTranspilers {
    public static IEnumerable<CodeInstruction> EmitGetExecutingAssembly(IEnumerable<CodeInstruction> instructions) {
        yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, typeof(Assembly).GetMethod(nameof(Assembly.GetExecutingAssembly))!);
        yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Pop);
        foreach (CodeInstruction instruction in instructions) {
            yield return instruction;
        }
    }
}

public sealed class GetExecutingAssemblyTranspilerGuardTests {
    [Fact]
    public void Compose_TranspilerEmittingGetExecutingAssembly_ThrowsConc143() {
        MethodBase target = typeof(GetExecAssemblyTargets).GetMethod(nameof(GetExecAssemblyTargets.Plain))!;
        MethodBase transpiler = typeof(GetExecAssemblyTranspilers).GetMethod(nameof(GetExecAssemblyTranspilers.EmitGetExecutingAssembly))!;
        Injection injection = new Injection(transpiler, new InjectAt.Transpiler(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [injection]));

        Assert.Equal("CONC143", ex.Code);
        Assert.Contains("EmitGetExecutingAssembly", ex.Message);
    }
}
