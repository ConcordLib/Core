namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a five-argument void call.
/// </summary>
/// <typeparam name="T1">The wrapped call's first argument type.</typeparam>
/// <typeparam name="T2">The wrapped call's second argument type.</typeparam>
/// <typeparam name="T3">The wrapped call's third argument type.</typeparam>
/// <typeparam name="T4">The wrapped call's fourth argument type.</typeparam>
/// <typeparam name="T5">The wrapped call's fifth argument type.</typeparam>
public sealed class VoidOperation<T1, T2, T3, T4, T5> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <param name="arg1">The first value to pass to the original operation.</param>
    /// <param name="arg2">The second value to pass to the original operation.</param>
    /// <param name="arg3">The third value to pass to the original operation.</param>
    /// <param name="arg4">The fourth value to pass to the original operation.</param>
    /// <param name="arg5">The fifth value to pass to the original operation.</param>
    public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) {
    }
}
