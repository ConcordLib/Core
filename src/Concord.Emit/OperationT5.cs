namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a four-argument value-producing call,
///     or a four-parameter target method under whole-method Around.
/// </summary>
/// <typeparam name="T1">The wrapped call's or target method's first argument type.</typeparam>
/// <typeparam name="T2">The wrapped call's or target method's second argument type.</typeparam>
/// <typeparam name="T3">The wrapped call's or target method's third argument type.</typeparam>
/// <typeparam name="T4">The wrapped call's or target method's fourth argument type.</typeparam>
/// <typeparam name="TResult">The value type produced by the wrapped call or target method.</typeparam>
public sealed class Operation<T1, T2, T3, T4, TResult> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <param name="arg1">The first value to pass to the original operation.</param>
    /// <param name="arg2">The second value to pass to the original operation.</param>
    /// <param name="arg3">The third value to pass to the original operation.</param>
    /// <param name="arg4">The fourth value to pass to the original operation.</param>
    /// <returns>The value produced by the original operation.</returns>
    public TResult Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4) {
        return default!;
    }
}
