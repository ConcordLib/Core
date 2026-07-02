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
    private ICoreDetour? detour;
    private long sequence;

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

    private IDetourHandle AddInternal(IReadOnlyList<Injection> added) {
        lock (gate) {
            List<long> owned = new List<long>(added.Count);
            foreach (Injection injection in added) {
                long seq = sequence++;
                live.Add((seq, injection));
                owned.Add(seq);
            }

            Recompose();
            return new RegistryHandle(this, owned);
        }
    }

    private void Remove(List<long> owned) {
        lock (gate) {
            foreach (long seq in owned) {
                for (int i = live.Count - 1; i >= 0; i--) {
                    if (live[i].Seq == seq) {
                        live.RemoveAt(i);
                        break;
                    }
                }
            }

            Recompose();
        }
    }

    private void Recompose() {
        ICoreDetour? old = detour;
        detour = null;
        if (old is { IsApplied: true }) {
            old.Undo();
        }

        old?.Dispose();

        if (live.Count == 0) {
            return;
        }

        live.Sort(static (a, b) => {
            int byPriority = b.Injection.Priority.CompareTo(a.Injection.Priority);
            return byPriority != 0 ? byPriority : a.Seq.CompareTo(b.Seq);
        });

        Injection[] ordered = new Injection[live.Count];
        for (int i = 0; i < live.Count; i++) {
            ordered[i] = live[i].Injection;
        }

        ComposeResult result = WrapperComposer.Compose(target, ordered);
        detour = DetourFactory.Current.CreateDetour(target, result.Wrapper);
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

            disposed = true;
            owner.Remove(owned);
        }
    }
}
