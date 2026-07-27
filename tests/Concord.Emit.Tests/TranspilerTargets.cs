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

    public static int TryCatchFinally(int mode) {
        int total = 0;
        try {
            total += mode;
            if (mode == 1) {
                throw new InvalidOperationException();
            }
        } catch (InvalidOperationException) {
            total = 1;
        } finally {
            total += 100;
        }

        return total;
    }

    public static int TryCatchCatchFinally(int mode) {
        int total = 0;
        try {
            total += mode;
            if (mode == 1) {
                throw new InvalidOperationException();
            }

            if (mode == 2) {
                throw new ArgumentException();
            }
        } catch (InvalidOperationException) {
            total = 1;
        } catch (ArgumentException) {
            total = 2;
        } finally {
            total += 100;
        }

        return total;
    }

    public static int TryNestedInCatch(int mode) {
        int total = 0;
        try {
            total += mode;
            if (mode != 0) {
                throw new InvalidOperationException();
            }
        } catch (InvalidOperationException) {
            try {
                total += 1;
                if (mode == 2) {
                    throw new ArgumentException();
                }
            } catch (ArgumentException) {
                total += 2;
            }
        }

        return total;
    }

    public static int TryNestedInFinally(int mode) {
        int total = 0;
        try {
            total += mode;
        } finally {
            try {
                total += 1;
                if (mode == 2) {
                    throw new ArgumentException();
                }
            } catch (ArgumentException) {
                total += 2;
            }
        }

        return total;
    }

    // Every path throws, so Roslyn emits no trailing `ret` and the outermost handler's HandlerEnd
    // is null (legally - the handler runs to the literal end of the method body).
    public static int AlwaysThrows(int mode) {
        try {
            throw new InvalidOperationException();
        } catch (InvalidOperationException) {
            throw new ArgumentException();
        }
    }

    public static int AlwaysThrowsFinally(int mode) {
        try {
            throw new InvalidOperationException();
        } finally {
            Console.Write(mode);
        }
    }

    public static unsafe int Fault(int mode) {
        int[] local = new int[1];
        fixed (int* p = &local[0]) {
            *p = mode;
        }

        return local[0];
    }
}
