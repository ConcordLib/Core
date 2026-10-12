namespace Concord;

/// <summary>
///     Control parameter passed to void-target injections that need to cancel target execution.
/// </summary>
/// <remarks>
///     Concord lowers calls on this type into wrapper locals while composing IL. Runtime instances are
///     not allocated on the patched method hot path.
/// </remarks>
public sealed class ControlHandle {
    /// <summary>
    ///     The exception an At.Catch injection caught. Only an At.Catch injection can read it.
    /// </summary>
    public System.Exception Exception { get; } = null!;

    /// <summary>
    ///     Marks the current injection as cancelling the original target invocation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Lowering matches Call/Callvirt on a ControlHandle instance. Making this static would change the emitted call shape and stop ControlHandleLowering from recognising the site.")]
    public void Cancel() { } // NOSONAR recognized control call; concord lowers this into wrapper IL, body is never executed as-is

    /// <summary>
    ///     Cancels the original target invocation, and when <paramref name="skipRemainingHeads" /> is
    ///     <see langword="true" /> also stops the head injections that would have run after this one.
    /// </summary>
    /// <param name="skipRemainingHeads">
    ///     <see langword="true" /> to skip every later head injection as well as the target body;
    ///     <see langword="false" /> only the target body. The overload is valid only in an At.Head injection.
    /// </param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Lowering matches Call/Callvirt on a ControlHandle instance. Making this static would change the emitted call shape and stop ControlHandleLowering from recognising the site.")]
    public void Cancel(bool skipRemainingHeads) { } // NOSONAR recognized control call; concord lowers this into wrapper IL, body is never executed as-is

    /// <summary>
    ///     Resumes the throw a Cancel call stopped. Only an At.Catch injection can call it.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Lowering matches Call/Callvirt on a ControlHandle instance. Making this static would change the emitted call shape and stop ControlHandleLowering from recognising the site.")]
    public void Rethrow() { } // NOSONAR recognized control call; concord lowers this into wrapper IL, body is never executed as-is

    /// <summary>
    ///     Stores a value in this call's state slot. The slot is scoped to the declaring patch
    ///     declaration and lives for one target call.
    /// </summary>
    /// <typeparam name="T">The slot's type. Every injection in one declaration must agree on it.</typeparam>
    /// <param name="value">The value to store.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Lowering matches Call/Callvirt on a ControlHandle instance. Making this static would change the emitted call shape and stop ControlHandleLowering from recognising the site.")]
    public void SetState<T>(T value) { } // NOSONAR recognized control call; concord lowers this into wrapper IL, body is never executed as-is

    /// <summary>
    ///     Reads the value stored in this call's state slot, or the type default when nothing wrote it.
    /// </summary>
    /// <typeparam name="T">The slot's type. Every injection in one declaration must agree on it.</typeparam>
    /// <returns>The stored value.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Lowering matches Call/Callvirt on a ControlHandle instance. Making this static would change the emitted call shape and stop ControlHandleLowering from recognising the site.")]
    public T GetState<T>() => default!; // NOSONAR recognized control call; concord lowers this into wrapper IL, body is never executed as-is
}
