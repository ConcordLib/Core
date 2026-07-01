namespace Concord.Orchestration;

internal sealed class AttachedPropertyStore : IAttachedPropertyRegistry {
    private readonly List<(Type BaseType, string Name, Type ValueType)> entries = [];

    public IReadOnlyList<(Type BaseType, string Name, Type ValueType)> Entries => entries;

    public void RegisterAttachedProperty(Type baseType, string name, Type valueType) {
        entries.Add((baseType, name, valueType));
    }

    public bool TryGet(Type baseType, string name, out Type valueType) {
        foreach ((Type BaseType, string Name, Type ValueType) entry in entries) {
            if (entry.BaseType == baseType && entry.Name == name) {
                valueType = entry.ValueType;
                return true;
            }
        }

        valueType = typeof(object);
        return false;
    }
}
