using Concord.AttachedData;

namespace Concord.Orchestration;

/// <summary>
///     Runtime-adapter-supplied registry that receives every <c>[Attached]</c> field declared on a patch.
///     Concord owns the storage; the adapter decides what else to do with it, such as persisting it.
/// </summary>
public interface IAttachedPropertyRegistry {
    /// <summary>
    ///     Registers an attached property named <paramref name="name" /> of type <paramref name="valueType" />
    ///     on <paramref name="baseType" />.
    /// </summary>
    /// <param name="declarationType">The patch declaration that declares the field. Its assembly identifies the owning mod.</param>
    /// <param name="baseType">The target type the property attaches to.</param>
    /// <param name="name">The declared field name.</param>
    /// <param name="valueType">The declared field type.</param>
    /// <param name="slot">The storage the patched code reads and writes, for the adapter to read and write too.</param>
    void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot);
}
