using Concord;

namespace Concord.Emit.Tests.NeutralRoundTrip;

public static class StraightLineTargets {
    public static int Add(int a, int b) {
        int sum = a + b;
        int doubled = sum * 2;
        return doubled;
    }
}

public static class StraightLineInjectionMethods {
    public static List<string> Log = new List<string>();

    public static void Head(ControlHandle ch) {
        Log.Add("head");
    }

    public static void Return(ControlHandle<int> ch) {
        Log.Add("return:" + ch.ReturnValue);
        ch.ReturnValue += 1;
    }

    public static int Around(int a, int b, Operation<int, int, int> original) {
        Log.Add("pre");
        int result = original.Invoke(a, b);
        Log.Add("post");
        return result;
    }
}
