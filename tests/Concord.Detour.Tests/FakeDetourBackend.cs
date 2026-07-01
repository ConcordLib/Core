using System.Reflection;

namespace Concord.Detour.Tests;

public sealed class FakeDetourBackend : IDetourBackend {
    public List<(MethodBase Original, MethodInfo Replacement)> AppliedDetours { get; } =
        new List<(MethodBase Original, MethodInfo Replacement)>();

    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        AppliedDetours.Add((original, replacement));
        return new FakeHandle(original);
    }

    private sealed class FakeHandle(MethodBase original) : IDetourHandle {
        public MethodBase Original { get; } = original;
        public bool IsApplied { get; private set; } = true;

        public void Dispose() {
            IsApplied = false;
        }
    }
}
