namespace Concord.Benchmarks;

public static class BenchHeads {
    public static void ValueReturnHead(ControlHandle ch, int a, int b) { } // NOSONAR empty benchmark head body by design
    public static void RefReturnHead(ControlHandle ch, string s) { } // NOSONAR empty benchmark head body by design
    public static void InstanceHead(ControlHandle ch, int x) { } // NOSONAR empty benchmark head body by design
    public static void InstanceTargetHead(ControlHandle ch, int x) { } // NOSONAR empty benchmark head body by design

    public static void ControlHandleReturnHead(ControlHandle<int> ch, int x) {
        ch.ReturnValue = ch.ReturnValue + 1; // NOSONAR mutation for benchmark
    }

    public static void ControlHandleCancelHead(ControlHandle ch) {
        ch.Cancel(); // NOSONAR cancel for benchmark
    }

    public static int AroundSpliceHead(int x, int y) {
        return BenchTargets.AroundSplice(x, y) + 1; // NOSONAR around splice for benchmark
    }

    public static int InvokeWrapHead(int x) {
        return BenchTargets.InvokeWrap(x) * 2; // NOSONAR invoke wrap for benchmark
    }

    public static void TryFinallyTargetHead(ControlHandle<int> ch, int x) { } // NOSONAR empty head for try/finally benchmark

    public static void StackedInjectionsHead1(ControlHandle<int> ch, int x) {
        ch.ReturnValue = ch.ReturnValue + 1; // NOSONAR stacked injection 1
    }

    public static void StackedInjectionsHead2(ControlHandle<int> ch, int x) {
        ch.ReturnValue = ch.ReturnValue + 2; // NOSONAR stacked injection 2
    }

    public static void StackedInjectionsHead3(ControlHandle<int> ch, int x) {
        ch.ReturnValue = ch.ReturnValue + 3; // NOSONAR stacked injection 3
    }
}
