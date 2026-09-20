using Concord.AttachedData;

namespace Concord.Orchestration;

internal sealed class AttachedPropertyStore : IAttachedPropertyRegistry {
    private readonly Dictionary<(Type DeclarationType, Type BaseType, string Name), Registration> entries = [];

    public IReadOnlyCollection<KeyValuePair<(Type DeclarationType, Type BaseType, string Name), Registration>> Entries => entries;

    public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
        entries[(declarationType, baseType, name)] = new Registration(valueType, slot);
    }

    public void Clear() {
        entries.Clear();
    }

    public void ReplayInto(IAttachedPropertyRegistry registry) {
        foreach (KeyValuePair<(Type DeclarationType, Type BaseType, string Name), Registration> entry in entries) {
            registry.RegisterAttachedProperty(
                entry.Key.DeclarationType,
                entry.Key.BaseType,
                entry.Key.Name,
                entry.Value.ValueType,
                entry.Value.Slot);
        }
    }

    internal readonly struct Registration {
        public Registration(Type valueType, IAttachedSlot slot) {
            ValueType = valueType;
            Slot = slot;
        }

        public Type ValueType { get; }

        public IAttachedSlot Slot { get; }
    }
}
