using Concord.Detour;

namespace Concord.Orchestration;

internal sealed class PatchHandle : IPatchHandle {
    private readonly Action? onDispose;
    private bool disposed;

    public PatchHandle(IReadOnlyList<IDetourHandle> detours, Action? onDispose) {
        Detours = detours;
        this.onDispose = onDispose;
    }

    public bool IsApplied {
        get {
            if (disposed) {
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

    public void Dispose() {
        if (disposed) {
            return;
        }

        disposed = true;
        foreach (IDetourHandle detour in Detours) {
            detour.Dispose();
        }

        onDispose?.Invoke();
    }
}
