using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

public static class PatchSortTargets
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Original()
    {
    }

    public static void MethodA()
    {
    }

    public static void MethodB()
    {
    }

    public static void MethodC()
    {
    }

    public static void Shared()
    {
    }

    public static MethodInfo MadeFactory(MethodBase original)
    {
        DynamicMethod method = new DynamicMethod("made", typeof(void), System.Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ret);
        return method;
    }
}

public class HarmonyPatchSortTests
{
    private static MethodInfo Method(System.Type type, string name)
    {
        return type.GetMethod(name)
            ?? throw new System.InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static readonly MethodBase OriginalMethod =
        Method(typeof(PatchSortTargets), nameof(PatchSortTargets.Original));

    private static readonly MethodInfo MethodA = Method(typeof(PatchSortTargets), nameof(PatchSortTargets.MethodA));

    private static readonly MethodInfo MethodB = Method(typeof(PatchSortTargets), nameof(PatchSortTargets.MethodB));

    private static readonly MethodInfo MethodC = Method(typeof(PatchSortTargets), nameof(PatchSortTargets.MethodC));

    private static readonly MethodInfo Shared = Method(typeof(PatchSortTargets), nameof(PatchSortTargets.Shared));

    private static readonly MethodInfo MadeFactory = Method(typeof(PatchSortTargets), nameof(PatchSortTargets.MadeFactory));

    [Fact]
    public void SortCategory_HigherPriorityRunsFirst_WhichIsConcordsOrderInverted()
    {
        Patch low = NewPatch(MethodA, index: 0, owner: "low", priority: 0);
        Patch high = NewPatch(MethodB, index: 1, owner: "high", priority: 200);

        IReadOnlyList<Patch> sorted = HarmonyPatchSort.SortCategory(OriginalMethod, new[] { low, high });

        Assert.Equal(new[] { "high", "low" }, sorted.Select(p => p.owner));
    }

    [Fact]
    public void SortCategory_EqualPriorityTiesBreakOnIndexAscending_RegardlessOfInputArrayOrder()
    {
        Patch first = NewPatch(MethodA, index: 0, owner: "first", priority: 100);
        Patch second = NewPatch(MethodB, index: 1, owner: "second", priority: 100);

        IReadOnlyList<Patch> sorted = HarmonyPatchSort.SortCategory(OriginalMethod, new[] { second, first });

        Assert.Equal(new[] { "first", "second" }, sorted.Select(p => p.owner));
    }

    [Fact]
    public void SortCategory_AfterDependencyOverridesPriority()
    {
        Patch a = NewPatch(MethodA, index: 0, owner: "a", priority: 100);
        Patch b = NewPatch(MethodB, index: 1, owner: "b", priority: 500, after: new[] { "a" });

        IReadOnlyList<Patch> sorted = HarmonyPatchSort.SortCategory(OriginalMethod, new[] { a, b });

        Assert.Equal(new[] { "a", "b" }, sorted.Select(p => p.owner));
    }

    [Fact]
    public void SortCategory_MutualBeforeCycleIsCulledByHarmonyNotThrown()
    {
        Patch a = NewPatch(MethodA, index: 0, owner: "a", priority: 100, before: new[] { "b" });
        Patch b = NewPatch(MethodB, index: 1, owner: "b", priority: 100, before: new[] { "a" });

        IReadOnlyList<Patch> sorted = HarmonyPatchSort.SortCategory(OriginalMethod, new[] { a, b });

        Assert.Equal(new[] { "b", "a" }, sorted.Select(p => p.owner));
    }

    [Fact]
    public void SortCategory_OneOwnerWithTwoMethods_EachSortedEntryKeepsItsOwnOwner()
    {
        Patch multiHigh = NewPatch(MethodA, index: 0, owner: "multi", priority: 300);
        Patch single = NewPatch(MethodB, index: 1, owner: "single", priority: 200);
        Patch multiLow = NewPatch(MethodC, index: 2, owner: "multi", priority: 100);

        IReadOnlyList<Patch> sorted = HarmonyPatchSort.SortCategory(OriginalMethod, new[] { multiHigh, single, multiLow });

        Assert.Equal(new[] { "multi", "single", "multi" }, sorted.Select(p => p.owner));
        Assert.Equal(new[] { MethodA, MethodB, MethodC }, sorted.Select(p => p.PatchMethod));
    }

    [Fact]
    public void SortCategory_TwoOwnersRegisterTheSameMethodInfo_EachKeepsItsOwnOwner()
    {
        Patch x = NewPatch(Shared, index: 5, owner: "x", priority: 300);
        Patch y = NewPatch(Shared, index: 2, owner: "y", priority: 500);

        IReadOnlyList<Patch> sorted = HarmonyPatchSort.SortCategory(OriginalMethod, new[] { x, y });

        Assert.Equal(new[] { "y", "x" }, sorted.Select(p => p.owner));
    }

    [Fact]
    public void SortCategory_SameMethodInfoAndAnAfterThatInvertsPriority_KeepsHarmonysOrder()
    {
        Patch x = NewPatch(Shared, index: 0, owner: "x", priority: 0);
        Patch y = NewPatch(Shared, index: 1, owner: "y", priority: 600, after: new[] { "x" });

        IReadOnlyList<Patch> sorted = HarmonyPatchSort.SortCategory(OriginalMethod, new[] { x, y });

        Assert.Equal(new[] { "x", "y" }, sorted.Select(p => p.owner));
    }

    [Fact]
    public void SortCategory_FactoryBackedPatchIsNotDroppedByReresolution()
    {
        Patch factory = NewPatch(MadeFactory, index: 0, owner: "factory", priority: 0);
        Patch plain = NewPatch(MethodA, index: 1, owner: "plain", priority: 200);

        IReadOnlyList<Patch> sorted = HarmonyPatchSort.SortCategory(OriginalMethod, new[] { factory, plain });

        Assert.Equal(new[] { "plain", "factory" }, sorted.Select(p => p.owner));
    }

    [Fact]
    public void Sort_InnerCategoriesHarmonyTracks_AreActuallyRead()
    {
        PatchInfo info = new PatchInfo();
        info.prefixes = new[] { NewPatch(MethodB, index: 0, owner: "plain.prefix", priority: 0) };
        FieldInfo? inner = typeof(PatchInfo).GetField("innerprefixes", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(inner != null, "this Harmony has no innerprefixes field, so the inner categories cannot be exercised");
        inner.SetValue(info, new[] { NewPatch(MethodA, index: 0, owner: "inner.owner", priority: 0) });

        HarmonyPatchSort.ByCategory sorted = HarmonyPatchSort.Sort(OriginalMethod, SupportMatrix.Incoming(info));

        Assert.Equal(new[] { "plain.prefix" }, sorted.Prefixes.Select(p => p.owner));
        Assert.Equal(new[] { "inner.owner" }, sorted.InnerPrefixes.Select(p => p.owner));
    }

    private static Patch NewPatch(
        MethodInfo method,
        int index,
        string owner,
        int priority,
        string[]? before = null,
        string[]? after = null)
    {
        return new Patch(method, index, owner, priority, before ?? System.Array.Empty<string>(), after ?? System.Array.Empty<string>(), false);
    }
}
