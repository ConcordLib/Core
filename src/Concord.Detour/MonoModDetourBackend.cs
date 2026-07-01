using System.Reflection;
using MonoMod.Core;

namespace Concord.Detour;

/// <summary>
///     Detour backend that delegates native method redirection to MonoMod.Core, the same engine Harmony
///     uses. MonoMod.Core resolves the runtime generic context, so methods declared on a generic type are
///     redirected correctly.
/// </summary>
public sealed class MonoModDetourBackend : IDetourBackend {
    /// <inheritdoc />
    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);

        ICoreDetour detour = DetourFactory.Current.CreateDetour(original, replacement);
        return new Handle(original, detour);
    }

    private sealed class Handle(MethodBase original, ICoreDetour detour) : IDetourHandle {
        private ICoreDetour? _detour = detour;

        public MethodBase Original { get; } = original;

        public bool IsApplied => _detour is { IsApplied: true };

        public void Dispose() {
            ICoreDetour? d = _detour;
            if (d is null) {
                return;
            }

            _detour = null;
            if (d.IsApplied) {
                d.Undo();
            }

            d.Dispose();
        }
    }
}
