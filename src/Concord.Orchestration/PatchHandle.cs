using System.Threading;
using Concord.Detour;

namespace Concord.Orchestration;

internal sealed class PatchHandle : IPatchHandle {
    private readonly Action? onDispose;
    private int disposed;

    public PatchHandle(IReadOnlyList<IDetourHandle> detours, Action? onDispose) {
        Detours = detours;
        this.onDispose = onDispose;
    }

    public bool IsApplied {
        get {
            if (Volatile.Read(ref disposed) != 0) {
                return false;
            }

            foreach (IDetourHandle detour in Detours) {
                if (!detour.IsApplied) {
                    return false;
                }
            }

            return Detours.Count > 0;
        }
    }

    public IReadOnlyList<IDetourHandle> Detours { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Blocker Code Smell", "S3877", Justification = "Dispose aggregates and surfaces detour-revert failures; silently swallowing them would hide broken unpatching.")]
    public void Dispose() {
        if (Interlocked.Exchange(ref disposed, 1) != 0) {
            return;
        }

        List<Exception>? failures = null;
        try {
            foreach (IDetourHandle detour in Detours) {
                try {
                    detour.Dispose();
                } catch (Exception ex) {
                    (failures ??= []).Add(ex);
                }
            }
        } finally {
            onDispose?.Invoke();
        }

        if (failures is not null) {
            throw new AggregateException("One or more detours failed to revert.", failures);
        }
    }
}
