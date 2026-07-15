namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a seven-argument value-producing call,
///     or a seven-parameter target method under whole-method Around.
/// </summary>
/// <typeparam name="T1">The wrapped call's or target method's first argument type.</typeparam>
/// <typeparam name="T2">The wrapped call's or target method's second argument type.</typeparam>
/// <typeparam name="T3">The wrapped call's or target method's third argument type.</typeparam>
/// <typeparam name="T4">The wrapped call's or target method's fourth argument type.</typeparam>
/// <typeparam name="T5">The wrapped call's or target method's fifth argument type.</typeparam>
/// <typeparam name="T6">The wrapped call's or target method's sixth argument type.</typeparam>
/// <typeparam name="T7">The wrapped call's or target method's seventh argument type.</typeparam>
/// <typeparam name="TResult">The value type produced by the wrapped call or target method.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2436", Justification = "Arity-N Operation handle; the type parameters mirror the wrapped target method's signature and are the handle's contract.")]
public sealed class Operation<T1, T2, T3, T4, T5, T6, T7, TResult> {
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
    /// <returns>The value produced by the original operation.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S1186", Justification = "Marker signature erased and replaced by Concord IL lowering at emit time; a real body would be dead code.")]
    public TResult Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) {
        return default!;
    }
}
