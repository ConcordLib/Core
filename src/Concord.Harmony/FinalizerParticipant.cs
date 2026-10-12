using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Emit;

namespace Concord.Harmony;

internal static class FinalizerParticipant {
    internal const string TerminalOwner = "concord.harmony.finalizer";

    private const byte Untouched = 0;

    private const byte Returning = 1;

    private const byte Finalized = 2;

    private const byte Catching = 3;

    private static readonly object Gate = new object();

    private static readonly Dictionary<(MethodInfo Finalizer, Type ReturnType), int> Slots = new Dictionary<(MethodInfo Finalizer, Type ReturnType), int>();

    private static Delegate[] adapters = Array.Empty<Delegate>();

    internal static string? Validate(MethodBase target, MethodInfo finalizer) {
        return HarmonyFinalizerShape.Validate(target, finalizer);
    }

    internal static IReadOnlyList<Injection> Map(MethodBase target, MethodInfo finalizer, string owner, int harmonyPriority, string[]? before = null, string[]? after = null) {
        HarmonyFinalizerShape shape = HarmonyFinalizerShape.Create(target, finalizer);
        Type returnType = HarmonyFinalizerShape.ReturnTypeOf(target);
        int slot = Register(shape, returnType);
        Dictionary<string, object?> bound = new Dictionary<string, object?> { ["slot"] = slot };
        string[] beforeOwners = before ?? Array.Empty<string>();
        string[] afterOwners = after ?? Array.Empty<string>();
        Type? receiver = ReceiverFor(target);

        return new[] {
            new Injection(Shim(nameof(OnReturn), nameof(OnReturnVoid), returnType, receiver), new InjectAt.Return(0), owner, 0) {
                ForeignOrigin = true, BoundArguments = bound, BeforeOwners = beforeOwners, AfterOwners = afterOwners,
            },
            new Injection(Shim(nameof(OnThrow), nameof(OnThrowVoid), returnType, receiver), new InjectAt.Catch(), owner, 0) {
                ForeignOrigin = true, BoundArguments = bound, BeforeOwners = beforeOwners, AfterOwners = afterOwners,
            },
            new Injection(Shim(nameof(ReturnTerminal), nameof(ReturnTerminalVoid), returnType, receiver), new InjectAt.Return(0), TerminalOwner, int.MaxValue) {
                ReplayOnCancel = true, RunsAfterForeign = true,
            },
            new Injection(Shim(nameof(CatchTerminal), nameof(CatchTerminalVoid), returnType, receiver), new InjectAt.Catch(), TerminalOwner, int.MaxValue) {
                RunsAfterForeign = true,
            },
        };
    }

    internal static Type? ReceiverFor(MethodBase target) {
        if (target.IsStatic) {
            return null;
        }

        Type? declaringType = target.DeclaringType;
        if (declaringType == null || declaringType.IsValueType || declaringType.ContainsGenericParameters) {
            return null;
        }

        return declaringType;
    }

    internal static void OnReturn<T>(ControlHandle<T> control, [Bound] int slot) {
        Pass pass = control.GetState<Pass>();
        T result = control.ReturnValue;
        pass = ReturnPass(pass, slot, null, ref result);
        control.ReturnValue = result;
        control.SetState(pass);
    }

    internal static void OnReturnVoid(ControlHandle control, [Bound] int slot) {
        Pass pass = control.GetState<Pass>();
        pass = ReturnPassVoid(pass, slot, null);
        control.SetState(pass);
    }

    internal static void ReturnTerminal<T>(ControlHandle<T> control) {
        Pass pass = control.GetState<Pass>();
        if (pass.Phase != Returning || pass.Current == null) {
            return;
        }

        pass.Phase = Finalized;
        control.SetState(pass);
        throw pass.Current;
    }

    internal static void ReturnTerminalVoid(ControlHandle control) {
        Pass pass = control.GetState<Pass>();
        if (pass.Phase != Returning || pass.Current == null) {
            return;
        }

        pass.Phase = Finalized;
        control.SetState(pass);
        throw pass.Current;
    }

