namespace Concord.Benchmarks;

public static class BenchHeads {
    public static void ValueReturnHead(ControlHandle ch, int a, int b) { } // NOSONAR empty benchmark head body by design
    public static void RefReturnHead(ControlHandle ch, string s) { } // NOSONAR empty benchmark head body by design
    public static void InstanceHead(ControlHandle ch, int x) { } // NOSONAR empty benchmark head body by design
}
