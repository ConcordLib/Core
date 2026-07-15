namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a one-argument value-producing call,
///     or a one-parameter target method under whole-method Around.
/// </summary>
/// <typeparam name="T1">The wrapped call's or target method's argument type.</typeparam>
/// <typeparam name="TResult">The value type produced by the wrapped call or target method.</typeparam>
public sealed class Operation<T1, TResult> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <param name="arg1">The value to pass to the original operation.</param>
    /// <returns>The value produced by the original operation.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S1186", Justification = "Marker signature erased and replaced by Concord IL lowering at emit time; a real body would be dead code.")]
    public TResult Invoke(T1 arg1) {
        return default!;
    }
}
