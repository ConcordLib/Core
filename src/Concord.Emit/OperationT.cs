namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a zero-argument value-producing call,
///     such as a property getter, or a zero-parameter target method under whole-method Around.
/// </summary>
/// <typeparam name="TResult">The value type produced by the wrapped call or target method.</typeparam>
public sealed class Operation<TResult> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <returns>The value produced by the original operation.</returns>
    public TResult Invoke() {
        return default!;
    }
}
