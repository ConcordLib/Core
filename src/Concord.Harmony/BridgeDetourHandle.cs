#nullable disable

using System.Reflection;
using Concord.Detour;

namespace Concord.Harmony;

internal sealed class BridgeDetourHandle : IDetourHandle
{
    private readonly HarmonyBridge bridge;
    private readonly MethodBase target;
    private readonly long[] owned;
    private bool disposed;

    internal BridgeDetourHandle(HarmonyBridge bridge, MethodBase target, long[] owned)
    {
        this.bridge = bridge;
        this.target = target;
        this.owned = owned;
    }

    public MethodBase Original
    {
        get { return target; }
    }

    public bool IsApplied
    {
        get { return !disposed; }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        bridge.DisposeHandle(target, owned);
        disposed = true;
    }
}
