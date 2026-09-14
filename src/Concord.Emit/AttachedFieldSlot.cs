namespace Concord.Emit;

internal readonly struct AttachedFieldSlot {
    public AttachedFieldSlot(int slot, Type valueType) {
        Slot = slot;
        ValueType = valueType;
    }

    public int Slot { get; }

    public Type ValueType { get; }
}
