namespace Concord;

/// <summary>
///     Binds an injection parameter to one argument of the matched call site. Valid on Head and Tail
///     shifts of the invoke and construction forms.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CaptureAttribute : Attribute {
    /// <summary>
    ///     Binds the decorated parameter to a 1-based argument of the matched call.
    /// </summary>
    /// <param name="arg">The 1-based argument of the matched call to bind.</param>
    public CaptureAttribute(uint arg) {
        Arg = arg;
    }

    /// <summary>
    ///     The 1-based argument of the matched call this parameter binds to.
    /// </summary>
    public uint Arg { get; }
}
