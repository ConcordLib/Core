using System.Reflection;
using Concord;

namespace Concord.Harmony;

internal static partial class InfixMapping {
    internal static TResult Run0<TResult>(Operation<TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = System.Array.Empty<object?>();
        object? result = default(TResult);
        if (infix.Prefix(null, null, args, ref result)) {
            result = op.Invoke();
        }

        return (TResult)infix.Postfix(null, null, args, result)!;
    }

    internal static TResult Run1<T1, TResult>(T1 a1, Operation<T1, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1 };
        object? result = default(TResult);
        if (infix.Prefix(null, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!);
        }

        return (TResult)infix.Postfix(null, null, args, result)!;
    }

    internal static TResult Run2<T1, T2, TResult>(T1 a1, T2 a2, Operation<T1, T2, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2 };
        object? result = default(TResult);
        if (infix.Prefix(null, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!);
        }

        return (TResult)infix.Postfix(null, null, args, result)!;
    }

    internal static TResult Run3<T1, T2, T3, TResult>(T1 a1, T2 a2, T3 a3, Operation<T1, T2, T3, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3 };
        object? result = default(TResult);
        if (infix.Prefix(null, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!);
        }

        return (TResult)infix.Postfix(null, null, args, result)!;
    }

    internal static TResult Run4<T1, T2, T3, T4, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, Operation<T1, T2, T3, T4, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4 };
        object? result = default(TResult);
        if (infix.Prefix(null, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!);
        }

        return (TResult)infix.Postfix(null, null, args, result)!;
    }

    internal static TResult Run5<T1, T2, T3, T4, T5, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, Operation<T1, T2, T3, T4, T5, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5 };
        object? result = default(TResult);
        if (infix.Prefix(null, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!);
        }

        return (TResult)infix.Postfix(null, null, args, result)!;
    }

    internal static TResult Run6<T1, T2, T3, T4, T5, T6, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, Operation<T1, T2, T3, T4, T5, T6, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6 };
        object? result = default(TResult);
        if (infix.Prefix(null, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!);
        }

        return (TResult)infix.Postfix(null, null, args, result)!;
    }

    internal static TResult Run7<T1, T2, T3, T4, T5, T6, T7, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, Operation<T1, T2, T3, T4, T5, T6, T7, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6, a7 };
        object? result = default(TResult);
        if (infix.Prefix(null, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!);
        }

        return (TResult)infix.Postfix(null, null, args, result)!;
    }

    internal static TResult Run8<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, Operation<T1, T2, T3, T4, T5, T6, T7, T8, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6, a7, a8 };
        object? result = default(TResult);
        if (infix.Prefix(null, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!);
        }

        return (TResult)infix.Postfix(null, null, args, result)!;
    }

    internal static void RunVoid0(Operation op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = System.Array.Empty<object?>();
        object? result = null;
        if (infix.Prefix(null, null, args, ref result)) {
            op.Invoke();
        }

        infix.Postfix(null, null, args, null);
    }

    internal static void RunVoid1<T1>(T1 a1, VoidOperation<T1> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1 };
        object? result = null;
        if (infix.Prefix(null, null, args, ref result)) {
            op.Invoke((T1)args[0]!);
        }

        infix.Postfix(null, null, args, null);
    }

    internal static void RunVoid2<T1, T2>(T1 a1, T2 a2, VoidOperation<T1, T2> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2 };
        object? result = null;
        if (infix.Prefix(null, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!);
        }

        infix.Postfix(null, null, args, null);
    }

    internal static void RunVoid3<T1, T2, T3>(T1 a1, T2 a2, T3 a3, VoidOperation<T1, T2, T3> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3 };
        object? result = null;
        if (infix.Prefix(null, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!);
        }

        infix.Postfix(null, null, args, null);
    }

    internal static void RunVoid4<T1, T2, T3, T4>(T1 a1, T2 a2, T3 a3, T4 a4, VoidOperation<T1, T2, T3, T4> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4 };
        object? result = null;
        if (infix.Prefix(null, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!);
        }

        infix.Postfix(null, null, args, null);
    }

    internal static void RunVoid5<T1, T2, T3, T4, T5>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, VoidOperation<T1, T2, T3, T4, T5> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5 };
        object? result = null;
        if (infix.Prefix(null, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!);
        }

        infix.Postfix(null, null, args, null);
    }

    internal static void RunVoid6<T1, T2, T3, T4, T5, T6>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, VoidOperation<T1, T2, T3, T4, T5, T6> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6 };
        object? result = null;
        if (infix.Prefix(null, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!);
        }

        infix.Postfix(null, null, args, null);
    }

    internal static void RunVoid7<T1, T2, T3, T4, T5, T6, T7>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, VoidOperation<T1, T2, T3, T4, T5, T6, T7> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6, a7 };
        object? result = null;
        if (infix.Prefix(null, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!);
        }

        infix.Postfix(null, null, args, null);
    }

    internal static void RunVoid8<T1, T2, T3, T4, T5, T6, T7, T8>(T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, VoidOperation<T1, T2, T3, T4, T5, T6, T7, T8> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6, a7, a8 };
        object? result = null;
        if (infix.Prefix(null, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!);
        }

        infix.Postfix(null, null, args, null);
    }

    internal static TResult RunSelf0<TSelf, TResult>(TSelf self, Operation<TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = System.Array.Empty<object?>();
        object? result = default(TResult);
        if (infix.Prefix(self, null, args, ref result)) {
            result = op.Invoke();
        }

        return (TResult)infix.Postfix(self, null, args, result)!;
    }

    internal static TResult RunSelf1<TSelf, T1, TResult>(TSelf self, T1 a1, Operation<T1, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1 };
        object? result = default(TResult);
        if (infix.Prefix(self, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!);
        }

        return (TResult)infix.Postfix(self, null, args, result)!;
    }

    internal static TResult RunSelf2<TSelf, T1, T2, TResult>(TSelf self, T1 a1, T2 a2, Operation<T1, T2, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2 };
        object? result = default(TResult);
        if (infix.Prefix(self, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!);
        }

        return (TResult)infix.Postfix(self, null, args, result)!;
    }

    internal static TResult RunSelf3<TSelf, T1, T2, T3, TResult>(TSelf self, T1 a1, T2 a2, T3 a3, Operation<T1, T2, T3, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3 };
        object? result = default(TResult);
        if (infix.Prefix(self, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!);
        }

        return (TResult)infix.Postfix(self, null, args, result)!;
    }

    internal static TResult RunSelf4<TSelf, T1, T2, T3, T4, TResult>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, Operation<T1, T2, T3, T4, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4 };
        object? result = default(TResult);
        if (infix.Prefix(self, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!);
        }

        return (TResult)infix.Postfix(self, null, args, result)!;
    }

    internal static TResult RunSelf5<TSelf, T1, T2, T3, T4, T5, TResult>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, Operation<T1, T2, T3, T4, T5, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5 };
        object? result = default(TResult);
        if (infix.Prefix(self, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!);
        }

        return (TResult)infix.Postfix(self, null, args, result)!;
    }

    internal static TResult RunSelf6<TSelf, T1, T2, T3, T4, T5, T6, TResult>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, Operation<T1, T2, T3, T4, T5, T6, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6 };
        object? result = default(TResult);
        if (infix.Prefix(self, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!);
        }

        return (TResult)infix.Postfix(self, null, args, result)!;
    }

    internal static TResult RunSelf7<TSelf, T1, T2, T3, T4, T5, T6, T7, TResult>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, Operation<T1, T2, T3, T4, T5, T6, T7, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6, a7 };
        object? result = default(TResult);
        if (infix.Prefix(self, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!);
        }

        return (TResult)infix.Postfix(self, null, args, result)!;
    }

    internal static TResult RunSelf8<TSelf, T1, T2, T3, T4, T5, T6, T7, T8, TResult>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, Operation<T1, T2, T3, T4, T5, T6, T7, T8, TResult> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6, a7, a8 };
        object? result = default(TResult);
        if (infix.Prefix(self, null, args, ref result)) {
            result = op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!);
        }

        return (TResult)infix.Postfix(self, null, args, result)!;
    }

    internal static void RunSelfVoid0<TSelf>(TSelf self, Operation op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = System.Array.Empty<object?>();
        object? result = null;
        if (infix.Prefix(self, null, args, ref result)) {
            op.Invoke();
        }

        infix.Postfix(self, null, args, null);
    }

    internal static void RunSelfVoid1<TSelf, T1>(TSelf self, T1 a1, VoidOperation<T1> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1 };
        object? result = null;
        if (infix.Prefix(self, null, args, ref result)) {
            op.Invoke((T1)args[0]!);
        }

        infix.Postfix(self, null, args, null);
    }

    internal static void RunSelfVoid2<TSelf, T1, T2>(TSelf self, T1 a1, T2 a2, VoidOperation<T1, T2> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2 };
        object? result = null;
        if (infix.Prefix(self, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!);
        }

        infix.Postfix(self, null, args, null);
    }

    internal static void RunSelfVoid3<TSelf, T1, T2, T3>(TSelf self, T1 a1, T2 a2, T3 a3, VoidOperation<T1, T2, T3> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3 };
        object? result = null;
        if (infix.Prefix(self, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!);
        }

        infix.Postfix(self, null, args, null);
    }

    internal static void RunSelfVoid4<TSelf, T1, T2, T3, T4>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, VoidOperation<T1, T2, T3, T4> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4 };
        object? result = null;
        if (infix.Prefix(self, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!);
        }

        infix.Postfix(self, null, args, null);
    }

    internal static void RunSelfVoid5<TSelf, T1, T2, T3, T4, T5>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, VoidOperation<T1, T2, T3, T4, T5> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5 };
        object? result = null;
        if (infix.Prefix(self, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!);
        }

        infix.Postfix(self, null, args, null);
    }

    internal static void RunSelfVoid6<TSelf, T1, T2, T3, T4, T5, T6>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, VoidOperation<T1, T2, T3, T4, T5, T6> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6 };
        object? result = null;
        if (infix.Prefix(self, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!);
        }

        infix.Postfix(self, null, args, null);
    }

    internal static void RunSelfVoid7<TSelf, T1, T2, T3, T4, T5, T6, T7>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, VoidOperation<T1, T2, T3, T4, T5, T6, T7> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6, a7 };
        object? result = null;
        if (infix.Prefix(self, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!);
        }

        infix.Postfix(self, null, args, null);
    }

    internal static void RunSelfVoid8<TSelf, T1, T2, T3, T4, T5, T6, T7, T8>(TSelf self, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6, T7 a7, T8 a8, VoidOperation<T1, T2, T3, T4, T5, T6, T7, T8> op, [Bound] int slot) {
        Infix infix = infixes[slot];
        object?[] args = { a1, a2, a3, a4, a5, a6, a7, a8 };
        object? result = null;
        if (infix.Prefix(self, null, args, ref result)) {
            op.Invoke((T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!);
        }

        infix.Postfix(self, null, args, null);
    }

    internal static MethodInfo Shim(int arity, bool isVoid, bool hasSelf, System.Type? outerType) {
        string name = (hasSelf ? "RunSelf" : "Run") + (isVoid ? "Void" : string.Empty) + arity;
        if (outerType == null) {
            return typeof(InfixMapping).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        }

        return typeof(OuterShim<>).MakeGenericType(outerType).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    }
}
