using System.Collections.Concurrent;
using System.Reflection;

namespace Concord.Detour;

/// <summary>
///     The targets whose foreign rebuild Concord cancelled, so the foreign patcher holds a claim in its
///     shared state but never installed a detour.
/// </summary>
public static class SuppressedRebuilds {
    private static readonly ConcurrentDictionary<MethodBase, byte> Held = new ConcurrentDictionary<MethodBase, byte>();

    /// <summary>Records that the foreign rebuild of <paramref name="target" /> was cancelled.</summary>
    /// <param name="target">The method Concord kept.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <see langword="null" />.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1510", Justification = "ArgumentNullException.ThrowIfNull needs net6 or later and this project also targets netstandard2.0 and net472.")]
    public static void Mark(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        Held[MethodIdentity.Normalize(target)] = 0;
    }

    /// <summary>Drops <paramref name="target" />, because the foreign patcher built its own wrapper after all.</summary>
    /// <param name="target">The method Concord gave back.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <see langword="null" />.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1510", Justification = "ArgumentNullException.ThrowIfNull needs net6 or later and this project also targets netstandard2.0 and net472.")]
    public static void Forget(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        Held.TryRemove(MethodIdentity.Normalize(target), out _);
    }

    /// <summary>Whether Concord is holding <paramref name="target" /> against a foreign rebuild.</summary>
    /// <param name="target">The method to test.</param>
    /// <returns><see langword="true" /> while the foreign patcher has no detour on it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <see langword="null" />.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1510", Justification = "ArgumentNullException.ThrowIfNull needs net6 or later and this project also targets netstandard2.0 and net472.")]
    public static bool Holds(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        return Held.ContainsKey(MethodIdentity.Normalize(target));
    }
}
