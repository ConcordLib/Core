namespace Concord;

/// <summary>
///     Exception thrown when Concord cannot read or allocate an extended enum member.
/// </summary>
public sealed class ConcordEnumException : Exception {
    /// <summary>
    ///     Initializes a new enum exception with a stable diagnostic code.
    /// </summary>
    /// <param name="code">The Concord diagnostic code, such as <c>CONC135</c>.</param>
    /// <param name="message">The human-readable error message.</param>
    public ConcordEnumException(string code, string message) : base(code + ": " + message) {
        Code = code;
    }

    /// <summary>
    ///     The stable Concord diagnostic code for this failure.
    /// </summary>
    public string Code { get; }
}
