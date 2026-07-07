namespace Concord.Generators.Tests.Consumer;

/// <summary>Target with private state reachable only through generated shadows.</summary>
public class SecretCounter {
    private int hits;

    private int Bump(int by) {
        hits += by;
        return hits;
    }

    /// <summary>The patched method.</summary>
    public void Tick() {
        Bump(1);
    }

    /// <summary>Test-only readout so assertions can see the true count.</summary>
    public int CurrentHits() {
        return Bump(0);
    }
}
