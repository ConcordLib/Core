using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class StateMachineLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class StateMachineTarget
    {
        public static IEnumerable<int> Iterate()
        {
            StateMachineLog.Entries.Add("body1");
            yield return 1;
            StateMachineLog.Entries.Add("body2");
            yield return 2;
        }

        public static async Task<int> AsyncTarget()
        {
            await Task.Yield();
            return 7;
        }

        public static MethodInfo ResolveIterateMoveNext()
        {
            foreach (Type nested in typeof(StateMachineTarget).GetNestedTypes(BindingFlags.NonPublic))
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

    public static class StateMachineMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            StateMachineLog.Entries.Add("head");
        }

        public static void DeclaredPostfix()
        {
            StateMachineLog.Entries.Add("declaredPostfix");
        }

        public static void MoveNextPostfix()
        {
            StateMachineLog.Entries.Add("moveNextPostfix");
        }
    }

    [Collection("HarmonySerial")]
    public sealed class StateMachineCoexistenceTests
    {
        private static Injection MakeHeadInjection(MethodInfo method, string owner, PatchBody body)
        {
            return new Injection(method, new InjectAt.Head(), owner, 0) { Body = body };
        }

        // The shape the real pipeline hands the bridge: CollectingPatchApplier has already resolved the
        // declared method to MoveNext, so Body stays StateMachine but the target is already MoveNext.
        [Fact]
        public void Validate_StateMachineBodyOnMoveNextTarget_IsAccepted()
        {
            MethodInfo moveNext = StateMachineTarget.ResolveIterateMoveNext();
            MethodInfo headMethod = typeof(StateMachineMods).GetMethod(nameof(StateMachineMods.ConcordHead));

            string reason = SupportMatrix.Validate(moveNext, new[] { MakeHeadInjection(headMethod, "test.sm", PatchBody.StateMachine) }, null);

            Assert.Null(reason);
        }

        // Sub-case 1: Harmony owns the DECLARED iterator method. MoveNext is uncontested, so Concord's
        // StateMachine injection routes raw and both run.
        [Fact]
        public void HarmonyOnDeclaredIterator_ConcordOnMoveNext_IsNotContested()
        {
            MethodInfo declared = typeof(StateMachineTarget).GetMethod(nameof(StateMachineTarget.Iterate));
            MethodInfo moveNext = StateMachineTarget.ResolveIterateMoveNext();
            MethodInfo headMethod = typeof(StateMachineMods).GetMethod(nameof(StateMachineMods.ConcordHead));
            MethodInfo postfix = typeof(StateMachineMods).GetMethod(nameof(StateMachineMods.DeclaredPostfix));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.sm.declared");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            try
            {
                harmony.Patch(declared, postfix: new HarmonyMethod(postfix));

                ForeignRouteResult result = bridge.TryRoute(moveNext, new[] { MakeHeadInjection(headMethod, "test.sm", PatchBody.StateMachine) }, false);

                Assert.Equal(ForeignRouteKind.NotContested, result.Kind);
            }
            finally
            {
                TranspilerParticipant.Registry.Clear(moveNext);
                TestUnpatch.Own(harmony, "test.sm.declared");
            }
        }

        // Sub-case 2: Harmony owns MoveNext (AccessTools.EnumeratorMoveNext style). Concord's StateMachine
        // injection targets the same MoveNext, so it is contested and routes through the bridge.
        [Fact]
        public void HarmonyOnMoveNext_ConcordStateMachineOnMoveNext_RoutesAndBothRun()
        {
            MethodInfo moveNext = StateMachineTarget.ResolveIterateMoveNext();
            MethodInfo headMethod = typeof(StateMachineMods).GetMethod(nameof(StateMachineMods.ConcordHead));
            MethodInfo postfix = typeof(StateMachineMods).GetMethod(nameof(StateMachineMods.MoveNextPostfix));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.sm.movenext");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                harmony.Patch(moveNext, postfix: new HarmonyMethod(postfix));

                result = bridge.TryRoute(moveNext, new[] { MakeHeadInjection(headMethod, "test.sm", PatchBody.StateMachine) }, false);

                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                StateMachineLog.Entries.Clear();
                List<int> values = new List<int>();
                foreach (int value in StateMachineTarget.Iterate())
                {
                    values.Add(value);
                }

                Assert.Equal(new List<int> { 1, 2 }, values);
                Assert.Contains("head", StateMachineLog.Entries);
                Assert.Contains("moveNextPostfix", StateMachineLog.Entries);
                Assert.Contains("body1", StateMachineLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TranspilerParticipant.Registry.Clear(moveNext);
                TestUnpatch.Own(harmony, "test.sm.movenext");
            }
        }

        // Sub-case 3: Harmony owns MoveNext, Concord targets the declared method with a Declared body.
        // Different methods, so no contest is reported and none exists.
        [Fact]
        public void HarmonyOnMoveNext_ConcordDeclaredOnDeclaredMethod_IsNotContested()
        {
            MethodInfo declared = typeof(StateMachineTarget).GetMethod(nameof(StateMachineTarget.Iterate));
            MethodInfo moveNext = StateMachineTarget.ResolveIterateMoveNext();
            MethodInfo headMethod = typeof(StateMachineMods).GetMethod(nameof(StateMachineMods.ConcordHead));
            MethodInfo postfix = typeof(StateMachineMods).GetMethod(nameof(StateMachineMods.MoveNextPostfix));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.sm.split");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            try
            {
                harmony.Patch(moveNext, postfix: new HarmonyMethod(postfix));

                ForeignRouteResult result = bridge.TryRoute(declared, new[] { MakeHeadInjection(headMethod, "test.sm", PatchBody.Declared) }, false);

                Assert.Equal(ForeignRouteKind.NotContested, result.Kind);
            }
            finally
            {
                TranspilerParticipant.Registry.Clear(declared);
                TestUnpatch.Own(harmony, "test.sm.split");
            }
        }
    }
}
