namespace Concord.Emit.Tests;

public static class TranspilerTargets {
    public static int Simple(int a, int b) {
        return a + b;
    }

    public static int Priced() {
        return 5;
    }

    public static int MultiCatch(int mode) {
        try {
            if (mode == 1) {
                throw new InvalidOperationException();
            }

            if (mode == 2) {
                throw new ArgumentException();
            }

            return 0;
        } catch (InvalidOperationException) {
            return 1;
        } catch (ArgumentException) {
            return 2;
        }
    }

    public static int Filtered(int mode) {
        try {
            if (mode == 1) {
                throw new InvalidOperationException("filter me");
            }

            return 0;
        } catch (InvalidOperationException ex) when (ex.Message.Length > 3) {
            return 1;
        }
    }

    public static int Finally(int mode) {
        int total = 0;
        try {
            total += mode;
            return total;
        } finally {
            total = 0;
        }
    }

    public static int Switched(int mode) {
        switch (mode) {
            case 0: return 10;
            case 1: return 11;
            case 2: return 12;
            default: return -1;
        }
    }

    public static T Generic<T>(T value) {
        return value;
    }

    public static unsafe int Fault(int mode) {
        int[] local = new int[1];
        fixed (int* p = &local[0]) {
            *p = mode;
        }

        return local[0];
    }
}
