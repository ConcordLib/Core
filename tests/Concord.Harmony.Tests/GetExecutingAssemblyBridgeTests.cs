using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class BridgeGetExecTarget
    {
        public static Assembly Observed;

        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Contested()
        {
            return 7;
        }
    }

    public static class BridgeGetExecMods
    {
        public static void Postfix()
        {
            BridgeGetExecTarget.Entries.Add("postfix");
        }

        public static void ConcordHead(ControlHandle ch)
        {
            BridgeGetExecTarget.Entries.Add("head");
            BridgeGetExecTarget.Observed = Assembly.GetExecutingAssembly();
        }
    }

    [Collection("HarmonySerial")]
    public sealed class GetExecutingAssemblyBridgeTests
    {
        [Fact]
        public void RoutedInjection_CallingGetExecutingAssembly_ObservesInjectionAssembly()
        {
            MethodInfo target = typeof(BridgeGetExecTarget).GetMethod(nameof(BridgeGetExecTarget.Contested));
            MethodInfo headMethod = typeof(BridgeGetExecMods).GetMethod(nameof(BridgeGetExecMods.ConcordHead));
            MethodInfo postfix = typeof(BridgeGetExecMods).GetMethod(nameof(BridgeGetExecMods.Postfix));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.getexec");
            HarmonyBridge bridge = new HarmonyBridge(_ => { });

            ForeignRouteResult result = null;
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));

                Injection head = new Injection(headMethod, new InjectAt.Head(), "test.concord", 0);
                result = bridge.TryRoute(target, new[] { head }, false);
                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                BridgeGetExecTarget.Observed = null;
                BridgeGetExecTarget.Entries.Clear();
                Assert.Equal(7, BridgeGetExecTarget.Contested());

                Assert.Equal(new List<string> { "head", "postfix" }, BridgeGetExecTarget.Entries);
                Assert.Same(typeof(BridgeGetExecMods).Assembly, BridgeGetExecTarget.Observed);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmony, "test.getexec");
            }
        }
    }

    public static class BridgeGetExecTranspilers
    {
        public static IEnumerable<CodeInstruction> EmitGetExecutingAssembly(IEnumerable<CodeInstruction> instructions)
        {
            yield return new CodeInstruction(OpCodes.Call, typeof(Assembly).GetMethod(nameof(Assembly.GetExecutingAssembly)));
            yield return new CodeInstruction(OpCodes.Pop);
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
            }
        }
    }

    [Collection("HarmonySerial")]
    public sealed class GetExecutingAssemblyTranspilerGuardBridgeTests
    {
        [Fact]
        public void StreamTranspilerEmittingGetExecutingAssembly_RejectsStreamWithConc143()
        {
            List<string> logs = new List<string>();
            TranspilerParticipant.Log = msg => logs.Add(msg);
            TranspilerParticipant.LastStreamFailure = null;

            MethodBase target = typeof(BridgeGetExecTarget).GetMethod(nameof(BridgeGetExecTarget.Contested));
            MethodBase transpiler = typeof(BridgeGetExecTranspilers).GetMethod(nameof(BridgeGetExecTranspilers.EmitGetExecutingAssembly));
            TranspilerParticipant.Registry.Add(target, new Injection[]
            {
                new Injection(transpiler, new InjectAt.Transpiler(), "test.concord", 0)
            });

            ILGenerator gen = new DynamicMethod("t", typeof(int), System.Type.EmptyTypes).GetILGenerator();
            List<HarmonyLib.CodeInstruction> instructions = new List<HarmonyLib.CodeInstruction>
            {
                new HarmonyLib.CodeInstruction(OpCodes.Ldc_I4_7),
                new HarmonyLib.CodeInstruction(OpCodes.Ret)
            };

            try
            {
                List<HarmonyLib.CodeInstruction> result = new List<HarmonyLib.CodeInstruction>(TranspilerParticipant.Transpile(instructions, target, gen));

                Assert.Equal(2, result.Count);
                Assert.NotNull(TranspilerParticipant.LastStreamFailure);
                Assert.Contains("CONC143", TranspilerParticipant.LastStreamFailure.ToString());
                Assert.Contains(CoexistenceLogMarkers.StreamRejected, logs[0]);
            }
            finally
            {
                TranspilerParticipant.Log = null;
                TranspilerParticipant.LastStreamFailure = null;
                TranspilerParticipant.Registry.Clear(target);
            }
        }
    }
}
