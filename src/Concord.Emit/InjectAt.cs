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
    ///     semantics). Early returns are not affected; use <see cref="Return" /> to target every return site.
    /// </summary>
    public sealed record Tail : InjectAt;

    /// <summary>
    ///     Uses the injection method body's call to the target method as the splice point for the original body.
    /// </summary>
    public sealed record Around : InjectAt;

    /// <summary>
    ///     Targets an inlined literal constant in the target body.
    /// </summary>
    /// <param name="Value">The literal to match. Supported kinds: int, long, float, double, string.</param>
    /// <param name="By">The 1-based occurrence to target, or <c>0</c> to target every match.</param>
    public sealed record Constant(object Value, uint By = 0) : InjectAt;

    /// <summary>
    ///     Targets a call to a named method inside the target body.
    /// </summary>
    /// <param name="DeclaringType">The declaring type of the call-site method to match.</param>
    /// <param name="Method">The call-site method name to match.</param>
    /// <param name="Shift">
    ///     Where the injection method runs relative to the matched call: <see cref="At.Head" /> before the
    ///     call, <see cref="At.Tail" /> after the call, or <see cref="At.Around" /> wrapping
    ///     the call (the injection method receives an <see cref="Operation" /> family handle matching the call's shape).
    /// </param>
    /// <param name="By">
    ///     The 1-based occurrence of the matched call to target, or <c>0</c> to target every matching call.
    /// </param>
    /// <param name="ParameterTypes">
    ///     When non-<see langword="null" />, only call sites whose parameter types match this array are
    ///     considered, allowing overloaded call-site methods to be disambiguated.
    /// </param>
    /// <param name="Arg">The 1-based argument to rewrite for At.Argument, or 0 to infer by unique type match.</param>
    public sealed record Invoke(Type DeclaringType, string Method, At Shift, uint By = 0, Type[]? ParameterTypes = null, uint Arg = 0) : InjectAt;
}
