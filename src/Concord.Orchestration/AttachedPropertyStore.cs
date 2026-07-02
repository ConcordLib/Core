namespace Concord.Orchestration;

internal sealed class AttachedPropertyStore : IAttachedPropertyRegistry {
    private readonly Dictionary<(Type BaseType, string Name), Type> entries = [];

    public IReadOnlyCollection<KeyValuePair<(Type BaseType, string Name), Type>> Entries => entries;

    public void RegisterAttachedProperty(Type baseType, string name, Type valueType) {
        entries[(baseType, name)] = valueType;
    }

    public bool TryGet(Type baseType, string name, out Type valueType) {
        if (entries.TryGetValue((baseType, name), out Type? found)) {
            valueType = found;
            return true;
        }

        valueType = typeof(object);
        return false;
    }
}
