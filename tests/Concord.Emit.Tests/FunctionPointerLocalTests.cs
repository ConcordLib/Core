#if NET
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class FunctionPointerTargets {
    public static int Counter { get; set; }

    public static unsafe int SizedArray(int seed) {
        delegate*<int, int>[] table = new delegate*<int, int>[2];
        table[0] = &Step;
        table[1] = &Step;
        return table[0](seed) + table[1](seed);
    }

    public static unsafe void Bump() {
        delegate*<int, int> fp = &Step;
        Counter += fp(1);
    }

    public static unsafe int JaggedAndPointer(int seed) {
        delegate*<int, int>[][] jagged = new delegate*<int, int>[1][];
        jagged[0] = new delegate*<int, int>[1];
        jagged[0][0] = &Step;
        delegate*<int, int>* indirect = stackalloc delegate*<int, int>[1];
        indirect[0] = &Step;
        return jagged[0][0](seed) + indirect[0](seed);
    }

    public static unsafe int ByRefLocal(int seed) {
        delegate*<int, int> fp = &Step;
        ref delegate*<int, int> slot = ref fp;
        return slot(seed);
    }

    private static int Step(int value) => value + 1;
}

public sealed class FunctionPointerLocalTests {
    [Fact]
    public void Compose_SpineFunctionPointerLocal_RunsAndReturnsTheRightValue() {
        MethodBase target = typeof(ProvHost).GetMethod(nameof(ProvHost.ViaPointer))!;

        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.Equal(16, result.Wrapper.Invoke(null, [new ProvHost(), 5]));
    }

    [Fact]
    public void Compose_InjectionFunctionPointerLocal_RunsAndReturnsTheRightValue() {
        MethodBase target = typeof(ProvHost).GetMethod(nameof(ProvHost.ViaPointer))!;
        MethodBase injectionMethod = typeof(FunctionPointerTargets).GetMethod(nameof(FunctionPointerTargets.Bump))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "fnptr", 0);

        FunctionPointerTargets.Counter = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head]);

        Assert.Equal(16, result.Wrapper.Invoke(null, [new ProvHost(), 5]));
        Assert.Equal(2, FunctionPointerTargets.Counter);
    }

    [Fact]
    public void Compose_FunctionPointerTypeOperand_RunsAndReturnsTheRightValue() {
        MethodBase target = typeof(FunctionPointerTargets).GetMethod(nameof(FunctionPointerTargets.SizedArray))!;

        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.Equal(12, result.Wrapper.Invoke(null, [5]));
    }

    [Fact]
    public void CapturedBody_RefusesTheCalliSignatureByName() {
        MethodBase target = typeof(ProvHost).GetMethod(nameof(ProvHost.ViaPointer))!;

        WrapperComposer.CaptureComposedBody = true;
        ComposeResult result;
        try {
            result = WrapperComposer.Compose(target, []);
        } finally {
            WrapperComposer.CaptureComposedBody = false;
        }

        Assert.Contains("CONC157", result.BodyRejection);
        Assert.Contains("calli", result.BodyRejection);
    }

    [Fact]
    public void Compose_JaggedAndPointerToFunctionPointer_RunsAndReturnsTheRightValue() {
        MethodBase target = typeof(FunctionPointerTargets).GetMethod(nameof(FunctionPointerTargets.JaggedAndPointer))!;

        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.Equal(12, result.Wrapper.Invoke(null, [5]));
    }

    [Fact]
    public void Compose_ByRefFunctionPointerLocal_RunsAndReturnsTheRightValue() {
        MethodBase target = typeof(FunctionPointerTargets).GetMethod(nameof(FunctionPointerTargets.ByRefLocal))!;

        ComposeResult result = WrapperComposer.Compose(target, []);

        Assert.Equal(6, result.Wrapper.Invoke(null, [5]));
    }
}
#endif
