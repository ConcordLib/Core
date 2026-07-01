namespace Concord.Orchestration;

/// <summary>
///     Runtime-adapter-supplied registry that registers a declared field as an attached property on a target type.
///     The runtime adapter owns the side-table storage; the field on the declaration is only a declaration.
/// </summary>
public interface IAttachedPropertyRegistry {
    /// <summary>
    ///     Registers an attached-property named <paramref name="name" /> of type <paramref name="valueType" />
    ///     on <paramref name="baseType" />.
    /// </summary>
    /// <param name="baseType">The target type the property attaches to.</param>
    /// <param name="name">The declared field name.</param>
    /// <param name="valueType">The declared field type.</param>
    void RegisterAttachedProperty(Type baseType, string name, Type valueType);
}
