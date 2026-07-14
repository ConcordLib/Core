namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap an eight-argument value-producing call.
/// </summary>
/// <typeparam name="T1">The wrapped call's first argument type.</typeparam>
/// <typeparam name="T2">The wrapped call's second argument type.</typeparam>
/// <typeparam name="T3">The wrapped call's third argument type.</typeparam>
/// <typeparam name="T4">The wrapped call's fourth argument type.</typeparam>
/// <typeparam name="T5">The wrapped call's fifth argument type.</typeparam>
/// <typeparam name="T6">The wrapped call's sixth argument type.</typeparam>
/// <typeparam name="T7">The wrapped call's seventh argument type.</typeparam>
/// <typeparam name="T8">The wrapped call's eighth argument type.</typeparam>
/// <typeparam name="TResult">The value type produced by the wrapped call.</typeparam>
public sealed class Operation<T1, T2, T3, T4, T5, T6, T7, T8, TResult> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <param name="arg1">The first value to pass to the original operation.</param>
    /// <param name="arg2">The second value to pass to the original operation.</param>
    /// <param name="arg3">The third value to pass to the original operation.</param>
    /// <param name="arg4">The fourth value to pass to the original operation.</param>
    /// <param name="arg5">The fifth value to pass to the original operation.</param>
    /// <param name="arg6">The sixth value to pass to the original operation.</param>
    /// <param name="arg7">The seventh value to pass to the original operation.</param>
    /// <param name="arg8">The eighth value to pass to the original operation.</param>
    /// <returns>The value produced by the original operation.</returns>
    public TResult Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) {
        return default!;
    }
}
