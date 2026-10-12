using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    using CodeInstruction = HarmonyLib.CodeInstruction;

    public static class ForeignPatchConverterTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Doubled(int x)
        {
            return x * 2;
        }
    }

    public static class ForeignPatchConverterMods
    {
        public static void Pre()
        {
        }

        public static void PreOther()
        {
        }

        public static void Post()
        {
        }

        public static void Fin()
        {
        }

        public static void FinTakesInstance(object __instance)
        {
        }

        public static IEnumerable<CodeInstruction> Trans(IEnumerable<CodeInstruction> instructions)
        {
            return instructions;
        }

        public static IEnumerable<CodeInstruction> TransOther(IEnumerable<CodeInstruction> instructions)
        {
            return instructions;
        }
    }

    public static class ForeignPatchConverterOpenGeneric<T>
    {
        public static void Pre()
        {
        }
    }

    public class ForeignPatchConverterTests
    {
        private const string OwnerA = "test.foreignconvert.a";

        private const string OwnerB = "test.foreignconvert.b";

        private static readonly MethodBase Target =
            typeof(ForeignPatchConverterTargets).GetMethod(nameof(ForeignPatchConverterTargets.Doubled))
            ?? throw new InvalidOperationException(nameof(ForeignPatchConverterTargets) + ".Doubled not found");

        [Fact]
        public void OneOfEachCategory_ConvertsInHarmonysEmittedOrder()
        {
            PatchInfo info = new PatchInfo();
            info.finalizers = Patches(Patch(nameof(ForeignPatchConverterMods.Fin), OwnerA));
            info.postfixes = Patches(Patch(nameof(ForeignPatchConverterMods.Post), OwnerA));
            info.transpilers = Patches(Patch(nameof(ForeignPatchConverterMods.Trans), OwnerA));
            info.prefixes = Patches(Patch(nameof(ForeignPatchConverterMods.Pre), OwnerA));

            ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(Target, info);

            Assert.Empty(converted.Refusals);
            Assert.Equal(7, converted.Injections.Count);

            Assert.IsType<InjectAt.Head>(converted.Injections[0].At);
            Assert.Equal(typeof(PrefixMapping), converted.Injections[0].InjectionMethod.DeclaringType);

            Assert.IsType<InjectAt.Transpiler>(converted.Injections[1].At);
            Assert.Equal(Mod(nameof(ForeignPatchConverterMods.Trans)), converted.Injections[1].InjectionMethod);

            Assert.IsType<InjectAt.Return>(converted.Injections[2].At);
            Assert.Equal(typeof(PostfixMapping), converted.Injections[2].InjectionMethod.DeclaringType);

            for (int i = 3; i < converted.Injections.Count; i++)
            {
                Assert.Equal(typeof(FinalizerParticipant), converted.Injections[i].InjectionMethod.DeclaringType);
            }
        }

        [Fact]
        public void TwoPrefixesAtDifferentPriorities_ComeBackInHarmonysOrderNotRegistrationOrder()
        {
            PatchInfo info = new PatchInfo();
            info.prefixes = Patches(
                Patch(nameof(ForeignPatchConverterMods.Pre), OwnerA, priority: 200),
                Patch(nameof(ForeignPatchConverterMods.PreOther), OwnerB, priority: 600, index: 1));

            ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(Target, info);

            Assert.Empty(converted.Refusals);
            Assert.Equal(new[] { OwnerB, OwnerA }, converted.Injections.Select(i => i.Owner).ToArray());
        }

        [Fact]
        public void TwoTranspilers_KeepTheOrderMapForLiveListProduces()
        {
            PatchInfo info = new PatchInfo();
            info.transpilers = Patches(
                Patch(nameof(ForeignPatchConverterMods.Trans), OwnerA, priority: 200),
                Patch(nameof(ForeignPatchConverterMods.TransOther), OwnerB, priority: 600, index: 1));

            ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(Target, info);

            Patches incoming = SupportMatrix.Incoming(info)
                ?? throw new InvalidOperationException("SupportMatrix found no Patches constructor on this Harmony");
            IReadOnlyList<Injection> expected = HarmonyTranspilerAdapter.MapForLiveList(
                HarmonyPatchSort.SortCategory(Target, incoming.Transpilers));

            Assert.Empty(converted.Refusals);
            Assert.Equal(
                expected.Select(i => i.Owner).ToArray(),
                converted.Injections.Select(i => i.Owner).ToArray());
        }

        [Fact]
        public void EveryConvertedInjection_CarriesForeignOrigin()
        {
            PatchInfo info = new PatchInfo();
            info.prefixes = Patches(Patch(nameof(ForeignPatchConverterMods.Pre), OwnerA));
            info.postfixes = Patches(Patch(nameof(ForeignPatchConverterMods.Post), OwnerA));
            info.transpilers = Patches(Patch(nameof(ForeignPatchConverterMods.Trans), OwnerA));
            info.finalizers = Patches(Patch(nameof(ForeignPatchConverterMods.Fin), OwnerA));

            ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(Target, info);

            Assert.Empty(converted.Refusals);
            string[] notForeign = converted.Injections
                .Where(i => i.Owner != FinalizerParticipant.TerminalOwner && !i.ForeignOrigin)
                .Select(i => i.InjectionMethod.Name + " @ " + i.At.GetType().Name)
                .ToArray();

            Assert.Equal(Array.Empty<string>(), notForeign);
        }

        [Fact]
        public void RefusedShape_IsReportedByOwnerAndReason_AndIsNotInTheList()
        {
            PatchInfo info = new PatchInfo();
            info.prefixes = Patches(Patch(nameof(ForeignPatchConverterMods.Pre), OwnerA));
            info.finalizers = Patches(Patch(nameof(ForeignPatchConverterMods.FinTakesInstance), OwnerB));

            ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(Target, info);

            ForeignPatchConverter.Refusal refusal = Assert.Single(converted.Refusals);
            Assert.Equal(OwnerB, refusal.Owner);
            Assert.Contains("__instance", refusal.Reason);

            Assert.Single(converted.Injections);
            Assert.Equal(typeof(PrefixMapping), converted.Injections[0].InjectionMethod.DeclaringType);
        }

        [Fact]
        public void NonEmptyInnerPrefixes_IsReportedAsRefused()
        {
            PatchInfo info = new PatchInfo();
            info.innerprefixes = Patches(Patch(nameof(ForeignPatchConverterMods.Pre), OwnerB));

            ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(Target, info);

            ForeignPatchConverter.Refusal refusal = Assert.Single(converted.Refusals);
            Assert.Equal(OwnerB, refusal.Owner);
            Assert.Contains("infix", refusal.Reason);
            Assert.Empty(converted.Injections);
        }

        [Fact]
        public void MapperThatThrows_BecomesARefusalRatherThanAnEscapingException()
        {
            PatchInfo info = new PatchInfo();
            info.prefixes = Patches(new Patch(
                typeof(ForeignPatchConverterOpenGeneric<>).GetMethod(nameof(ForeignPatchConverterOpenGeneric<int>.Pre)),
                0,
                OwnerB,
                400,
                Array.Empty<string>(),
                Array.Empty<string>(),
                false));

            ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(Target, info);

            ForeignPatchConverter.Refusal refusal = Assert.Single(converted.Refusals);
            Assert.Equal(OwnerB, refusal.Owner);
            Assert.Contains("Exception", refusal.Reason);
            Assert.Empty(converted.Injections);
        }

        [Fact]
        public void MutualBeforeCycleHarmonyCulls_ConvertsWithoutASecondOrderingRulebook()
        {
            PatchInfo info = new PatchInfo();
            info.prefixes = Patches(
                Patch(nameof(ForeignPatchConverterMods.Pre), OwnerA, before: new[] { OwnerB }),
                Patch(nameof(ForeignPatchConverterMods.PreOther), OwnerB, index: 1, before: new[] { OwnerA }));

            ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(Target, info);

            Assert.Empty(converted.Refusals);
            Assert.Equal(2, converted.Injections.Count);
            foreach (Injection injection in converted.Injections)
            {
                Assert.Empty(injection.BeforeOwners);
                Assert.Empty(injection.AfterOwners);
            }

            List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
            foreach (Injection injection in converted.Injections)
            {
                live.Add((live.Count, injection));
            }

            Injection[] ordered = InjectionOrderer.OrderForComposition(live);
            Assert.Equal(
                converted.Injections.Select(i => i.Owner).Reverse().ToArray(),
                ordered.Select(i => i.Owner).ToArray());
        }

        [Fact]
        public void NoPatchesAtAll_IsEmptyAndNotRefused()
        {
            ForeignPatchConverter.Conversion converted = ForeignPatchConverter.Convert(Target, new PatchInfo());

            Assert.Empty(converted.Injections);
            Assert.Empty(converted.Refusals);
        }

        private static MethodInfo Mod(string name)
        {
            return typeof(ForeignPatchConverterMods).GetMethod(name)
                ?? throw new InvalidOperationException(nameof(ForeignPatchConverterMods) + "." + name + " not found");
        }

        private static Patch[] Patches(params Patch[] patches)
        {
            return patches;
        }

        private static Patch Patch(
            string name,
            string owner,
            int priority = 400,
            int index = 0,
            string[]? before = null,
            string[]? after = null)
        {
            return new Patch(Mod(name), index, owner, priority, before ?? Array.Empty<string>(), after ?? Array.Empty<string>(), false);
        }
    }
}
