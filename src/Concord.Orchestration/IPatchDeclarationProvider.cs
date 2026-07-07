namespace Concord;

/// <summary>
///     Enumerates the <see cref="PatchAttribute" /> declarations of an assembly without a reflection
///     scan. Implemented by the registry type a <see cref="PatchRegistryAttribute" /> points at,
///     normally emitted by Concord.Generators.
/// </summary>
public interface IPatchDeclarationProvider {
    /// <summary>Gets every declaration type in the assembly, in deterministic order.</summary>
    IReadOnlyList<Type> Declarations { get; }
}
