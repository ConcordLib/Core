using System.Reflection;
using Concord.Emit;
using MonoMod.Core;

namespace Concord.Detour;

internal sealed class TargetDetourRegistry {
    private static readonly Dictionary<MethodBase, TargetDetourRegistry> Registries = new Dictionary<MethodBase, TargetDetourRegistry>();
    private static readonly object RegistriesGate = new object();

    private readonly MethodBase target;
    private readonly object gate = new object();
    private readonly List<(long Seq, Injection Injection)> live = [];
    private readonly List<IntPtr> wrapperKeys = [];
    private ICoreDetour? detour;
    private long sequence;
    private IReadOnlyList<string> owners = [];

    private TargetDetourRegistry(MethodBase target) {
        this.target = target;
    }

    private bool IsApplied {
        get {
            lock (gate) {
                return detour is { IsApplied: true };
            }
        }
    }

    internal static IDetourHandle Add(MethodBase target, IReadOnlyList<Injection> added) {
        TargetDetourRegistry registry;
        lock (RegistriesGate) {
            if (!Registries.TryGetValue(target, out registry!)) {
                registry = new TargetDetourRegistry(target);
                Registries[target] = registry;
            }
        }

        return registry.AddInternal(added);
    }

    // Keys are normalized when RoutingDetourBackend is in the chain and raw when it is not, so try both.
    internal static IReadOnlyList<string> OwnersFor(MethodBase target) {
        TargetDetourRegistry? registry = Find(target) ?? Find(MethodIdentity.Normalize(target));
        if (registry == null) {
            return [];
        }

        lock (registry.gate) {
            return registry.owners;
        }
    }

    private static TargetDetourRegistry? Find(MethodBase target) {
        lock (RegistriesGate) {
            return Registries.TryGetValue(target, out TargetDetourRegistry? registry) ? registry : null;
        }
    }

    // Composition order runs backwards: the body composed last wraps the others, so it runs first.
    private static IReadOnlyList<string> BuildOwners(IReadOnlyList<Injection> ordered) {
        if (ordered.Count == 0) {
            return [];
        }

        List<string> result = new List<string>(ordered.Count);
        for (int i = ordered.Count - 1; i >= 0; i--) {
            string owner = ordered[i].Owner;
            if (!result.Contains(owner)) {
                result.Add(owner);
            }
        }

        return result;
    }

    private IDetourHandle AddInternal(IReadOnlyList<Injection> added) {
        lock (gate) {
            List<long> owned = new List<long>(added.Count);
            List<(long Seq, Injection Injection)> tentative = new List<(long Seq, Injection Injection)>(live.Count + added.Count);
            tentative.AddRange(live);
            bool debug = false;
            long nextSequence = sequence;
            foreach (Injection injection in added) {
                long seq = nextSequence++;
                tentative.Add((seq, injection));
                owned.Add(seq);
                debug |= injection.Debug;
            }

            Injection[] ordered = InjectionOrderer.OrderForComposition(tentative);
            List<(long Seq, Injection Injection)> previous = new List<(long Seq, Injection Injection)>(live);
            long previousSequence = sequence;
            live.Clear();
            live.AddRange(tentative);
            sequence = nextSequence;
            RecomposeOrRestore(ordered, debug, previous, previousSequence);
            return new RegistryHandle(this, owned);
        }
    }

    private void Remove(List<long> owned) {
        lock (gate) {
            List<(long Seq, Injection Injection)> previous = new List<(long Seq, Injection Injection)>(live);
            foreach (long seq in owned) {
                for (int i = live.Count - 1; i >= 0; i--) {
                    if (live[i].Seq == seq) {
                        live.RemoveAt(i);
                        break;
                    }
                }
            }

            RecomposeOrRestore(InjectionOrderer.OrderForComposition(live), false, previous, null);
        }
    }

    private void RecomposeOrRestore(
        IReadOnlyList<Injection> ordered,
        bool debug,
        List<(long Seq, Injection Injection)> previous,
        long? previousSequence) {
        try {
            Recompose(ordered, debug);
        } catch (Exception primaryFailure) {
            live.Clear();
            live.AddRange(previous);
            if (previousSequence.HasValue) {
                sequence = previousSequence.Value;
            }

            bool restoredDebug = false;
            foreach ((long Seq, Injection Injection) entry in live) {
                restoredDebug |= entry.Injection.Debug;
            }

            try {
                Recompose(InjectionOrderer.OrderForComposition(live), restoredDebug);
            } catch (Exception rollbackFailure) {
                throw new AggregateException(primaryFailure, rollbackFailure);
            }

            throw;
        }
    }

    private void Recompose(IReadOnlyList<Injection> ordered, bool debug) {
        ComposeResult? composed = null;
        if (ordered.Count > 0) {
            if (debug) {
                PatchDebugLog.Append(target, WrapperComposer.ComposeDump(target, ordered));
            }

            composed = WrapperComposer.Compose(target, ordered);
        }

        ICoreDetour? old = detour;
        detour = null;
        MethodIdentity.Forget(wrapperKeys);
        if (old is { IsApplied: true }) {
            old.Undo();
        }

        old?.Dispose();

        if (composed is not null) {
            detour = MonoModHost.Factory.CreateDetour(target, composed.Wrapper);
            MethodIdentity.Remember(composed.Wrapper, target, wrapperKeys);
        }

        owners = BuildOwners(ordered);
    }

    private sealed class RegistryHandle : IDetourHandle {
        private readonly TargetDetourRegistry owner;
        private readonly List<long> owned;
        private bool disposed;

        public RegistryHandle(TargetDetourRegistry owner, List<long> owned) {
            this.owner = owner;
            this.owned = owned;
        }

        public MethodBase Original => owner.target;

        public bool IsApplied => !disposed && owner.IsApplied;

        public void Dispose() {
            if (disposed) {
                return;
            }

            owner.Remove(owned);
            disposed = true;
        }
    }
}
