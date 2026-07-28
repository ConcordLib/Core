using System.Reflection;
using Concord.Detour;
using Concord.Emit;

namespace Concord.Orchestration;

internal sealed class CollectingPatchApplier : IPatchApplier {
    private readonly List<IDetourHandle> handles = [];

    public IReadOnlyList<IDetourHandle> Handles => handles;

    public void ApplyPatch(MethodBase target, Injection injection) {
        WrapperComposer.RejectSharedGenericInstantiation(target);
        MethodBase canonical = WrapperComposer.ResolveBodyTarget(target, injection.Body);
        WrapperComposer.RejectSharedGenericInstantiation(canonical);
        WrapperComposer.ValidateBodySelection(target, canonical, injection);
        IDetourHandle handle = DetourBackend.Current.ApplyComposed(canonical, [injection]);
        handles.Add(handle);
    }
}
