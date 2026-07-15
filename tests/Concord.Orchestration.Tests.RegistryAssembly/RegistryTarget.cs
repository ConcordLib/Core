using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Concord.Orchestration.Tests.RegistryAssembly;

/// <summary>Detour target for the registry fast-path tests.</summary>
public class RegistryTarget {
    /// <summary>Bumped by <see cref="Bump" />.</summary>
    [SuppressMessage("Minor Code Smell", "S1104", Justification = "Public mutable field is incremented by the patched target method and read by the test to verify the detour ran; encapsulating it would break the test.")]
    public int Count;

    /// <summary>The patched method.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Bump() {
        Count++;
    }
}
