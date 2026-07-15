namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a six-argument void call,
///     or a six-parameter void target method under whole-method Around.
/// </summary>
/// <typeparam name="T1">The wrapped call's or target method's first argument type.</typeparam>
/// <typeparam name="T2">The wrapped call's or target method's second argument type.</typeparam>
/// <typeparam name="T3">The wrapped call's or target method's third argument type.</typeparam>
/// <typeparam name="T4">The wrapped call's or target method's fourth argument type.</typeparam>
/// <typeparam name="T5">The wrapped call's or target method's fifth argument type.</typeparam>
/// <typeparam name="T6">The wrapped call's or target method's sixth argument type.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2436", Justification = "Arity-N Operation handle; the type parameters mirror the wrapped target method's signature and are the handle's contract.")]
public sealed class VoidOperation<T1, T2, T3, T4, T5, T6> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <param name="arg1">The first value to pass to the original operation.</param>
    /// <param name="arg2">The second value to pass to the original operation.</param>
    /// <param name="arg3">The third value to pass to the original operation.</param>
    /// <param name="arg4">The fourth value to pass to the original operation.</param>
    /// <param name="arg5">The fifth value to pass to the original operation.</param>
    /// <param name="arg6">The sixth value to pass to the original operation.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S1186", Justification = "Marker signature erased and replaced by Concord IL lowering at emit time; a real body would be dead code.")]
    public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) {
    }
}
