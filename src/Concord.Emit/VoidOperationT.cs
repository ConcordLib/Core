namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a one-argument void call,
///     such as a property setter.
/// </summary>
/// <typeparam name="T1">The wrapped call's argument type.</typeparam>
public sealed class VoidOperation<T1> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <param name="arg1">The value to pass to the original operation.</param>
    public void Invoke(T1 arg1) {
    }
}
