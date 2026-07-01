namespace Concord.Emit.Tests;

public sealed class SeedTarget {
    private readonly int _seed = 7;

    private int Seed() {
        return _seed;
    }

    public int SeedValue() {
        return Seed();
    }
}
