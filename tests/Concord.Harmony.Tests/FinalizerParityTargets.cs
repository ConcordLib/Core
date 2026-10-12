using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Concord;

namespace Concord.Harmony.Tests
{
    public static class FinalizerParityLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class FinalizerParityTargets
    {
        private static readonly int[] Cell = new int[1];

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Body(int x)
        {
            FinalizerParityLog.Entries.Add("body");
            if (x < 0)
            {
                throw new InvalidOperationException("boom");
            }

            return x * 2;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Propagate(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Swallow(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Replace(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Normal(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TwoOwners(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int WithPostfix(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ResultWithoutSwallow(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Chain(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ManufactureOnNormalReturn(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int TwoManufactureOnNormalReturn(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ThrowingFinalizer(int x)
        {
            return Body(x);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ref int ByRefReturn(int x)
        {
            Cell[0] = x;
            return ref Cell[0];
        }
    }

    public static class FinalizerParityLogic
    {
        public static string Describe(Exception? error)
        {
            return error == null ? "null" : error.GetType().Name + ":" + error.Message;
        }

        public static Exception? Propagate(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("fin:" + Describe(error));
            return error;
        }

        public static Exception? Swallow(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("fin:" + Describe(error));
            return null;
        }

        public static Exception? Replace(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("fin:" + Describe(error));
            result = 99;
            return null;
        }

        public static Exception? OwnerA(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("finA:" + Describe(error));
            return error;
        }

        public static Exception? OwnerB(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("finB:" + Describe(error));
            return error;
        }

        public static Exception? WriteResultKeepThrow(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("finA:" + Describe(error));
            result = 42;
            return error;
        }

        public static Exception? ReadResultKeepThrow(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("finB:result=" + result);
            return error;
        }

        public static Exception? ChainWrap(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("finA:" + Describe(error));
            return error == null ? null : new ArgumentException("wrapped");
        }

        public static Exception? ChainObserve(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("finB:" + Describe(error));
            return error;
        }

        public static Exception? ManufactureOnNormalReturn(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("fin:" + Describe(error));
            return error ?? new ArgumentException("manufactured");
        }

        public static Exception? ObserveAfterManufacture(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("finB:" + Describe(error));
            return error;
        }

        public static Exception? Boom(Exception? error, ref int result)
        {
            FinalizerParityLog.Entries.Add("fin:" + Describe(error));
            throw new NotSupportedException("fin-boom");
        }

        public static void Postfix(ref int result)
        {
            FinalizerParityLog.Entries.Add("postfix");
            result += 1;
        }
    }

    public static class FinalizerParityHarmonyMethods
    {
        public static Exception? Propagate(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.Propagate(__exception, ref __result);
        }

        public static Exception? Swallow(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.Swallow(__exception, ref __result);
        }

        public static Exception? Replace(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.Replace(__exception, ref __result);
        }

        public static Exception? OwnerA(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.OwnerA(__exception, ref __result);
        }

        public static Exception? OwnerB(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.OwnerB(__exception, ref __result);
        }

        public static Exception? WriteResultKeepThrow(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.WriteResultKeepThrow(__exception, ref __result);
        }

        public static Exception? ReadResultKeepThrow(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.ReadResultKeepThrow(__exception, ref __result);
        }

        public static Exception? ChainWrap(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.ChainWrap(__exception, ref __result);
        }

        public static Exception? ChainObserve(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.ChainObserve(__exception, ref __result);
        }

        public static Exception? ManufactureOnNormalReturn(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.ManufactureOnNormalReturn(__exception, ref __result);
        }

        public static Exception? ObserveAfterManufacture(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.ObserveAfterManufacture(__exception, ref __result);
        }

        public static Exception? Boom(Exception? __exception, ref int __result)
        {
            return FinalizerParityLogic.Boom(__exception, ref __result);
        }

        public static Exception? NoResult(Exception? __exception)
        {
            FinalizerParityLog.Entries.Add("fin:" + FinalizerParityLogic.Describe(__exception));
            return __exception;
        }

        public static void Postfix(ref int __result)
        {
            FinalizerParityLogic.Postfix(ref __result);
        }
    }

    public static class FinalizerParityConcordMethods
    {
        public static void Postfix(ControlHandle<int> control)
        {
            int result = control.ReturnValue;
            FinalizerParityLogic.Postfix(ref result);
            control.ReturnValue = result;
        }
    }
}