    internal static void OnThrow<T>(ControlHandle<T> control, [Bound] int slot) {
        Pass pass = control.GetState<Pass>();
        if (pass.Phase == Finalized) {
            return;
        }

        Exception caught = control.Exception;
        T result = control.ReturnValue;
        pass = ThrowPass(pass, slot, null, caught, ref result);
        control.ReturnValue = result;
        control.SetState(pass);
    }

    internal static void OnThrowVoid(ControlHandle control, [Bound] int slot) {
        Pass pass = control.GetState<Pass>();
        if (pass.Phase == Finalized) {
            return;
        }

        Exception caught = control.Exception;
        pass = ThrowPassVoid(pass, slot, null, caught);
        control.SetState(pass);
    }

    internal static void CatchTerminal<T>(ControlHandle<T> control) {
        Pass pass = control.GetState<Pass>();
        if (pass.Phase == Finalized) {
            control.Rethrow();
            return;
        }

        Exception? current = pass.Phase == Catching ? pass.Current : control.Exception;
        if (current == null) {
            control.Cancel();
            return;
        }

        if (ReferenceEquals(current, control.Exception)) {
            control.Rethrow();
            return;
        }

        throw current;
    }

    internal static void CatchTerminalVoid(ControlHandle control) {
        Pass pass = control.GetState<Pass>();
        if (pass.Phase == Finalized) {
            control.Rethrow();
            return;
        }

        Exception? current = pass.Phase == Catching ? pass.Current : control.Exception;
        if (current == null) {
            control.Cancel();
            return;
        }

        if (ReferenceEquals(current, control.Exception)) {
            control.Rethrow();
            return;
        }

        throw current;
    }

    private static Pass ReturnPass<T>(Pass pass, int slot, object? instance, ref T result) {
        if (pass.Phase == Untouched) {
            pass.Phase = Returning;
        }

        pass.Current = Invoke(slot, pass.Current, instance, ref result);
        return pass;
    }

    private static Pass ReturnPassVoid(Pass pass, int slot, object? instance) {
        if (pass.Phase == Untouched) {
            pass.Phase = Returning;
        }

        pass.Current = InvokeVoid(slot, pass.Current, instance);
        return pass;
    }

    private static Pass ThrowPass<T>(Pass pass, int slot, object? instance, Exception caught, ref T result) {
        if (pass.Phase != Catching) {
            pass.Phase = Catching;
            pass.Current = caught;
        }

        pass.Current = RunSwallowingThrows(slot, pass.Current, instance, ref result);
        return pass;
    }

    private static Pass ThrowPassVoid(Pass pass, int slot, object? instance, Exception caught) {
        if (pass.Phase != Catching) {
            pass.Phase = Catching;
            pass.Current = caught;
        }

        pass.Current = RunVoidSwallowingThrows(slot, pass.Current, instance);
        return pass;
    }

    private static Exception? Invoke<T>(int slot, Exception? current, object? instance, ref T result) {
        Delegate adapter = adapters[slot];
        if (adapter is FinalizeResultOn<T> onInstance) {
            return onInstance(current, ref result, instance);
        }

        return ((FinalizeResult<T>)adapter)(current, ref result);
    }

    private static Exception? InvokeVoid(int slot, Exception? current, object? instance) {
        Delegate adapter = adapters[slot];
        if (adapter is FinalizeVoidOn onInstance) {
            return onInstance(current, instance);
        }

        return ((FinalizeVoid)adapter)(current);
    }

    private static Exception? RunSwallowingThrows<T>(int slot, Exception? current, object? instance, ref T result) {
        try {
            return Invoke(slot, current, instance, ref result);
        } catch (Exception) {
            return current;
        }
    }

    private static Exception? RunVoidSwallowingThrows(int slot, Exception? current, object? instance) {
        try {
            return InvokeVoid(slot, current, instance);
        } catch (Exception) {
            return current;
        }
    }

