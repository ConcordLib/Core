using Concord;

namespace Concord.Emit.Tests.NeutralRoundTrip;

public static class EarlyReturnTargets {
    public static int Classify(int x) {
        if (x < 0) {
            return -1;
        }

        if (x == 0) {
            return 0;
        }

        if (x < 10) {
            return 1;
        }

        return 2;
    }
}

public static class EarlyReturnInjectionMethods {
    public static List<string> Log = new List<string>();

    public static void Head(ControlHandle ch) {
        Log.Add("head");
    }

    public static void Return(ControlHandle<int> ch) {
        Log.Add("return:" + ch.ReturnValue);
    }

    public static void Tail(ControlHandle<int> ch) {
        Log.Add("tail:" + ch.ReturnValue);
    }

    public static int Around(int x, Operation<int, int> original) {
        Log.Add("pre");
        int result = original.Invoke(x);
        Log.Add("post");
        return result;
    }
}
