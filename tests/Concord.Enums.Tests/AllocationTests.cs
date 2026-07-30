using System.Reflection;
using Xunit;

namespace Concord.Enums.Tests;

[Flags]
public enum Ability {
    None = 0,
    Swim = 1,
    Fly = 2,
    Burrow = 4,
}

public enum SmallKind : byte {
    Zero = 0,
}

public abstract class AllocScratch : ExtendedEnum<WeatherKind> {
    public static WeatherKind Slot;
}

public sealed class FakeStore : IEnumValueStore {
    private Dictionary<string, long> saved = new Dictionary<string, long>(StringComparer.Ordinal);

    public bool TryLoad(out IReadOnlyDictionary<string, long> map) {
        map = Copy(saved);
        return true;
    }

    public void Save(IReadOnlyDictionary<string, long> map) {
        saved = Copy(map);
    }

    public void Forget(string id) {
        saved.Remove(id);
    }

    public IReadOnlyDictionary<string, long> Snapshot() => saved;

    private static Dictionary<string, long> Copy(IReadOnlyDictionary<string, long> source) {
        Dictionary<string, long> copy = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, long> entry in source) {
            copy[entry.Key] = entry.Value;
        }

        return copy;
    }
}

[Collection(RegistryCollection.Name)]
public sealed class AllocationTests {
    [Fact]
    public void Allocation_IsIndependentOfRegistrationOrder() {
        ExtendedEnumRegistry.ResetForTests();
        IReadOnlyDictionary<string, long> first = AllocateNames(["b.two", "a.one", "c.three"]);

        ExtendedEnumRegistry.ResetForTests();
        IReadOnlyDictionary<string, long> second = AllocateNames(["c.three", "b.two", "a.one"]);

        Assert.Equal(first["a.one"], second["a.one"]);
        Assert.Equal(first["b.two"], second["b.two"]);
        Assert.Equal(first["c.three"], second["c.three"]);
    }

    [Fact]
    public void KnownId_KeepsItsValueAcrossReallocation() {
        ExtendedEnumRegistry.ResetForTests();
        FakeStore store = new FakeStore();
        ExtendedEnumRegistry.UseStore(store);

        AllocateNames(["a.one", "b.two"]);
        long originalB = store.Snapshot()["b.two"];

        AllocateNames(["a.one", "b.two", "c.three"]);

        Assert.Equal(originalB, store.Snapshot()["b.two"]);
    }

    [Fact]
    public void OrphanedId_KeepsItsValue_AndComesBackOnReinstall() {
        ExtendedEnumRegistry.ResetForTests();
        FakeStore store = new FakeStore();
        ExtendedEnumRegistry.UseStore(store);

        AllocateNames(["a.one", "b.two"]);
        long originalB = store.Snapshot()["b.two"];

        AllocateNames(["a.one"]);
        Assert.Equal(originalB, store.Snapshot()["b.two"]);

        AllocateNames(["a.one", "b.two"]);
        Assert.Equal(originalB, store.Snapshot()["b.two"]);
    }

    [Fact]
    public void NewMember_DoesNotReuseAnOrphanedValue() {
        ExtendedEnumRegistry.ResetForTests();
        FakeStore store = new FakeStore();
        ExtendedEnumRegistry.UseStore(store);

        AllocateNames(["a.one", "b.two"]);
        long originalB = store.Snapshot()["b.two"];

        IReadOnlyDictionary<string, long> after = AllocateNames(["a.one", "z.new"]);

        Assert.NotEqual(originalB, after["z.new"]);
    }

    [Fact]
    public void PinnedCollision_ThrowsConc135() {
        ExtendedEnumRegistry.ResetForTests();

        ConcordEnumException ex = Assert.Throws<ConcordEnumException>(() => AllocatePinned(typeof(WeatherKind), "p.one", 1));

        Assert.Contains("CONC135", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FlagsEnum_AllocatesTheLowestFreeBit() {
        ExtendedEnumRegistry.ResetForTests();

        IReadOnlyDictionary<string, long> map = AllocateNamesFor(typeof(Ability), ["a.glide"]);

        Assert.Equal(8L, map["a.glide"]);
    }

    [Fact]
    public void ExhaustingTheUnderlyingType_ThrowsConc136() {
        ExtendedEnumRegistry.ResetForTests();

        string[] ids = new string[300];
        for (int i = 0; i < ids.Length; i++) {
            ids[i] = "byte.member." + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        }

        ConcordEnumException ex = Assert.Throws<ConcordEnumException>(() => AllocateNamesFor(typeof(SmallKind), ids));

        Assert.Contains("CONC136", ex.ToString(), StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, long> AllocateNames(string[] ids) {
        return AllocateNamesFor(typeof(WeatherKind), ids);
    }

    private static IReadOnlyDictionary<string, long> AllocateNamesFor(Type enumType, string[] ids) {
        List<ExtendedEnumMember> members = new List<ExtendedEnumMember>();
        foreach (string id in ids) {
            members.Add(new ExtendedEnumMember(enumType, Scratch, id, null));
        }

        return ExtendedEnumRegistry.Allocate(members);
    }

    private static IReadOnlyDictionary<string, long> AllocatePinned(Type enumType, string id, long value) {
        return ExtendedEnumRegistry.Allocate([new ExtendedEnumMember(enumType, Scratch, id, value)]);
    }

    private static FieldInfo Scratch => typeof(AllocScratch).GetField(nameof(AllocScratch.Slot))!;
}