    private static int Register(HarmonyFinalizerShape shape, Type returnType) {
        (MethodInfo, Type) key = (shape.Method, returnType);
        lock (Gate) {
            if (Slots.TryGetValue(key, out int existing)) {
                return existing;
            }

            Delegate adapter = shape.BuildAdapter(returnType);
            Delegate[] grown = new Delegate[adapters.Length + 1];
            Array.Copy(adapters, grown, adapters.Length);
            grown[adapters.Length] = adapter;
            adapters = grown;
            Slots[key] = grown.Length - 1;
            return grown.Length - 1;
        }
    }

    private static MethodInfo Shim(string generic, string plain, Type returnType, Type? receiver = null) {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        Type host = receiver == null ? typeof(FinalizerParticipant) : typeof(OnInstance<>).MakeGenericType(receiver);
        if (returnType == typeof(void)) {
            return host.GetMethod(plain, flags)!;
        }

        return host.GetMethod(generic, flags)!.MakeGenericMethod(returnType);
    }

    internal struct Pass {
        internal byte Phase;

        internal Exception? Current;
    }

    internal abstract class OnInstance<TInstance>
        where TInstance : class {
        [InjectInstance]
        private TInstance Self {
            get { return null!; }
        }

        internal static void ReturnTerminal<T>(ControlHandle<T> control) {
            Pass pass = control.GetState<Pass>();
            if (pass.Phase != Returning || pass.Current == null) {
                return;
            }

            pass.Phase = Finalized;
            control.SetState(pass);
            throw pass.Current;
        }

        internal static void ReturnTerminalVoid(ControlHandle control) {
            Pass pass = control.GetState<Pass>();
            if (pass.Phase != Returning || pass.Current == null) {
                return;
            }

            pass.Phase = Finalized;
            control.SetState(pass);
            throw pass.Current;
        }

        internal static void CatchTerminal<T>(ControlHandle<T> control) {
            Pass pass = control.GetState<Pass>();
            if (pass.Phase == Finalized) {
                control.Rethrow();
                return;
            }

            Exception? current = pass.Phase == Catching ? pass.Current : control.Exception;
            if (current == null) {
                control.Cancel();
                return;
            }

            if (ReferenceEquals(current, control.Exception)) {
                control.Rethrow();
                return;
            }

            throw current;
        }

        internal static void CatchTerminalVoid(ControlHandle control) {
            Pass pass = control.GetState<Pass>();
            if (pass.Phase == Finalized) {
                control.Rethrow();
                return;
            }

            Exception? current = pass.Phase == Catching ? pass.Current : control.Exception;
            if (current == null) {
                control.Cancel();
                return;
            }

            if (ReferenceEquals(current, control.Exception)) {
                control.Rethrow();
                return;
            }

            throw current;
        }

        internal void OnReturn<T>(ControlHandle<T> control, [Bound] int slot) {
            Pass pass = control.GetState<Pass>();
            T result = control.ReturnValue;
            pass = ReturnPass(pass, slot, Self, ref result);
            control.ReturnValue = result;
            control.SetState(pass);
        }

        internal void OnReturnVoid(ControlHandle control, [Bound] int slot) {
            Pass pass = control.GetState<Pass>();
            pass = ReturnPassVoid(pass, slot, Self);
            control.SetState(pass);
        }

        internal void OnThrow<T>(ControlHandle<T> control, [Bound] int slot) {
            Pass pass = control.GetState<Pass>();
            if (pass.Phase == Finalized) {
                return;
            }

            Exception caught = control.Exception;
            T result = control.ReturnValue;
            pass = ThrowPass(pass, slot, Self, caught, ref result);
            control.ReturnValue = result;
            control.SetState(pass);
        }

        internal void OnThrowVoid(ControlHandle control, [Bound] int slot) {
            Pass pass = control.GetState<Pass>();
            if (pass.Phase == Finalized) {
                return;
            }

            Exception caught = control.Exception;
            pass = ThrowPassVoid(pass, slot, Self, caught);
            control.SetState(pass);
        }
    }
}
