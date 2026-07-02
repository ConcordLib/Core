using System.Reflection;
using Concord.Emit;

namespace Concord.Detour.Tests;

public sealed class FakeDetourBackend : IDetourBackend {
    public List<(MethodBase Original, MethodInfo Replacement)> AppliedDetours { get; } =
        new List<(MethodBase Original, MethodInfo Replacement)>();

    public List<(MethodBase Target, IReadOnlyList<Injection> Added)> ComposedDetours { get; } =
        new List<(MethodBase Target, IReadOnlyList<Injection> Added)>();

    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        AppliedDetours.Add((original, replacement));
        return new FakeHandle(original);
    }

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        ComposedDetours.Add((target, added));
        return new FakeHandle(target);
    }

    private sealed class FakeHandle(MethodBase original) : IDetourHandle {
        public MethodBase Original { get; } = original;
        public bool IsApplied { get; private set; } = true;

        public void Dispose() {
            IsApplied = false;
        }
    }
}
