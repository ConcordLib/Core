using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace Concord.Host.Adapter;

/// <summary>
///     Names the patch owner behind a stack frame. A replaced body holds the target's own IL and every injection's IL
///     in one method, so a frame alone cannot say whose code threw; this table answers that from the IL offset.
/// </summary>
public static class FrameAttribution {
    private static readonly ConcurrentDictionary<MethodBase, Attributed> Registered =
        new ConcurrentDictionary<MethodBase, Attributed>();

    /// <summary>Records the owner of every byte range in a body just written, replacing any earlier entry.</summary>
    /// <param name="target">The patched method. Keyed by shared-body scope, so one generic's instantiations share an entry.</param>
    /// <param name="body">The composed body, read for each instruction's originating injection.</param>
    /// <param name="encoded">The encoding of that same body, read for the offsets those instructions landed at.</param>
    /// <param name="ordered">The injections in the order they were composed, read for owner names.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when the body and the encoding disagree on how many instructions there are.</exception>
    public static void Register(
        MethodBase target,
        ComposedBody body,
        EncodedBody encoded,
        IReadOnlyList<Injection> ordered) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(ordered);

        if (body.Instructions.Count != encoded.InstructionOffsets.Count) {
            throw new ArgumentException(
                $"The composed body holds {body.Instructions.Count} instructions and the encoded body reports " +
                $"{encoded.InstructionOffsets.Count} offsets, so the two cannot be paired into an attribution table.",
                nameof(encoded));
        }

        Registered[Key(target)] = Build(target, body, encoded, ordered);
    }

    /// <summary>Drops the table for <paramref name="target" />, so its frames stop being attributed. A no-op when none is held.</summary>
    /// <param name="target">The method whose body is coming off.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static void Forget(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        Registered.TryRemove(Key(target), out _);
    }

    /// <summary>The patch owner whose IL the frame is stopped in.</summary>
    /// <param name="frame">A frame off a stack trace or an exception.</param>
    /// <returns>The owner id, or <c>null</c> when the frame is in the target's own IL, in an unpatched method, or carries no IL offset.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="frame" /> is <c>null</c>.</exception>
    public static string? OwnerOfFrame(StackFrame frame) {
        ArgumentNullException.ThrowIfNull(frame);
        MethodBase? method = frame.GetMethod();
        return method is null ? null : OwnerAt(method, frame.GetILOffset());
    }

    /// <summary>The patch owner of the IL at an offset the runtime reported for <paramref name="target" />.</summary>
    /// <param name="target">The method the offset was reported against.</param>
    /// <param name="reportedOffset">The IL offset as the runtime reports it, sentinel shift included.</param>
    /// <returns>The owner id, or <c>null</c> when nothing is registered, the offset lands in the target's own IL, or it falls outside the body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static string? OwnerAt(MethodBase target, int reportedOffset) {
        ArgumentNullException.ThrowIfNull(target);
        return Registered.TryGetValue(Key(target), out Attributed? attributed)
            ? attributed.At(reportedOffset)
            : null;
    }

    /// <summary>Every owner that contributed IL to the body registered for <paramref name="target" />, in composition order.</summary>
    /// <param name="target">The patched method to look up.</param>
    /// <returns>The owner ids, each once. Empty when nothing is registered for the target.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static IReadOnlyList<string> OwnersRegisteredFor(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Registered.TryGetValue(Key(target), out Attributed? attributed)
            ? attributed.Owners
            : [];
    }

    private static MethodBase Key(MethodBase target) {
        return BodyScope.KeyOf(target);
    }

    private static Attributed Build(
        MethodBase target,
        ComposedBody body,
        EncodedBody encoded,
        IReadOnlyList<Injection> ordered) {
        string[] registered = OwnersOf(ordered, out Dictionary<MethodBase, string> byInjection);
        List<int> starts = new List<int>();
        List<string?> owners = new List<string?>();

        for (int i = 0; i < body.Instructions.Count; i++) {
            string? owner = null;
            if (body.Instructions[i].OriginInjection is MethodBase injection) {
                byInjection.TryGetValue(injection, out owner);
            }

            if (owners.Count > 0 && string.Equals(owners[owners.Count - 1], owner, StringComparison.Ordinal)) {
                continue;
            }

            starts.Add(encoded.InstructionOffsets[i]);
            owners.Add(owner);
        }

        if (starts.Count == 0 || starts[starts.Count - 1] < encoded.CodeSize) {
            starts.Add(encoded.CodeSize);
            owners.Add(null);
        }

        return new Attributed(
            [.. starts], [.. owners], registered, WaylithDetourBackend.Band(target, encoded, ordered));
    }

    private static string[] OwnersOf(IReadOnlyList<Injection> ordered, out Dictionary<MethodBase, string> byInjection) {
        List<string> owners = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        byInjection = new Dictionary<MethodBase, string>();

        foreach (Injection injection in ordered) {
            if (seen.Add(injection.Owner)) {
                owners.Add(injection.Owner);
            }

            byInjection.TryAdd(injection.InjectionMethod, injection.Owner);
        }

        return [.. owners];
    }

    private sealed class Attributed {
        private readonly int[] starts;

        private readonly string?[] owners;

        private readonly int band;

        internal Attributed(int[] starts, string?[] owners, string[] registered, int band) {
            this.starts = starts;
            this.owners = owners;
            this.band = band;
            Owners = registered;
        }

        internal IReadOnlyList<string> Owners { get; }

        internal string? At(int reportedOffset) {
            int replacedOffset = reportedOffset;
            if (band >= 0) {
                if (reportedOffset < band) {
                    return null;
                }

                replacedOffset = reportedOffset - band;
            }

            if (replacedOffset < 0) {
                return null;
            }

            int at = Array.BinarySearch(starts, replacedOffset);
            if (at < 0) {
                at = ~at - 1;
            }

            return at < 0 ? null : owners[at];
        }
    }
}
