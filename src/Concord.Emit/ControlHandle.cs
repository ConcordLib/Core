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
    ///     Marks the current injection as cancelling the original target invocation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Lowering matches Call/Callvirt on a ControlHandle instance; making this static would change the emitted call shape and stop ControlHandleLowering from recognising the site.")]
    public void Cancel() { } // NOSONAR recognized control call; concord lowers this into wrapper IL, body is never executed as-is
}
