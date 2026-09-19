using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class BridgeLocalTarget
    {
        // One int local in Debug and in Release: ToString() takes its address so Release keeps it in a
        // slot, and the string return type keeps a Debug return temp out of the int count. A plain
        // [Local] int binds it until someone else's transpiler declares a second one.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string OneInt(int seed)
        {
            int doubled = seed * 2;
            return doubled.ToString();
        }
    }

    public static class BridgeLocalMods
    {
        public static List<string> Entries = new List<string>();

        public static void ReadInt([Local] int value)
        {
            Entries.Add("local:" + value);
        }

        public static void Healthy()
        {
            Entries.Add("healthy");
        }

        // Mod C's honest transpiler: it declares an int local of its own and uses it. That is all it
        // takes to make mod A's [Local] int ambiguous, through no fault of C's.
        public static IEnumerable<CodeInstruction> AddScratchInt(
            IEnumerable<CodeInstruction> instructions, ITranspilerContext context)
        {
            LocalRef scratch = context.DeclareLocal(typeof(int));
            List<CodeInstruction> output = new List<CodeInstruction>(instructions);
            output.Insert(0, new CodeInstruction(OpCodes.Ldc_I4, 42));
            output.Insert(1, new CodeInstruction(OpCodes.Stloc_S, scratch));
            output.Insert(2, new CodeInstruction(OpCodes.Ldloc_S, scratch));
            output.Insert(3, new CodeInstruction(OpCodes.Pop));
            return output;
        }
    }

    public sealed class LocalEvictionBridgeTests
    {
        // Before eviction, one unresolvable [Local] made TranspilerParticipant hand the stream back
        // unchanged, which silently disabled every Concord injection on the target for every mod.
        [Fact]
        public void ABrokenLocal_EvictsOnlyItself_AndTheBridgeLogsWhy()
        {
            MethodInfo target = typeof(BridgeLocalTarget).GetMethod(nameof(BridgeLocalTarget.OneInt));
            MethodInfo broken = typeof(BridgeLocalMods).GetMethod(nameof(BridgeLocalMods.ReadInt));
            MethodInfo healthy = typeof(BridgeLocalMods).GetMethod(nameof(BridgeLocalMods.Healthy));
            MethodInfo transpiler = typeof(BridgeLocalMods).GetMethod(nameof(BridgeLocalMods.AddScratchInt));

            List<string> log = new List<string>();
            HarmonyBridge bridge = new HarmonyBridge(log.Add);
            ForeignRouteResult result = null;
            try
            {
                result = bridge.TryRoute(
                    target,
                    new[]
                    {
                        new Injection(broken, new InjectAt.Return(), "mod-a", 0),
                        new Injection(healthy, new InjectAt.Return(), "mod-b", 0),
                        new Injection(transpiler, new InjectAt.Transpiler(false), "mod-c", 0),
                    },
                    true);

                Assert.Equal(ForeignRouteKind.Routed, result.Kind);

                BridgeLocalMods.Entries.Clear();
                Assert.Equal("10", BridgeLocalTarget.OneInt(5));
                Assert.Equal(new List<string> { "healthy" }, BridgeLocalMods.Entries);

                string evicted = Assert.Single(log, entry => entry.StartsWith(CoexistenceLogMarkers.InjectionEvicted));
                Assert.Contains("mod-a", evicted);
                Assert.Contains("CONC147", evicted);
                Assert.Contains("(target body)", evicted);
                Assert.Contains("(transpiler-added)", evicted);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }
    }
}
