using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Concord.Detour;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class LockScopeLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class LockScopeTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int First()
        {
            return 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Second()
        {
            return 2;
        }
    }

    public static class LockScopeMods
    {
        public static void PostfixFirst()
        {
            LockScopeLog.Entries.Add("postfixFirst");
        }

        public static void PostfixSecond()
        {
            LockScopeLog.Entries.Add("postfixSecond");
        }
    }

    [Collection("HarmonySerial")]
    public sealed class HarmonyLockScopeTests
    {
        private volatile bool competitorStarted;
        private volatile bool competitorDone;

        [Fact]
        public void Available_TrueAgainstVendoredHarmony()
        {
            Assert.True(HarmonyLockScope.Available);
        }

        [Fact]
        public void HoldingScope_PatchAndGetPatchInfoSucceed_CompetingThreadOnAnotherTargetBlocksUntilRelease()
        {
            MethodInfo first = typeof(LockScopeTargets).GetMethod(nameof(LockScopeTargets.First));
            MethodInfo second = typeof(LockScopeTargets).GetMethod(nameof(LockScopeTargets.Second));
            MethodInfo postfixFirst = typeof(LockScopeMods).GetMethod(nameof(LockScopeMods.PostfixFirst));
            MethodInfo postfixSecond = typeof(LockScopeMods).GetMethod(nameof(LockScopeMods.PostfixSecond));

            HarmonyLib.Harmony harmonyMain = new HarmonyLib.Harmony("test.lockscope.main");
            HarmonyLib.Harmony harmonyCompetitor = new HarmonyLib.Harmony("test.lockscope.competitor");

            Thread competitor = null;

            try
            {
                using (HarmonyLockScope.Enter())
                {
                    Patches existing = PatchProcessor.GetPatchInfo(first);
                    Assert.Null(existing);

                    harmonyMain.Patch(first, postfix: new HarmonyMethod(postfixFirst));

                    Patches afterPatch = PatchProcessor.GetPatchInfo(first);
                    Assert.NotNull(afterPatch);
                    Assert.Single(afterPatch.Postfixes);

                    LockScopeLog.Entries.Clear();
                    Assert.Equal(1, LockScopeTargets.First());
                    Assert.Equal(new List<string> { "postfixFirst" }, LockScopeLog.Entries);

                    competitor = new Thread(() =>
                    {
                        competitorStarted = true;
                        harmonyCompetitor.Patch(second, postfix: new HarmonyMethod(postfixSecond));
                        competitorDone = true;
                    });
                    competitor.Start();

                    SpinWait.SpinUntil(() => competitorStarted, 5000);
                    Assert.True(competitorStarted);
                    Thread.Sleep(500);
                    Assert.False(competitorDone);
                }

                Assert.True(competitor.Join(10000));
                Assert.True(competitorDone);

                LockScopeLog.Entries.Clear();
                Assert.Equal(2, LockScopeTargets.Second());
                Assert.Equal(new List<string> { "postfixSecond" }, LockScopeLog.Entries);
            }
            finally
            {
                competitor?.Join(1000);
                harmonyMain.UnpatchAll("test.lockscope.main");
                harmonyCompetitor.UnpatchAll("test.lockscope.competitor");
            }
        }
    }
}
