using Concord.AttachedData;
using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class SomeBase { }

public sealed class AttachedPropertyStoreTests {
    [Fact]
    public void RegisterAttachedProperty_Duplicate_IsIdempotent() {
        AttachedPropertyStore store = new AttachedPropertyStore();
        IAttachedSlot slot = AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(SomeBase), "X", typeof(int)));
        store.RegisterAttachedProperty(typeof(SomeBase), typeof(SomeBase), "X", typeof(int), slot);
        store.RegisterAttachedProperty(typeof(SomeBase), typeof(SomeBase), "X", typeof(int), slot);

        Assert.Single(store.Entries);
    }
}
