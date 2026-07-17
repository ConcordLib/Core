using Concord;

namespace Concord.Emit.Tests.NeutralRoundTrip;

public sealed class InstanceClassTarget {
    public int Base;

    public InstanceClassTarget(int seedBase) {
        Base = seedBase;
    }

    public int AddToBase(int amount) {
        int result = Base + amount;
        return result;
    }
}

public struct InstanceStructTarget {
    public int Base;

    public InstanceStructTarget(int seedBase) {
        Base = seedBase;
    }

    public int AddToBase(int amount) {
        int result = Base + amount;
        return result;
    }
}

public static class InstanceInjectionMethods {
    public static List<string> Log = new List<string>();

    public static void Head(ControlHandle ch) {
        Log.Add("head");
    }

    public static void Return(ControlHandle<int> ch) {
        Log.Add("return:" + ch.ReturnValue);
    }

    public static int AroundAddToBase(int amount, Operation<int, int> original) {
        Log.Add("pre");
        int result = original.Invoke(amount);
        Log.Add("post");
        return result;
    }
}
