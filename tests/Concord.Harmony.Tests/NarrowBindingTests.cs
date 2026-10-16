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
    public class NarrowBase
    {
        public virtual string Tag => "base";
    }

    public sealed class NarrowDerived : NarrowBase
    {
        public override string Tag => "derived";

        public string Extra => "extra";
    }

    public class NarrowTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual int Measure(NarrowBase subject)
        {
            return subject.Tag.Length;
        }
    }

    public sealed class NarrowTargetDerived : NarrowTarget
    {
        public string Name => "derived-target";
    }

    public static class NarrowMods
    {
        public static List<string> Entries = new List<string>();

        public static void ReadsNarrowedArgument(NarrowDerived subject)
        {
            Entries.Add("arg:" + subject.Extra);
        }

        public static void ReadsNarrowedInstance(NarrowTargetDerived __instance)
        {
            Entries.Add("instance:" + __instance.Name);
        }

        public static void ReadsAnUnrelatedInstance(string __instance)
        {
            Entries.Add("unrelated:" + __instance);
        }
    }

    [Collection("HarmonySerial")]
    public sealed class NarrowBindingTests
    {
        [Fact]
        public void PostfixNarrowingATargetArgument_MatchesHarmony()
        {
            Assert.Equal(
                Run(nameof(NarrowMods.ReadsNarrowedArgument), viaHarmony: true),
                Run(nameof(NarrowMods.ReadsNarrowedArgument), viaHarmony: false));
        }

        [Fact]
        public void PostfixNarrowingTheInstance_MatchesHarmony()
        {
            Assert.Equal(
                Run(nameof(NarrowMods.ReadsNarrowedInstance), viaHarmony: true),
                Run(nameof(NarrowMods.ReadsNarrowedInstance), viaHarmony: false));
        }

        [Fact]
        public void PostfixNarrowingTheInstance_ThrowsWhenTheInstanceIsNotThatType()
        {
            Func<NarrowTarget, NarrowBase, int> wrapper = Compose(nameof(NarrowMods.ReadsNarrowedInstance));

            Assert.Throws<InvalidCastException>(() => wrapper(new NarrowTarget(), new NarrowDerived()));
        }

        [Fact]
        public void PostfixDeclaringAnUnrelatedInstanceType_IsRefused()
        {
            MethodInfo patch = typeof(NarrowMods).GetMethod(nameof(NarrowMods.ReadsAnUnrelatedInstance))!;

            string? reason = PostfixMapping.Validate(Target(), patch);

            Assert.NotNull(reason);
            Assert.Contains("unrelated to 'NarrowTarget'", reason);
        }

        private static MethodBase Target()
        {
            return typeof(NarrowTarget).GetMethod(nameof(NarrowTarget.Measure))!;
        }

        private static string Run(string modName, bool viaHarmony)
        {
            return viaHarmony ? ViaHarmony(modName) : Record(Compose(modName));
        }

        private static string ViaHarmony(string modName)
        {
            const string harmonyId = "test.narrowbinding";
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
            try
            {
                harmony.Patch(Target(), postfix: new HarmonyMethod(typeof(NarrowMods).GetMethod(modName)!));
                return Record((holder, subject) => holder.Measure(subject));
            }
            finally
            {
                TestUnpatch.Own(harmony, harmonyId);
            }
        }

        private static Func<NarrowTarget, NarrowBase, int> Compose(string modName)
        {
            MethodBase target = Target();
            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Injection injection in PostfixMapping.Map(target, typeof(NarrowMods).GetMethod(modName)!, "test.narrowbinding", 400))
            {
                live.Add((live.Count, injection));
            }

            ComposeResult composed = WrapperComposer.Compose(target, InjectionOrderer.OrderForComposition(live));
            Assert.Empty(composed.Rejected);
            return (Func<NarrowTarget, NarrowBase, int>)composed.Wrapper.CreateDelegate(typeof(Func<NarrowTarget, NarrowBase, int>));
        }

        private static string Record(Func<NarrowTarget, NarrowBase, int> call)
        {
            NarrowMods.Entries.Clear();
            int produced = call(new NarrowTargetDerived(), new NarrowDerived());
            return produced + "|" + string.Join(",", NarrowMods.Entries);
        }
    }
}
