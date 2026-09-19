using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Exception thrown when Concord cannot compose a wrapper for a target method.
/// </summary>
public sealed class ConcordEmitException : Exception {
    /// <summary>
    ///     Initializes a new emit exception with a stable diagnostic code.
    /// </summary>
    /// <param name="code">The Concord diagnostic code, such as <c>CONC012</c>.</param>
    /// <param name="message">The human-readable error message.</param>
    public ConcordEmitException(string code, string message) : base(code + ": " + message) {
        Code = code;
        Detail = message;
    }

    /// <summary>
    ///     Gets the stable Concord diagnostic code for this failure.
    /// </summary>
    public string Code { get; }

    /// <summary>
    ///     The injection method whose <c>[Local]</c> or <c>At.Local</c> selector could not be resolved
    ///     against the composed body. Null on every failure that is not a local binding.
    /// </summary>
    internal MethodBase? LocalBindingMethod { get; init; }

    /// <summary>
    ///     Whether another mod's edit to the body is what broke the selector: a slot its transpiler
    ///     added, or a <c>By</c> count its edits moved. Only those are evicted. A selector that would
    ///     have failed against the target's own IL is the author's own mistake, and dropping it would
    ///     hide the mistake instead of reporting it.
    /// </summary>
    internal bool BrokenByAForeignEdit { get; init; }

    /// <summary>
    ///     The exact injection the failure belongs to, stamped by composition dispatch. Eviction uses
    ///     it so two injections sharing one method do not fall together. Null when the failure surfaced
    ///     outside the per-injection dispatch loop.
    /// </summary>
    internal Injection? LocalBindingInjection { get; set; }

    /// <summary>
    ///     The message without its leading diagnostic code, for rebuilding a failure without stacking
    ///     a second <c>CONCxxx:</c> prefix on it.
    /// </summary>
    internal string Detail { get; }
}
