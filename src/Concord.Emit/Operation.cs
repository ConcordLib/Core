namespace Concord;

/// <summary>
///     Control parameter used by invoke injections.
/// </summary>
public sealed class Operation {
    /// <summary>
    ///     Invokes the original operation (a zero-argument void call site, or a zero-parameter void target
    ///     method under whole-method Around) from inside a wrap injection.
    /// </summary>
    public void Invoke() {
    }
} // NOSONAR control parameter type; recognized by lowering via typeof(Operation)
