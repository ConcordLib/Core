namespace Concord.Orchestration;

/// <summary>
///     Thrown when a class carrying <see cref="PatchAttribute" /> is not a valid Concord declaration: its
///     target base type cannot be resolved, or an explicit target disagrees with the class's own base type.
/// </summary>
public sealed class ConcordDeclarationException : Exception {
    /// <summary>
    ///     Initializes a new instance of the <see cref="ConcordDeclarationException" /> class.
    /// </summary>
    /// <param name="message">A description of why the declaration is invalid.</param>
    public ConcordDeclarationException(string message) : base(message) { }
}
