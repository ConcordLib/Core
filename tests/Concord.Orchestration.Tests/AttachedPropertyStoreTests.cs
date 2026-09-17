using System;
using System.Collections.Generic;
using Concord.AttachedData;
using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class SomeBase { }

public sealed class FirstDeclaration { }

public sealed class SecondDeclaration { }

public sealed class AttachedPropertyStoreTests {
    [Fact]
    public void RegisterAttachedProperty_Duplicate_IsIdempotent() {
        AttachedPropertyStore store = new AttachedPropertyStore();
        IAttachedSlot slot = AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(SomeBase), "X", typeof(int)));
        store.RegisterAttachedProperty(typeof(SomeBase), typeof(SomeBase), "X", typeof(int), slot);
        store.RegisterAttachedProperty(typeof(SomeBase), typeof(SomeBase), "X", typeof(int), slot);

        Assert.Single(store.Entries);
    }

    [Fact]
    public void RegisterAttachedProperty_SameNameFromTwoDeclarations_KeepsBoth() {
        AttachedPropertyStore store = new AttachedPropertyStore();
        IAttachedSlot first = AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(SomeBase), "Y", typeof(int)));
        IAttachedSlot second = AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(SomeBase), "Y", typeof(int)));
        store.RegisterAttachedProperty(typeof(FirstDeclaration), typeof(SomeBase), "Y", typeof(int), first);
        store.RegisterAttachedProperty(typeof(SecondDeclaration), typeof(SomeBase), "Y", typeof(int), second);

        Assert.Equal(2, store.Entries.Count);

        RecordingRegistry registry = new RecordingRegistry();
        store.ReplayInto(registry);

        Assert.Equal(2, registry.Declarations.Count);
        Assert.Contains(typeof(FirstDeclaration), registry.Declarations);
        Assert.Contains(typeof(SecondDeclaration), registry.Declarations);
    }

    private sealed class RecordingRegistry : IAttachedPropertyRegistry {
        public List<Type> Declarations { get; } = [];

        public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
            Declarations.Add(declarationType);
        }
    }
}
