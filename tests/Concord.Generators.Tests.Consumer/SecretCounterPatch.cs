namespace Concord.Generators.Tests.Consumer;

/// <summary>Uses ONLY generated members: the shadow field and the shadow method stub.</summary>
[Patch]
[Shadow("hits")]
[Shadow("Bump", typeof(int))]
public abstract partial class SecretCounterPatch : SecretCounter {
    /// <summary>Head observation of the private state, written by the injection.</summary>
    public static int Observed;

    [Inject(At.Head, nameof(SecretCounter.Tick))]
    private void TickHead() {
        Observed = hits + Bump(0);
    }
}
