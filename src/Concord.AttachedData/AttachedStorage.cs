using System.Reflection;
using System.Threading;

namespace Concord.AttachedData;

/// <summary>
///     The side tables behind <c>[Attached]</c> fields. A slot is allocated once per declared field and
///     identified by an int baked into the emitted IL, so a field access lowers to one static call.
/// </summary>
public static class AttachedStorage {
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, Allocation> Ids = new Dictionary<string, Allocation>();
    private static readonly List<IAttachedSlot> Registered = new List<IAttachedSlot>();

    private static readonly MethodInfo CreateMethod = typeof(AttachedStorage)
        .GetMethod(nameof(CreateSlot), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    ///     Allocates the slot for one declared field, or returns the slot already allocated for it.
    /// </summary>
    /// <param name="declaringType">The patch declaration that declares the field.</param>
    /// <param name="fieldName">The declared field name.</param>
    /// <param name="valueType">The declared field type.</param>
    /// <returns>The slot id, stable for the process.</returns>
    public static int SlotFor(Type declaringType, string fieldName, Type valueType) {
        // AssemblyQualifiedName, not FullName: two mods can both ship Patches.PawnPatch, and sharing
        // one slot between them would silently share the values too.
        string key = declaringType.AssemblyQualifiedName + "::" + fieldName;
        lock (Gate) {
            if (Ids.TryGetValue(key, out Allocation existing)) {
                if (existing.ValueType != valueType) {
                    throw new InvalidOperationException(
                        "Attached field '" + declaringType.FullName + "." + fieldName + "' was already allocated as '" +
                        existing.ValueType.FullName + "' and cannot be reallocated as '" + valueType.FullName + "'.");
                }

                return existing.Slot;
            }

            int id = Registered.Count;
            Registered.Add((IAttachedSlot)CreateMethod.MakeGenericMethod(valueType).Invoke(null, new object[] { id })!);
            Ids[key] = new Allocation(id, valueType);
            return id;
        }
    }

    /// <summary>Gets the type-erased accessor for an allocated slot.</summary>
    /// <param name="slot">The slot id from <see cref="SlotFor" />.</param>
    /// <returns>The accessor.</returns>
    public static IAttachedSlot SlotAt(int slot) {
        lock (Gate) {
            return Registered[slot];
        }
    }

    /// <summary>Reads a slot. Emitted in place of <c>ldfld</c> on an attached field.</summary>
    /// <typeparam name="TVal">The slot's value type.</typeparam>
    /// <param name="target">The instance the field is attached to.</param>
    /// <param name="slot">The slot id.</param>
    /// <returns>The attached value, or the default when absent.</returns>
    public static TVal Get<TVal>(object target, int slot) {
        return Storage<TVal>.At(slot).Get(target);
    }

    /// <summary>Writes a slot. Emitted in place of <c>stfld</c> on an attached field.</summary>
    /// <typeparam name="TVal">The slot's value type.</typeparam>
    /// <param name="target">The instance the field is attached to.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="slot">The slot id.</param>
    public static void Set<TVal>(object target, TVal value, int slot) {
        Storage<TVal>.At(slot).Set(target, value);
    }

    /// <summary>Takes the address of a slot. Emitted in place of <c>ldflda</c> on an attached field.</summary>
    /// <typeparam name="TVal">The slot's value type.</typeparam>
    /// <param name="target">The instance the field is attached to.</param>
    /// <param name="slot">The slot id.</param>
    /// <returns>A reference to the attached value, created as the default when absent.</returns>
    public static ref TVal GetOrAddRef<TVal>(object target, int slot) {
        return ref Storage<TVal>.At(slot).GetOrAddRef(target);
    }

    private static IAttachedSlot CreateSlot<TVal>(int slot) {
        Storage<TVal>.Ensure(slot);
        return new Slot<TVal>(slot);
    }

    // Grown only under Gate at slot-allocation time. A reader reads Fields once, so it either sees the
    // old array (which still holds every slot it could be asking for) or the new one.
    private readonly struct Allocation {
        public Allocation(int slot, Type valueType) {
            Slot = slot;
            ValueType = valueType;
        }

        public int Slot { get; }

        public Type ValueType { get; }
    }

    private static class Storage<TVal> {
        private static AttachedField<object, TVal>[] fields = new AttachedField<object, TVal>[0];

        internal static AttachedField<object, TVal> At(int slot) {
            return Volatile.Read(ref fields)[slot];
        }

        internal static void Ensure(int slot) {
            AttachedField<object, TVal>[] grown = fields;
            if (grown.Length <= slot) {
                Array.Resize(ref grown, slot + 1);
            }

            grown[slot] ??= new AttachedField<object, TVal>();
            Volatile.Write(ref fields, grown);
        }
    }

    private sealed class Slot<TVal> : IAttachedSlot {
        private readonly int slot;

        internal Slot(int slot) {
            this.slot = slot;
        }

        public object? Get(object target) {
            return Storage<TVal>.At(slot).Get(target);
        }

        public void Set(object target, object? value) {
            Storage<TVal>.At(slot).Set(target, value is TVal typed ? typed : default!);
        }
    }
}
