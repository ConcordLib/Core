namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a three-argument void call.
/// </summary>
/// <typeparam name="T1">The wrapped call's first argument type.</typeparam>
/// <typeparam name="T2">The wrapped call's second argument type.</typeparam>
/// <typeparam name="T3">The wrapped call's third argument type.</typeparam>
public sealed class VoidOperation<T1, T2, T3> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <param name="arg1">The first value to pass to the original operation.</param>
    /// <param name="arg2">The second value to pass to the original operation.</param>
    /// <param name="arg3">The third value to pass to the original operation.</param>
    public void Invoke(T1 arg1, T2 arg2, T3 arg3) {
    }
}
