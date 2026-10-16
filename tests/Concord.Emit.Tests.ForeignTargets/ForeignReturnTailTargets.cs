using System.Collections.Generic;

namespace Concord.Emit.Tests.ForeignTargets;

public static class ForeignReturnTailTargets {
    public static string GuardedLoopThenReturn(List<string> parts, bool extra) {
        string text = "x";
        if (extra) {
            text = text + "\n\n" + parts.Count;
        }

        if (parts != null) {
            foreach (string part in parts) {
                if (part == null) {
                    continue;
                }

                text = text + "\n\n" + part;
            }
        }

        return text;
    }

    public static float ScaleAndReturn(int scale) {
        return scale * 2f;
    }
}
