using System.Reflection;
using System.Threading;
using Concord.Emit;
using MonoMod.Core;

namespace Concord.Detour;

/// <summary>
///     Detour backend that delegates native method redirection to MonoMod.Core, the same engine Harmony
///     uses. MonoMod.Core resolves the runtime generic context, so methods declared on a generic type are
///     redirected correctly.
/// </summary>
public sealed class MonoModDetourBackend : IDetourBackend, IOwnsBodyQuery {
    /// <summary>
    ///     Initializes a new instance of the <see cref="MonoModDetourBackend" /> class and wires
    ///     <see cref="ReversePatchFactory.OwnsBody" /> to it, so <see cref="ReversePatchFactory.Bind" />
    ///     can tell Concord-owned bodies apart from untouched ones without a host doing it by hand.
    /// </summary>
    public MonoModDetourBackend() {
        ReversePatchFactory.OwnsBody = OwnsBody;
    }

    /// <inheritdoc />
    public bool OwnsBody(MethodBase target) {
        return TargetDetourRegistry.OwnsBody(target);
    }

    /// <inheritdoc />
    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        if (original is null) {
            throw new ArgumentNullException(nameof(original));
        }

        if (replacement is null) {
            throw new ArgumentNullException(nameof(replacement));
        }

        ICoreDetour detour = MonoModHost.Factory.CreateDetour(original, replacement);
        return new Handle(original, detour);
    }

    /// <inheritdoc />
    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        if (added is null) {
            throw new ArgumentNullException(nameof(added));
        }

        return TargetDetourRegistry.Add(target, added);
    }

    private sealed class Handle(MethodBase original, ICoreDetour detour) : IDetourHandle {
        private ICoreDetour? _detour = detour;

        public MethodBase Original { get; } = original;

        public bool IsApplied => Volatile.Read(ref _detour) is { IsApplied: true };

        public void Dispose() {
            ICoreDetour? d = Interlocked.Exchange(ref _detour, null);
            if (d is null) {
                return;
            }

            if (d.IsApplied) {
                d.Undo();
            }

            d.Dispose();
        }
    }
}
