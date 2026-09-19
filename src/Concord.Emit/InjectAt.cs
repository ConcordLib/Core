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
    ///     a caught exception. Exceptions that escape the target do not. Early returns are not affected. Use
    ///     <see cref="Return" /> to target every return site.
    /// </summary>
    public sealed record Tail : InjectAt;

    /// <summary>
    ///     Wraps the entire target method. The injection method declares an <see cref="Operation" /> family
    ///     handle and calls <c>original.Invoke(args)</c> to run the original body. Omitting the call skips
    ///     the body. Operation-only (no <see cref="ControlHandle" />).
    /// </summary>
    public sealed record Around : InjectAt;

    /// <summary>
    ///     Runs the injection inside a synthesized <c>finally</c> that covers the head injections and the
    ///     target body, so it runs whether the target returns or throws. Cannot read or replace the return
    ///     value.
    /// </summary>
    public sealed record Finally : InjectAt;

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
    public sealed record Invoke(Type DeclaringType, string Method, At Shift, uint By = 0, Type[]? ParameterTypes = null, uint Arg = 0) : InjectAt {
        /// <summary>
        ///     Bounds matching and <c>By</c> counting to a range of the target body.
        /// </summary>
        public SliceRange? Slice { get; init; }
    }

    /// <summary>
    ///     Targets a <c>newobj</c> instruction inside the target body.
    /// </summary>
    /// <param name="ConstructedType">The type being constructed.</param>
    /// <param name="Shift">Where the injection method runs relative to the construction.</param>
    /// <param name="By">The 1-based occurrence to target, or <c>0</c> to target every match.</param>
    /// <param name="ParameterTypes">Restricts matches to one constructor overload by parameter types.</param>
    /// <param name="Arg">The 1-based constructor argument to rewrite for At.Argument, or 0 to infer by unique type match.</param>
    public sealed record NewObj(Type ConstructedType, At Shift, uint By = 0, Type[]? ParameterTypes = null, uint Arg = 0) : InjectAt {
        /// <summary>
        ///     Bounds matching and <c>By</c> counting to a range of the target body.
        /// </summary>
        public SliceRange? Slice { get; init; }
    }

    /// <summary>
    ///     Rewrites the target body's raw IL through an author-supplied transpiler method.
    /// </summary>
    /// <param name="Final">
    ///     <see langword="false" /> rewrites the original body before declarative injections compose onto it.
    ///     <see langword="true" /> rewrites the fully composed wrapper.
    /// </param>
    public sealed record Transpiler(bool Final = false) : InjectAt;

    /// <summary>
    ///     Targets a read or write of one of the target method's own local variables.
    /// </summary>
    /// <param name="LocalType">The declared type of the local to match.</param>
    /// <param name="Access">Whether to match writes to the local or reads of it.</param>
    /// <param name="By">The 1-based occurrence of the access to target, or <c>0</c> to target every match.</param>
    /// <param name="Ordinal">
    ///     The 1-based occurrence of <paramref name="LocalType" /> among the target's locals, in slot order,
    ///     or <c>0</c> to leave it unset.
    /// </param>
    /// <param name="Index">
    ///     A raw local slot in the target body, or <c>-1</c> to leave it unset. Precise and brittle: any
    ///     recompile of the target can move it.
    /// </param>
    /// <param name="Name">
    ///     The local's source name, read from a pdb when one is present, or <see langword="null" /> to
    ///     leave it unset. The least portable selector: see <see cref="LocalAttribute.Name" /> for the
    ///     hosts where no pdb can be found and for how a name can cover more than one slot.
    /// </param>
    public sealed record Local(Type LocalType, LocalAccess Access, uint By = 0, uint Ordinal = 0, int Index = -1, string? Name = null) : InjectAt {
        /// <summary>
        ///     Bounds matching and <c>By</c> counting to a range of the target body.
        /// </summary>
        public SliceRange? Slice { get; init; }
    }
}
