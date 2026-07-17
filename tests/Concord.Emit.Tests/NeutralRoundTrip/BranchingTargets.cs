using Concord;

namespace Concord.Emit.Tests.NeutralRoundTrip;

public static class BranchingTargets {
    public static int ClassifyAndDouble(int x) {
        int classified;
        if (x < 0) {
            classified = -1;
        } else if (x == 0) {
            classified = 0;
        } else {
            classified = 1;
        }

        return classified * 2;
    }

    public static int SwitchOnValue(int which) {
        switch (which) {
            case 0:
                return 10;
            case 1:
                return 20;
            case 2:
                return 30;
            case 3:
                return 40;
            default:
                return -1;
        }
    }
}

public static class BranchingInjectionMethods {
    public static List<string> Log = new List<string>();

    public static void Head(ControlHandle ch) {
        Log.Add("head");
    }

    public static void Return(ControlHandle<int> ch) {
        Log.Add("return:" + ch.ReturnValue);
    }

    public static int AroundClassify(int x, Operation<int, int> original) {
        Log.Add("pre");
        int result = original.Invoke(x);
        Log.Add("post");
        return result;
    }

    public static int AroundSwitch(int which, Operation<int, int> original) {
        Log.Add("pre");
        int result = original.Invoke(which);
        Log.Add("post");
        return result;
    }
}
