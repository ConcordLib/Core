namespace Concord;

/// <summary>
///     Control parameter passed to non-void-target injections that can inspect, replace, or supply
///     the target method's return value.
/// </summary>
/// <typeparam name="T">The return type of the target method.</typeparam>
/// <remarks>
///     Concord lowers calls and property access on this type into wrapper locals while composing IL.
///     Runtime instances are not allocated on the patched method hot path.
/// </remarks>
public sealed class ControlHandle<T> {
    /// <summary>
    ///     Gets or sets the target method result carried by the wrapper.
    /// </summary>
    public T ReturnValue { get; set; } = default!;

    /// <summary>
    ///     Marks the current injection as cancelling the original target invocation.
    /// </summary>
    public void Cancel() { } // NOSONAR recognized control call; concord lowers this into wrapper IL, body is never executed as-is
}
