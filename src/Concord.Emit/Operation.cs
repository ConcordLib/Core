namespace Concord;

/// <summary>
///     Control parameter used by invoke injections.
/// </summary>
public sealed class Operation {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    public void Invoke() {
    }
} // NOSONAR control parameter type; recognized by lowering via typeof(Operation), intentionally empty
