using Concord.Emit;

namespace Concord.Detour;

/// <summary>
/// Topologically sorts injections for composition order.
/// </summary>
public static class InjectionOrderer {
    /// <summary>
    /// Returns injections in composition order: the order in which patch bodies are nested.
    /// </summary>
    /// <remarks>
    /// The returned array is composition order: bodies are composed in array order, and each
    /// later-composed body wraps the ones before it, so the last element runs first at runtime.
    ///
    /// Runtime ordering: an injection runs before every injection whose <see cref="Injection.Owner"/>
    /// appears in its <see cref="Injection.BeforeOwners"/>, and after every injection whose owner
    /// appears in its <see cref="Injection.AfterOwners"/>. Among unconstrained injections, lower
    /// <see cref="Injection.Priority"/> runs earlier; among equal priorities, higher <c>Seq</c>
    /// (later registration) runs earlier. Equivalently, in the returned array: higher priority
    /// composes earlier, and lower <c>Seq</c> composes earlier.
    ///
    /// A constraint cycle throws <see cref="ConcordEmitException"/> (code CONC052).
    /// </remarks>
    /// <param name="live">Injections paired with registration sequence number.</param>
    /// <returns>Injections in composition order (the order bodies are composed/nested).</returns>
    /// <exception cref="ConcordEmitException">If ordering constraints form a cycle.</exception>
    public static Injection[] OrderForComposition(IReadOnlyList<(long Seq, Injection Injection)> live) {
        int count = live.Count;
        if (count == 0) {
            return [];
        }

        Dictionary<string, List<int>> owners = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        List<HashSet<int>> outgoing = new List<HashSet<int>>(count);
        for (int i = 0; i < count; i++) {
            string owner = live[i].Injection.Owner;
            if (!owners.TryGetValue(owner, out List<int>? nodes)) {
                nodes = [];
                owners.Add(owner, nodes);
            }

            nodes.Add(i);
            outgoing.Add([]);
        }

        int[] incoming = new int[count];
        for (int i = 0; i < count; i++) {
            Injection injection = live[i].Injection;
            AddEdges(i, injection.BeforeOwners, owners, outgoing, incoming, false);
            AddEdges(i, injection.AfterOwners, owners, outgoing, incoming, true);
        }

        bool[] emitted = new bool[count];
        int[] runtimeOrder = new int[count];
        int emittedCount = 0;
        while (emittedCount < count) {
            int next = FindNext(live, incoming, emitted);
            if (next < 0) {
                throw BuildCycleError(live, outgoing, emitted);
            }

            emitted[next] = true;
            runtimeOrder[emittedCount++] = next;
            foreach (int dependent in outgoing[next]) {
                incoming[dependent]--;
            }
        }

        Injection[] result = new Injection[count];
        for (int i = 0; i < count; i++) {
            result[i] = live[runtimeOrder[count - i - 1]].Injection;
        }

        return result;
    }

    private static void AddEdges(
        int constrained,
        IReadOnlyList<string> referencedOwners,
        IReadOnlyDictionary<string, List<int>> owners,
        IReadOnlyList<HashSet<int>> outgoing,
        int[] incoming,
        bool reverse) {
        foreach (string owner in referencedOwners) {
            if (!owners.TryGetValue(owner, out List<int>? referenced)) {
                continue;
            }

            foreach (int node in referenced) {
                int from = reverse ? node : constrained;
                int to = reverse ? constrained : node;
                if (outgoing[from].Add(to)) {
                    incoming[to]++;
                }
            }
        }
    }

    private static int FindNext(
        IReadOnlyList<(long Seq, Injection Injection)> live,
        IReadOnlyList<int> incoming,
        IReadOnlyList<bool> emitted) {
        int next = -1;
        for (int i = 0; i < live.Count; i++) {
            if (emitted[i] || incoming[i] != 0) {
                continue;
            }

            if (next < 0 || RunsBefore(live[i], live[next])) {
                next = i;
            }
        }

        return next;
    }

    private static bool RunsBefore((long Seq, Injection Injection) candidate, (long Seq, Injection Injection) current) {
        int byPriority = candidate.Injection.Priority.CompareTo(current.Injection.Priority);
        if (byPriority != 0) {
            return byPriority < 0;
        }

        return candidate.Seq > current.Seq;
    }

    private static ConcordEmitException BuildCycleError(
        IReadOnlyList<(long Seq, Injection Injection)> live,
        IReadOnlyList<HashSet<int>> outgoing,
        IReadOnlyList<bool> emitted) {
        int[] state = new int[live.Count];
        List<int> path = [];
        for (int i = 0; i < live.Count; i++) {
            if (emitted[i] || state[i] != 0) {
                continue;
            }

            List<int>? cycle = FindCycle(i, outgoing, emitted, state, path);
            if (cycle != null) {
                string owners = string.Join(" -> ", cycle.Select(node => live[node].Injection.Owner));
                return new ConcordEmitException("CONC052", "Patch ordering cycle: " + owners + ".");
            }
        }

        return new ConcordEmitException("CONC052", "Patch ordering cycle.");
    }

    private static List<int>? FindCycle(
        int node,
        IReadOnlyList<HashSet<int>> outgoing,
        IReadOnlyList<bool> emitted,
        int[] state,
        List<int> path) {
        state[node] = 1;
        path.Add(node);

        foreach (int next in outgoing[node]) {
            if (emitted[next]) {
                continue;
            }

            if (state[next] == 0) {
                List<int>? nested = FindCycle(next, outgoing, emitted, state, path);
                if (nested != null) {
                    return nested;
                }
            } else if (state[next] == 1) {
                int start = path.IndexOf(next);
                List<int> cycle = path.GetRange(start, path.Count - start);
                cycle.Add(next);
                return cycle;
            }
        }

        path.RemoveAt(path.Count - 1);
        state[node] = 2;
        return null;
    }
}
