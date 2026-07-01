using System.Reflection;
using Concord.Detour;
using Concord.Emit;

namespace Concord.Orchestration;

internal sealed class CollectingPatchApplier : IPatchApplier {
    private readonly List<IDetourHandle> handles = [];

    public IReadOnlyList<IDetourHandle> Handles => handles;

    public void ApplyPatch(MethodBase target, Injection injection) {
        ComposeResult result = WrapperComposer.Compose(target, [injection]);
        IDetourHandle handle = DetourBackend.Current.Apply(target, result.Wrapper);
        handles.Add(handle);
    }
}
