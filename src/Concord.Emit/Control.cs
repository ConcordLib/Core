namespace Concord;

/// <summary>
///     Flow decision returned from a head injection method: continue into the original target body or cancel it.
/// </summary>
/// <remarks>
///     The numeric values are part of the lowering contract: <see cref="Continue" /> must stay <c>0</c> and any
///     nonzero value cancels. Concord lowers the returned value into the wrapper's cancel local, so no enum value
///     is boxed or allocated on the patched hot path.
/// </remarks>
public enum Control {
    /// <summary>Run the original target body.</summary>
    Continue = 0,

    /// <summary>Skip the original target body, like <see cref="ControlHandle.Cancel" />.</summary>
    Cancel = 1,
}
