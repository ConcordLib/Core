using Concord.AttachedData;

namespace Concord.Orchestration;

internal sealed class AttachedPropertyStore : IAttachedPropertyRegistry {
    private readonly Dictionary<(Type BaseType, string Name), Registration> entries = [];

    public IReadOnlyCollection<KeyValuePair<(Type BaseType, string Name), Registration>> Entries => entries;

    public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
        entries[(baseType, name)] = new Registration(declarationType, valueType, slot);
    }

    public bool TryGet(Type baseType, string name, out Type valueType) {
        if (entries.TryGetValue((baseType, name), out Registration found)) {
            valueType = found.ValueType;
            return true;
        }

        valueType = typeof(object);
        return false;
    }

    public void ReplayInto(IAttachedPropertyRegistry registry) {
        foreach (KeyValuePair<(Type BaseType, string Name), Registration> entry in entries) {
            registry.RegisterAttachedProperty(
                entry.Value.DeclarationType,
                entry.Key.BaseType,
                entry.Key.Name,
                entry.Value.ValueType,
                entry.Value.Slot);
        }
    }

    internal readonly struct Registration {
        public Registration(Type declarationType, Type valueType, IAttachedSlot slot) {
            DeclarationType = declarationType;
            ValueType = valueType;
            Slot = slot;
        }

        public Type DeclarationType { get; }

        public Type ValueType { get; }

        public IAttachedSlot Slot { get; }
    }
}
