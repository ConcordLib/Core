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

    /// <summary>
    ///     Stores a value in this call's state slot. The slot is scoped to the declaring patch
    ///     declaration and lives for one target call.
    /// </summary>
    /// <typeparam name="TState">The slot's type. Every injection in one declaration must agree on it.</typeparam>
    /// <param name="value">The value to store.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Lowering matches Call/Callvirt on a ControlHandle instance; making this static would change the emitted call shape and stop ControlHandleLowering from recognising the site.")]
    public void SetState<TState>(TState value) { } // NOSONAR recognized control call; concord lowers this into wrapper IL, body is never executed as-is

    /// <summary>
    ///     Reads the value stored in this call's state slot, or the type default when nothing wrote it.
    /// </summary>
    /// <typeparam name="TState">The slot's type. Every injection in one declaration must agree on it.</typeparam>
    /// <returns>The stored value.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Lowering matches Call/Callvirt on a ControlHandle instance; making this static would change the emitted call shape and stop ControlHandleLowering from recognising the site.")]
    public TState GetState<TState>() => default!; // NOSONAR recognized control call; concord lowers this into wrapper IL, body is never executed as-is
}
