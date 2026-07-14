namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a two-argument void call,
///     or a two-parameter void target method under whole-method Around.
/// </summary>
/// <typeparam name="T1">The wrapped call's or target method's first argument type.</typeparam>
/// <typeparam name="T2">The wrapped call's or target method's second argument type.</typeparam>
public sealed class VoidOperation<T1, T2> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <param name="arg1">The first value to pass to the original operation.</param>
    /// <param name="arg2">The second value to pass to the original operation.</param>
    public void Invoke(T1 arg1, T2 arg2) {
    }
}
