namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that replace a value-producing operation.
/// </summary>
/// <typeparam name="T">The value type accepted and returned by the wrapped operation.</typeparam>
public sealed class Operation<T> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <param name="arg">The value to pass to the original operation.</param>
    /// <returns>The value produced by the original operation.</returns>
    public T Invoke(T arg) {
        return default!;
    }
}
