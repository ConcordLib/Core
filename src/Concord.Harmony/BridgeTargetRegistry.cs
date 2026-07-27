#nullable disable

using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;

namespace Concord.Harmony;

internal sealed class BridgeTargetRegistry
{
    private readonly object gate = new object();
    private readonly Dictionary<MethodBase, Entry> entries = new Dictionary<MethodBase, Entry>();

    internal long[] Add(MethodBase target, IReadOnlyList<Injection> added)
    {
        target = MethodIdentity.Normalize(target);

        lock (gate)
        {
            Entry entry = GetOrCreateEntry(target);

            long[] owned = new long[added.Count];
            for (int i = 0; i < added.Count; i++)
            {
                long seq = entry.NextSeq++;
                entry.Live.Add((seq, added[i]));
                owned[i] = seq;
            }

            return owned;
        }
    }

    internal (long Seq, Injection Injection)[] Remove(MethodBase target, IReadOnlyList<long> owned)
    {
        target = MethodIdentity.Normalize(target);

        lock (gate)
        {
            if (!entries.TryGetValue(target, out Entry entry))
            {
                return System.Array.Empty<(long Seq, Injection Injection)>();
            }

            List<(long Seq, Injection Injection)> removed = new List<(long Seq, Injection Injection)>(owned.Count);
            foreach (long seq in owned)
            {
                for (int i = entry.Live.Count - 1; i >= 0; i--)
                {
                    if (entry.Live[i].Seq == seq)
                    {
                        removed.Add(entry.Live[i]);
                        entry.Live.RemoveAt(i);
                        break;
                    }
                }
            }

            return removed.ToArray();
        }
    }

    internal void Restore(MethodBase target, IReadOnlyList<(long Seq, Injection Injection)> pairs)
    {
        target = MethodIdentity.Normalize(target);

        lock (gate)
        {
            Entry entry = GetOrCreateEntry(target);
            entry.Live.AddRange(pairs);
        }
    }

    internal Injection[] OrderedSnapshot(MethodBase target)
    {
        target = MethodIdentity.Normalize(target);

        List<(long Seq, Injection Injection)> live;
        lock (gate)
        {
            if (!entries.TryGetValue(target, out Entry entry))
            {
                return System.Array.Empty<Injection>();
            }

            live = new List<(long Seq, Injection Injection)>(entry.Live);
        }

        return InjectionOrderer.OrderForComposition(live);
    }

    internal bool HasInjections(MethodBase target)
    {
        target = MethodIdentity.Normalize(target);

        lock (gate)
        {
            return entries.TryGetValue(target, out Entry entry) && entry.Live.Count > 0;
        }
    }

    internal void Clear(MethodBase target)
    {
        target = MethodIdentity.Normalize(target);

        lock (gate)
        {
            entries.Remove(target);
        }
    }

    private Entry GetOrCreateEntry(MethodBase target)
    {
        if (!entries.TryGetValue(target, out Entry entry))
        {
            entry = new Entry();
            entries[target] = entry;
        }

        return entry;
    }

    private sealed class Entry
    {
        internal List<(long Seq, Injection Injection)> Live { get; } = new List<(long Seq, Injection Injection)>();

        internal long NextSeq { get; set; }
    }
}
