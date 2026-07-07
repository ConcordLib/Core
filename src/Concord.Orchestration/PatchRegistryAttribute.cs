namespace Concord;

/// <summary>
///     Assembly-level pointer at the generated patch registry. When present,
///     <see cref="Patcher.Apply(System.Reflection.Assembly)" /> enumerates declarations
///     through the registry instead of scanning every type in the assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class PatchRegistryAttribute : Attribute {
    /// <summary>Initializes the attribute.</summary>
    /// <param name="registryType">
    ///     The registry type. Must implement <see cref="IPatchDeclarationProvider" /> and have a
    ///     parameterless constructor.
    /// </param>
    public PatchRegistryAttribute(Type registryType) {
        RegistryType = registryType;
    }

    /// <summary>Gets the registry type.</summary>
    public Type RegistryType { get; }
}
