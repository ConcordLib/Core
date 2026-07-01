namespace Concord.Emit;

/// <summary>
///     Exception thrown when Concord cannot compose a wrapper for a target method.
/// </summary>
public sealed class ConcordEmitException : Exception {
    /// <summary>
    ///     Initializes a new emit exception with a stable diagnostic code.
    /// </summary>
    /// <param name="code">The Concord diagnostic code, such as <c>CONC012</c>.</param>
    /// <param name="message">The human-readable error message.</param>
    public ConcordEmitException(string code, string message) : base(message) {
        Code = code;
    }

    /// <summary>
    ///     Gets the stable Concord diagnostic code for this failure.
    /// </summary>
    public string Code { get; }
}
