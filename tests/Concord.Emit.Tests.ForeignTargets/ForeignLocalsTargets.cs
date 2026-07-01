namespace Concord.Emit.Tests.ForeignTargets;

/// <summary>
///     A void instance target defined in a separate assembly. Composing a Head injection method over it
///     forces the injection method's BCL locals to be imported into the wrapper module, reproducing the
///     null-hash <c>AssemblyNameReference</c> scope on a cross-module local.
/// </summary>
public sealed class ForeignLocalTarget {
    /// <summary>Gets or sets the number of times the target body actually ran.</summary>
    public static int Runs { get; set; }

    /// <summary>Does trivial work so the wrapper has a real instance spine to copy.</summary>
    public void Work() { // NOSONAR deliberately an instance target so the wrapper copies a real instance spine
        Runs++;
    }
}

/// <summary>
///     A Head injection method defined in the foreign assembly that mirrors a real consumer patch: a
///     <c>try/catch</c> whose <see cref="System.Exception" /> catch local is only consumed through a
///     boxing string concatenation. The compiler emits an <see cref="System.Exception" /> local whose
///     type is never imported through a normalizing operand, so when its type is copied into the
///     wrapper module the BCL <see cref="System.Reflection.AssemblyName" /> scope stays hash-less.
/// </summary>
public static class ForeignLocalsInjectionMethods {
    /// <summary>Gets or sets a marker proving the head body ran.</summary>
    public static string Marker { get; set; } = string.Empty;

    /// <summary>
    ///     Head injection method with an <see cref="System.Exception" /> catch local consumed only via string
    ///     concatenation, then cancels the original.
    /// </summary>
    /// <param name="ch">The control handle used to cancel the target method.</param>
    public static void HeadWithForeignBclLocals(ControlHandle ch) {
        try {
            Marker = "ran";
        } catch (Exception e) {
            Marker = "failed: " + e;
        }

        ch.Cancel();
    }
}
