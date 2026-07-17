using Concord;

namespace Concord.Emit.Tests.NeutralRoundTrip;

public static class ExceptionRegionTargets {
    public static List<string> Trace = new List<string>();

    public static int TryCatch(int x) {
        try {
            Trace.Add("try");
            if (x == 0) {
                throw new InvalidOperationException("boom");
            }

            return x * 2;
        } catch (InvalidOperationException) {
            Trace.Add("catch");
            return -1;
        }
    }

    public static int TryFinally(int x) {
        int result;
        try {
            Trace.Add("try");
            result = x * 2;
        } finally {
            Trace.Add("finally");
        }

        return result;
    }

    public static int NestedTryCatchFinally(int x) {
        int result = 0;
        try {
            Trace.Add("outer-try");
            try {
                Trace.Add("inner-try");
                if (x == 0) {
                    throw new InvalidOperationException("inner-boom");
                }

                result = x * 3;
            } catch (InvalidOperationException) {
                Trace.Add("inner-catch");
                result = -2;
            } finally {
                Trace.Add("inner-finally");
            }
        } finally {
            Trace.Add("outer-finally");
        }

        return result;
    }
}

public static class ExceptionRegionInjectionMethods {
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
