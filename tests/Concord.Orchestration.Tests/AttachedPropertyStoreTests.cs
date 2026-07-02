using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class SomeBase { }

public sealed class AttachedPropertyStoreTests {
    [Fact]
    public void RegisterAttachedProperty_Duplicate_IsIdempotent() {
        AttachedPropertyStore store = new AttachedPropertyStore();
        store.RegisterAttachedProperty(typeof(SomeBase), "X", typeof(int));
        store.RegisterAttachedProperty(typeof(SomeBase), "X", typeof(int));

        Assert.Single(store.Entries);
    }
}
