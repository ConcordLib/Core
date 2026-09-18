namespace Concord;

/// <summary>
///     Control parameter used by invoke injections.
/// </summary>
public sealed class Operation {
    /// <summary>
    ///     Invokes the original operation (a zero-argument void call site, or a zero-parameter void target
    ///     method under whole-method Around) from inside a wrap injection.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S1186", Justification = "Marker signature erased and replaced by Concord IL lowering at emit time. A real body would be dead code.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Lowering matches Call/Callvirt on an Operation instance. Making this static would change the emitted call shape and stop ControlHandleLowering from recognising the site.")]
    public void Invoke() {
    }
} // NOSONAR control parameter type; recognized by lowering via typeof(Operation)
