namespace Concord.Emit;

/// <summary>
/// An exception thrown during neutral model conversion.
/// </summary>
public sealed class NeutralConversionException : Exception {
    /// <summary>
    /// Creates a conversion exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public NeutralConversionException(string message) : base(message) {
    }
}
