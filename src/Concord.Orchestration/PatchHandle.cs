using Concord.Detour;

namespace Concord.Orchestration;

internal sealed class PatchHandle : IPatchHandle {
    private readonly Action? onDispose;
    private bool disposed;

    public PatchHandle(IReadOnlyList<IDetourHandle> detours, Action? onDispose) {
        Detours = detours;
        this.onDispose = onDispose;
    }

    public bool IsApplied => !disposed;

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
