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
    ///     Inserts the injection after the target method body and before the wrapper returns.
    /// </summary>
    public sealed record Tail : InjectAt;

    /// <summary>
    ///     Uses the injection method body's call to the target method as the splice point for the original body.
    /// </summary>
    public sealed record Around : InjectAt;

    /// <summary>
    ///     Targets a call to a named method inside the target body.
    /// </summary>
    /// <param name="DeclaringType">The declaring type of the call-site method to match.</param>
    /// <param name="Method">The call-site method name to match.</param>
    /// <param name="Shift">
    ///     Where the injection method runs relative to the matched call: <see cref="At.Head" /> before the
    ///     call, <see cref="At.Tail" /> after the call, or <see cref="At.Around" /> wrapping
    ///     the call (the injection method receives an <see cref="Operation{T}" /> handle).
    /// </param>
    /// <param name="By">
    ///     The 1-based occurrence of the matched call to target, or <c>0</c> to target every matching call.
    /// </param>
    /// <param name="ParameterTypes">
    ///     When non-<see langword="null" />, only call sites whose parameter types match this array are
    ///     considered, allowing overloaded call-site methods to be disambiguated.
    /// </param>
    public sealed record Invoke(Type DeclaringType, string Method, At Shift, uint By = 0, Type[]? ParameterTypes = null) : InjectAt;
}
