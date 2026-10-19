using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Concord.Harmony;

/// <summary>
///     Reorders a method's Harmony patches through <c>HarmonyLib.PatchSorter</c>, which returns the
///     patches themselves so owner, priority and the before and after lists all survive the sort.
/// </summary>
public static class HarmonyPatchSort
{
    private static readonly Type? SorterType =
        typeof(HarmonyLib.Harmony).Assembly.GetType("HarmonyLib.PatchSorter");

    private static readonly ConstructorInfo? SorterCtor = FindSorterCtor();

    private static readonly MethodInfo? SorterSort =
        SorterType?.GetMethod("Sort", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

    private static readonly ConcurrentDictionary<string, (FieldInfo? Field, PropertyInfo? Property)> InnerAccessors = new();

    /// <summary>
    ///     Reports whether <c>HarmonyLib.PatchSorter</c> is reachable on the Harmony loaded in this process.
    /// </summary>
    public static bool Available => SorterCtor != null && SorterSort != null;

    /// <summary>
    ///     Sorts the prefix, postfix, transpiler, finalizer and inner categories into Harmony's run order.
    /// </summary>
    /// <param name="original">The patched method.</param>
    /// <param name="patchInfo">The patch categories currently registered on <paramref name="original" />.</param>
    /// <returns>Each category in Harmony's run order. Inner categories come back empty on a Harmony without them.</returns>
    public static ByCategory Sort(MethodBase original, Patches? patchInfo)
    {
        if (patchInfo == null)
        {
            Patch[] empty = Array.Empty<Patch>();
            return new ByCategory(empty, empty, empty, empty, empty, empty, empty);
        }

        return new ByCategory(
            SortCategory(original, patchInfo.Prefixes),
            SortCategory(original, patchInfo.Postfixes),
            SortCategory(original, patchInfo.Transpilers),
            SortCategory(original, patchInfo.Finalizers),
            SortCategory(original, InnerCategory(patchInfo, "InnerPrefixes")),
            SortCategory(original, InnerCategory(patchInfo, "InnerPostfixes")),
            SortCategory(original, InnerCategory(patchInfo, "InnerFinalizers")));
    }

    /// <summary>
    ///     Sorts one patch category into Harmony's run order.
    /// </summary>
    /// <param name="original">The patched method, kept for call-site symmetry; the sort itself does not read it.</param>
    /// <param name="category">The unsorted patches for one category.</param>
    /// <returns>The same <see cref="Patch" /> instances, reordered to Harmony's run order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when Harmony's sorter is not reachable.</exception>
    public static IReadOnlyList<Patch> SortCategory(MethodBase original, IReadOnlyList<Patch>? category)
    {
        if (category == null || category.Count == 0)
        {
            return Array.Empty<Patch>();
        }

        ConstructorInfo? ctor = SorterCtor;
        MethodInfo? sort = SorterSort;
        if (ctor == null || sort == null)
        {
            throw new InvalidOperationException(
                "HarmonyLib.PatchSorter is not reachable on Harmony " + HarmonyVersionText() +
                ", so Concord cannot reproduce Harmony's patch order and refuses to guess one");
        }

        Patch[] patches = new Patch[category.Count];
        for (int i = 0; i < patches.Length; i++)
        {
            patches[i] = category[i];
        }

        object[] args = new object[ctor.GetParameters().Length];
        args[0] = patches;
        for (int i = 1; i < args.Length; i++)
        {
            args[i] = false;
        }

        object sorter = ctor.Invoke(args);
        return (Patch[])sort.Invoke(sorter, Array.Empty<object>())!;
    }

    internal static IReadOnlyList<Patch> InnerCategory(Patches patchInfo, string name)
    {
        (FieldInfo? field, PropertyInfo? property) = InnerAccessors.GetOrAdd(name, static n => (
            typeof(Patches).GetField(n, BindingFlags.Public | BindingFlags.Instance),
            typeof(Patches).GetProperty(n, BindingFlags.Public | BindingFlags.Instance)));

        if (field != null)
        {
            return field.GetValue(patchInfo) as IReadOnlyList<Patch> ?? Array.Empty<Patch>();
        }

        if (property != null)
        {
            return property.GetValue(patchInfo) as IReadOnlyList<Patch> ?? Array.Empty<Patch>();
        }

        return Array.Empty<Patch>();
    }

    private static ConstructorInfo? FindSorterCtor()
    {
        if (SorterType == null)
        {
            return null;
        }

        foreach (ConstructorInfo ctor in SorterType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            ParameterInfo[] parameters = ctor.GetParameters();
            if (parameters.Length < 2 || parameters[0].ParameterType != typeof(Patch[]) || parameters[1].ParameterType != typeof(bool))
            {
                continue;
            }

            bool rest = true;
            for (int i = 2; i < parameters.Length; i++)
            {
                rest &= parameters[i].ParameterType == typeof(bool);
            }

            if (rest)
            {
                return ctor;
            }
        }

        return null;
    }

    private static string HarmonyVersionText()
    {
        return typeof(HarmonyLib.Harmony).Assembly.GetName().Version?.ToString() ?? "<unknown>";
    }

    /// <summary>
    ///     Result of <see cref="Sort" />: one list per category, each in Harmony's run order.
    /// </summary>
    public sealed class ByCategory
    {
        internal ByCategory(
            IReadOnlyList<Patch> prefixes,
            IReadOnlyList<Patch> postfixes,
            IReadOnlyList<Patch> transpilers,
            IReadOnlyList<Patch> finalizers,
            IReadOnlyList<Patch> innerPrefixes,
            IReadOnlyList<Patch> innerPostfixes,
            IReadOnlyList<Patch> innerFinalizers)
        {
            Prefixes = prefixes;
            Postfixes = postfixes;
            Transpilers = transpilers;
            Finalizers = finalizers;
            InnerPrefixes = innerPrefixes;
            InnerPostfixes = innerPostfixes;
            InnerFinalizers = innerFinalizers;
        }

        /// <summary>
        ///     Prefixes, in Harmony's run order rather than registration order.
        /// </summary>
        public IReadOnlyList<Patch> Prefixes { get; }

        /// <summary>
        ///     Postfixes, in Harmony's run order rather than registration order.
        /// </summary>
        public IReadOnlyList<Patch> Postfixes { get; }

        /// <summary>
        ///     Transpilers, in Harmony's run order rather than registration order.
        /// </summary>
        public IReadOnlyList<Patch> Transpilers { get; }

        /// <summary>
        ///     Finalizers, in Harmony's run order rather than registration order.
        /// </summary>
        public IReadOnlyList<Patch> Finalizers { get; }

        /// <summary>
        ///     Inner prefixes, empty on a Harmony that does not track them.
        /// </summary>
        public IReadOnlyList<Patch> InnerPrefixes { get; }

        /// <summary>
        ///     Inner postfixes, empty on a Harmony that does not track them.
        /// </summary>
        public IReadOnlyList<Patch> InnerPostfixes { get; }

        /// <summary>
        ///     Inner finalizers, empty on a Harmony that does not track them.
        /// </summary>
        public IReadOnlyList<Patch> InnerFinalizers { get; }
    }
}
