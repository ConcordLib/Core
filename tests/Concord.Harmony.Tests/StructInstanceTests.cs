using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class StructInstanceLog
    {
        public static List<string> Entries = new List<string>();
    }

    public struct StructInstanceTarget
    {
        public int Seed;

        public StructInstanceTarget(int seed)
        {
            Seed = seed;
            StructInstanceLog.Entries.Add("body:" + seed);
        }
    }

    public static class StructInstanceMods
    {
        public static void ReadsInstance(StructInstanceTarget __instance)
        {
            StructInstanceLog.Entries.Add("post:" + __instance.Seed);
        }

        public static void TakesInstanceByRef(ref StructInstanceTarget __instance)
        {
            __instance.Seed = 0;
        }
    }

    [Collection("HarmonySerial")]
    public sealed class StructInstanceTests
    {
        [Fact]
        public void PostfixReadingAStructInstance_MatchesHarmony()
        {
            if (TestRuntime.IsNetFramework)
            {
                return;
            }

            Assert.Equal(ViaHarmony(), ViaConcord());
        }

        [Fact]
        public void PostfixTakingAStructInstanceByRef_IsRefused()
        {
            MethodInfo patch = typeof(StructInstanceMods).GetMethod(nameof(StructInstanceMods.TakesInstanceByRef))!;

            string? reason = PostfixMapping.Validate(Target(), patch);

            Assert.NotNull(reason);
            Assert.Contains("takes __instance by reference", reason);
        }

        private static MethodBase Target()
        {
            return typeof(StructInstanceTarget).GetConstructor(new[] { typeof(int) })!;
        }

        private static string ViaHarmony()
        {
            const string harmonyId = "test.structinstance.harmony";
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            try
            {
                harmony.Patch(
                    Target(),
                    postfix: new HarmonyMethod(typeof(StructInstanceMods).GetMethod(nameof(StructInstanceMods.ReadsInstance))!));
                return Record();
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }
        }

        private static string ViaConcord()
        {
            MethodBase target = Target();
            const string harmonyId = "test.structinstance.concord";
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            TranspilerParticipant.LastStreamFailure = null;

            try
            {
                Injection[] mapped = PostfixMapping
                    .Map(target, typeof(StructInstanceMods).GetMethod(nameof(StructInstanceMods.ReadsInstance))!, "test.concord.structinstance", 400)
                    .ToArray();

                Assert.NotEmpty(mapped);
                TranspilerParticipant.Registry.Add(target, mapped);
                harmony.Patch(target, transpiler: new HarmonyMethod(TranspilerParticipant.TranspileMethod) { priority = Priority.Last });

                Assert.Null(TranspilerParticipant.LastStreamFailure);
                return Record();
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        private static string Record()
        {
            StructInstanceLog.Entries.Clear();
            StructInstanceTarget made = new StructInstanceTarget(6);
            return made.Seed + "|" + string.Join(",", StructInstanceLog.Entries);
        }
    }
}
