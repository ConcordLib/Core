namespace Concord;

/// <summary>
///     Control parameter used by invoke injections that wrap a zero-argument value-producing call,
///     such as a property getter, or a zero-parameter target method under whole-method Around.
/// </summary>
/// <typeparam name="TResult">The value type produced by the wrapped call or target method.</typeparam>
public sealed class Operation<TResult> {
    /// <summary>
    ///     Invokes the original operation from inside a wrap injection.
    /// </summary>
    /// <returns>The value produced by the original operation.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S1186", Justification = "Marker signature erased and replaced by Concord IL lowering at emit time; a real body would be dead code.")]
    public TResult Invoke() {
        return default!;
    }
}
