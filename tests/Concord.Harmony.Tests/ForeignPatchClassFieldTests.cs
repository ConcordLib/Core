using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
#pragma warning disable SA1306
    public sealed class ForeignFieldHolder
    {
        private int BaseTicksBetweenPickHits = 100;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Ticks(int amount)
        {
            return BaseTicksBetweenPickHits + amount;
        }
    }

#pragma warning disable CS0414
    public class ForeignCollidingPatch
    {
        public static List<string> Entries = new List<string>();

        private const float BaseTicksBetweenPickHits = 100f;

        private int OriginalCare = 3;

        public int Care { get; set; }

        public static void Prefix(int amount)
        {
            Entries.Add("prefix:" + BaseTicksBetweenPickHits + ":" + amount);
        }
    }
#pragma warning restore CS0414, SA1306

    [Collection("HarmonySerial")]
    public sealed class ForeignPatchClassFieldTests
    {
        [Fact]
        public void ForeignPatchClassOwnFields_AreNotReadAsConcordDeclarations()
        {
            MethodInfo target = typeof(ForeignFieldHolder).GetMethod(nameof(ForeignFieldHolder.Ticks))!;
            MethodInfo prefix = typeof(ForeignCollidingPatch).GetMethod(nameof(ForeignCollidingPatch.Prefix))!;

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>
            {
                (0, PrefixMapping.Map(target, prefix, "test.foreignfields")),
            };

            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            ComposeResult composed = WrapperComposer.Compose(target, ordered);
            Assert.Empty(composed.Rejected);

            ForeignCollidingPatch.Entries.Clear();
            Func<ForeignFieldHolder, int, int> wrapper =
                (Func<ForeignFieldHolder, int, int>)composed.Wrapper.CreateDelegate(typeof(Func<ForeignFieldHolder, int, int>));

            Assert.Equal(107, wrapper(new ForeignFieldHolder(), 7));
            Assert.Equal(new[] { "prefix:100:7" }, ForeignCollidingPatch.Entries);
        }
    }
}
