namespace Concord.Emit;

/// <summary>
///     Base type for supported injection positions inside a generated wrapper.
/// </summary>
public abstract record InjectAt {
    /// <summary>
    ///     Inserts the injection before the target method body.
    /// </summary>
    public sealed record Head : InjectAt;

    /// <summary>
    ///     Inserts the injection before each <c>return</c> in the target body, reading and replacing the
    ///     value returned at that site.
    /// </summary>
    /// <param name="By">
    ///     The 1-based occurrence of the return to target, or <c>0</c> to target every return.
    /// </param>
    public sealed record Return(uint By = 0) : InjectAt;

    /// <summary>
    ///     Inserts the injection before the last <c>return</c> in the target body (Mixin <c>@At("TAIL")</c>
    ///     semantics). A protected-region <c>leave</c> that reaches that return runs the injection, including after
    ///     a caught exception. Exceptions that escape the target do not. Early returns are not affected; use
    ///     <see cref="Return" /> to target every return site.
    /// </summary>
    public sealed record Tail : InjectAt;

    /// <summary>
    ///     Wraps the entire target method. The injection method declares an <see cref="Operation" /> family
    ///     handle and calls <c>original.Invoke(args)</c> to run the original body; omitting the call skips
    ///     the body. Operation-only (no <see cref="ControlHandle" />).
    /// </summary>
    public sealed record Around : InjectAt;

    /// <summary>
    ///     Targets an inlined literal constant in the target body.
    /// </summary>
    /// <param name="Value">The literal to match. Supported kinds: int, long, float, double, string.</param>
    /// <param name="By">The 1-based occurrence to target, or <c>0</c> to target every match.</param>
    public sealed record Constant(object Value, uint By = 0) : InjectAt;

    /// <summary>
    ///     Targets a named method or property call inside the target body. <see cref="At.Head" /> and
    ///     <see cref="At.Tail" /> also target field reads.
    /// </summary>
    /// <param name="DeclaringType">The declaring type of the member access to match.</param>
    /// <param name="Method">The method, property, or field name to match.</param>
    /// <param name="Shift">
    ///     Where the injection method runs relative to the matched access: <see cref="At.Head" /> before it,
    ///     <see cref="At.Tail" /> after it, or <see cref="At.Around" /> wrapping a method or property call.
    ///     Field reads support Head and Tail only.
    /// </param>
    /// <param name="By">
    ///     The 1-based occurrence of the matched access to target, or <c>0</c> to target every matching access.
    /// </param>
    /// <param name="ParameterTypes">
    ///     For method or property calls, restricts matches by parameter types. Leave this <see langword="null" />
    ///     or empty when matching a field read.
    /// </param>
    /// <param name="Arg">The 1-based argument to rewrite for At.Argument, or 0 to infer by unique type match.</param>
    public sealed record Invoke(Type DeclaringType, string Method, At Shift, uint By = 0, Type[]? ParameterTypes = null, uint Arg = 0) : InjectAt;
}
