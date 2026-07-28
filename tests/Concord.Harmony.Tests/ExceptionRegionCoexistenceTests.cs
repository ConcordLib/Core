using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class TryCatchLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class TryCatchTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(bool shouldThrow)
        {
            try
            {
                TryCatchLog.Entries.Add("try");
                if (shouldThrow)
                {
                    throw new InvalidOperationException("boom");
                }

                return 1;
            }
            catch (InvalidOperationException)
            {
                TryCatchLog.Entries.Add("catch");
                return 2;
            }
        }
    }

    public static class TryCatchMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            TryCatchLog.Entries.Add("head");
        }
    }

    public static class TryFinallyLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class TryFinallyTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(bool shouldThrow)
        {
            try
            {
                TryFinallyLog.Entries.Add("try");
                if (shouldThrow)
                {
                    throw new InvalidOperationException("boom");
                }

                return 1;
            }
            finally
            {
                TryFinallyLog.Entries.Add("finally");
            }
        }
    }

    public static class TryFinallyMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            TryFinallyLog.Entries.Add("head");
        }
    }

    public static class TryCatchFinallyLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class TryCatchFinallyTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(int mode)
        {
            try
            {
                TryCatchFinallyLog.Entries.Add("try");
                if (mode == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                if (mode == 2)
                {
                    throw new ArgumentException("uncaught");
                }

                return 1;
            }
            catch (InvalidOperationException)
            {
                TryCatchFinallyLog.Entries.Add("catch");
                return 2;
            }
            finally
            {
                TryCatchFinallyLog.Entries.Add("finally");
            }
        }
    }

    public static class TryCatchFinallyMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            TryCatchFinallyLog.Entries.Add("head");
        }
    }

    public static class MultiCatchLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class MultiCatchTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(int mode)
        {
            try
            {
                MultiCatchLog.Entries.Add("try");
                if (mode == 1)
                {
                    throw new ArgumentException("a");
                }

                if (mode == 2)
                {
                    throw new InvalidOperationException("b");
                }

                return 0;
            }
            catch (ArgumentException)
            {
                MultiCatchLog.Entries.Add("catchA");
                return 1;
            }
            catch (InvalidOperationException)
            {
                MultiCatchLog.Entries.Add("catchB");
                return 2;
            }
        }
    }

    public static class MultiCatchMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            MultiCatchLog.Entries.Add("head");
        }
    }

    public static class NestedTryInCatchLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class NestedTryInCatchTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(bool outerThrows, bool innerThrows)
        {
            try
            {
                NestedTryInCatchLog.Entries.Add("outerTry");
                if (outerThrows)
                {
                    throw new InvalidOperationException("outer");
                }

                return 1;
            }
            catch (InvalidOperationException)
            {
                NestedTryInCatchLog.Entries.Add("outerCatch");
                try
                {
                    NestedTryInCatchLog.Entries.Add("innerTry");
                    if (innerThrows)
                    {
                        throw new ArgumentException("inner");
                    }

                    return 2;
                }
                catch (ArgumentException)
                {
                    NestedTryInCatchLog.Entries.Add("innerCatch");
                    return 3;
                }
            }
        }
    }

    public static class NestedTryInCatchMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            NestedTryInCatchLog.Entries.Add("head");
        }
    }

    public static class ExceptionFilterLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class ExceptionFilterTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(int code)
        {
            try
            {
                ExceptionFilterLog.Entries.Add("try");
                if (code != 0)
                {
                    throw new InvalidOperationException(code.ToString());
                }

                return 0;
            }
            catch (InvalidOperationException ex) when (ex.Message == "1")
            {
                ExceptionFilterLog.Entries.Add("filterMatch");
                return 1;
            }
        }
    }

    public static class ExceptionFilterMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            ExceptionFilterLog.Entries.Add("head");
        }
    }

    public static class IteratorFaultLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class IteratorFaultTarget
    {
        public static IEnumerable<int> Iterate()
        {
            try
            {
                IteratorFaultLog.Entries.Add("before1");
                yield return 1;
                IteratorFaultLog.Entries.Add("before2");
                yield return 2;
            }
            finally
            {
                IteratorFaultLog.Entries.Add("finally");
            }
        }

        public static MethodInfo ResolveMoveNext()
        {
            foreach (Type nested in typeof(IteratorFaultTarget).GetNestedTypes(BindingFlags.NonPublic))
            {
                if (nested.Name.Contains("Iterate"))
                {
                    MethodInfo moveNext = nested.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (moveNext != null)
                    {
                        return moveNext;
                    }
                }
            }

            throw new InvalidOperationException("iterator state machine MoveNext was not found");
        }
    }

    public static class IteratorFaultMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            IteratorFaultLog.Entries.Add("head");
        }
    }

    // Coverage gap this file closes: CoexistenceTests.cs targets are all single-statement returns
    // with no exception regions, so nothing anywhere exercised try/catch/finally survival through
    // Concord's neutral-IL composition against the real Harmony bridge. See P2 in the harmony-port
    // spec: this is the behavioural baseline the neutral-IL-model rewrite (P3-P9) is measured against.
    [Collection("HarmonySerial")]
    public sealed class ExceptionRegionCoexistenceTests
    {
        private static Injection MakeHeadInjection(MethodInfo method, string owner)
        {
            return new Injection(method, new InjectAt.Head(), owner, 0);
        }

        [Fact]
        public void TryCatch_ConcordHeadInjection_ThrowingAndNonThrowingPathsBothPreserved()
        {
            MethodInfo target = typeof(TryCatchTarget).GetMethod(nameof(TryCatchTarget.Run));
            MethodInfo headMethod = typeof(TryCatchMods).GetMethod(nameof(TryCatchMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.trycatch") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                TryCatchLog.Entries.Clear();
                int nonThrowing = TryCatchTarget.Run(false);
                Assert.Equal(1, nonThrowing);
                Assert.Equal(new List<string> { "head", "try" }, TryCatchLog.Entries);

                TryCatchLog.Entries.Clear();
                int throwing = TryCatchTarget.Run(true);
                Assert.Equal(2, throwing);
                Assert.Equal(new List<string> { "head", "try", "catch" }, TryCatchLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void TryFinally_ConcordHeadInjection_FinallyRunsOnBothNormalReturnAndPropagatedException()
        {
            MethodInfo target = typeof(TryFinallyTarget).GetMethod(nameof(TryFinallyTarget.Run));
            MethodInfo headMethod = typeof(TryFinallyMods).GetMethod(nameof(TryFinallyMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.tryfinally") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                TryFinallyLog.Entries.Clear();
                int nonThrowing = TryFinallyTarget.Run(false);
                Assert.Equal(1, nonThrowing);
                Assert.Equal(new List<string> { "head", "try", "finally" }, TryFinallyLog.Entries);

                TryFinallyLog.Entries.Clear();
                InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => TryFinallyTarget.Run(true));
                Assert.Equal("boom", thrown.Message);
                Assert.Equal(new List<string> { "head", "try", "finally" }, TryFinallyLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void TryCatchFinally_ConcordHeadInjection_CaughtUncaughtAndNonThrowingPathsAllRunFinally()
        {
            MethodInfo target = typeof(TryCatchFinallyTarget).GetMethod(nameof(TryCatchFinallyTarget.Run));
            MethodInfo headMethod = typeof(TryCatchFinallyMods).GetMethod(nameof(TryCatchFinallyMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.trycatchfinally") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                TryCatchFinallyLog.Entries.Clear();
                int nonThrowing = TryCatchFinallyTarget.Run(0);
                Assert.Equal(1, nonThrowing);
                Assert.Equal(new List<string> { "head", "try", "finally" }, TryCatchFinallyLog.Entries);

                TryCatchFinallyLog.Entries.Clear();
                int caught = TryCatchFinallyTarget.Run(1);
                Assert.Equal(2, caught);
                Assert.Equal(new List<string> { "head", "try", "catch", "finally" }, TryCatchFinallyLog.Entries);

                TryCatchFinallyLog.Entries.Clear();
                Assert.Throws<ArgumentException>(() => TryCatchFinallyTarget.Run(2));
                Assert.Equal(new List<string> { "head", "try", "finally" }, TryCatchFinallyLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void MultiCatch_ConcordHeadInjection_EachCatchClauseAndNonThrowingPathAllPreserved()
        {
            MethodInfo target = typeof(MultiCatchTarget).GetMethod(nameof(MultiCatchTarget.Run));
            MethodInfo headMethod = typeof(MultiCatchMods).GetMethod(nameof(MultiCatchMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.multicatch") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                MultiCatchLog.Entries.Clear();
                int nonThrowing = MultiCatchTarget.Run(0);
                Assert.Equal(0, nonThrowing);
                Assert.Equal(new List<string> { "head", "try" }, MultiCatchLog.Entries);

                MultiCatchLog.Entries.Clear();
                int caughtByA = MultiCatchTarget.Run(1);
                Assert.Equal(1, caughtByA);
                Assert.Equal(new List<string> { "head", "try", "catchA" }, MultiCatchLog.Entries);

                MultiCatchLog.Entries.Clear();
                int caughtByB = MultiCatchTarget.Run(2);
                Assert.Equal(2, caughtByB);
                Assert.Equal(new List<string> { "head", "try", "catchB" }, MultiCatchLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void NestedTryInsideCatch_ConcordHeadInjection_OuterAndInnerThrowingAndNonThrowingPathsAllPreserved()
        {
            MethodInfo target = typeof(NestedTryInCatchTarget).GetMethod(nameof(NestedTryInCatchTarget.Run));
            MethodInfo headMethod = typeof(NestedTryInCatchMods).GetMethod(nameof(NestedTryInCatchMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.nestedtry") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                NestedTryInCatchLog.Entries.Clear();
                int outerNonThrowing = NestedTryInCatchTarget.Run(false, false);
                Assert.Equal(1, outerNonThrowing);
                Assert.Equal(new List<string> { "head", "outerTry" }, NestedTryInCatchLog.Entries);

                NestedTryInCatchLog.Entries.Clear();
                int innerNonThrowing = NestedTryInCatchTarget.Run(true, false);
                Assert.Equal(2, innerNonThrowing);
                Assert.Equal(new List<string> { "head", "outerTry", "outerCatch", "innerTry" }, NestedTryInCatchLog.Entries);

                NestedTryInCatchLog.Entries.Clear();
                int innerThrowing = NestedTryInCatchTarget.Run(true, true);
                Assert.Equal(3, innerThrowing);
                Assert.Equal(new List<string> { "head", "outerTry", "outerCatch", "innerTry", "innerCatch" }, NestedTryInCatchLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact(Skip = "Confirmed a Harmony 2.4.2 bug, not a Concord bridge limitation: HarmonyLib.MethodBodyReader.ParseExceptions never emits a BeginCatchBlock marker at a filter clause's own HandlerOffset (only BeginExceptFilterBlock at FilterOffset), so no transpiler - including a completely pass-through one - can round-trip a 'when' filter through Harmony's own CodeInstruction model. Reproduced standalone: Harmony.Patch(target, transpiler: <identity transpiler returning instructions unchanged>) on a method with a when-clause throws the identical 'Incorrect code generation for exception block' from RuntimeILGenerator.EndExceptionBlock, with zero Concord code involved. Concord's own model (CecilCodeConverter) correctly requires and represents the missing HandlerStart marker when reading a filter clause from a real Cecil body; the gap is entirely upstream, in what Harmony hands transpilers. Revisit only if a future Harmony version fixes ParseExceptions's Filter case, or if the bridge independently re-parses original.GetMethodBody().ExceptionHandlingClauses to recover HandlerOffset (out of scope here).")]
        public void ExceptionFilter_ConcordHeadInjection_FilterMatchNonMatchAndNonThrowingPathsAllPreserved()
        {
            MethodInfo target = typeof(ExceptionFilterTarget).GetMethod(nameof(ExceptionFilterTarget.Run));
            MethodInfo headMethod = typeof(ExceptionFilterMods).GetMethod(nameof(ExceptionFilterMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.filter") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                ExceptionFilterLog.Entries.Clear();
                int nonThrowing = ExceptionFilterTarget.Run(0);
                Assert.Equal(0, nonThrowing);
                Assert.Equal(new List<string> { "head", "try" }, ExceptionFilterLog.Entries);

                ExceptionFilterLog.Entries.Clear();
                int filterMatched = ExceptionFilterTarget.Run(1);
                Assert.Equal(1, filterMatched);
                Assert.Equal(new List<string> { "head", "try", "filterMatch" }, ExceptionFilterLog.Entries);

                ExceptionFilterLog.Entries.Clear();
                Assert.Throws<InvalidOperationException>(() => ExceptionFilterTarget.Run(2));
                Assert.Equal(new List<string> { "head", "try" }, ExceptionFilterLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void IteratorFault_ConcordHeadInjection_FullEnumerationAndEarlyDisposeBothRunFinally()
        {
            MethodInfo target = IteratorFaultTarget.ResolveMoveNext();
            MethodInfo headMethod = typeof(IteratorFaultMods).GetMethod(nameof(IteratorFaultMods.ConcordHead));
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(target, new[] { MakeHeadInjection(headMethod, "test.fault") }, true);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                IteratorFaultLog.Entries.Clear();
                List<int> values = new List<int>();
                foreach (int value in IteratorFaultTarget.Iterate())
                {
                    values.Add(value);
                }

                Assert.Equal(new List<int> { 1, 2 }, values);
                Assert.Contains("finally", IteratorFaultLog.Entries);

                IteratorFaultLog.Entries.Clear();
                using (IEnumerator<int> enumerator = IteratorFaultTarget.Iterate().GetEnumerator())
                {
                    Assert.True(enumerator.MoveNext());
                    Assert.Equal(1, enumerator.Current);
                }

                Assert.Contains("finally", IteratorFaultLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }
    }
}
