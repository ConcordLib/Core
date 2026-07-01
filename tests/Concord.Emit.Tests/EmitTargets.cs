namespace Concord.Emit.Tests;

public static class EmitTargets {
    public static int Counter;

    public static int Add(int a, int b) {
        return a + b;
    }

    public static int Tracked(int a, int b) {
        return a + b;
    }

    public static void Bump(ControlHandle ch) {
        Counter++;
    }
}

public class Counterish {
    public int N;

    public int Step(int delta) {
        return N += delta;
    }
}

public class CounterInjectionMethod : Counterish {
    public void Head(ControlHandle ch, int delta) { N += delta * 10; }
}
