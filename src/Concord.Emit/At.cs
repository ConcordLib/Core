namespace Concord;

// This enum is APPEND-ONLY. The analyzer compares raw At ordinals in InjectedMemberAnalyzer
// (lines 847, 851, 860, 880, 883, 969, 987, 1263, 1290, 1897, 2100). Reordering or inserting
// silently breaks static analysis. Only ever add new values at the end.

/// <summary>
///     Public injection-position values for <see cref="InjectAttribute" /> and the fluent <c>PatchBuilder</c>.
///     Maps to a <see cref="Concord.Emit.InjectAt" /> for composition.
/// </summary>
public enum At {
    /// <summary>Run the injection method at the head of the target (default).</summary>
    Head,

    /// <summary>
    ///     Run the injection method before each <c>return</c> in the target body (or a single chosen return),
    ///     reading and replacing the value returned at that site.
    /// </summary>
    Return,

    /// <summary>
    ///     Run the injection method once at the end of the target, just before the wrapper returns. This includes
    ///     completion after a caught exception, but not an exception that escapes the target.
    /// </summary>
    Tail,

    /// <summary>Wrap the whole target body.</summary>
    Around,

    /// <summary>Replace an inlined literal constant in the target body with the injection method's return value.</summary>
    Constant,

    /// <summary>Rewrite one argument of a matched call inside the target body through the injection method.</summary>
    Argument,

    /// <summary>
    ///     Rewrite the target's original IL directly, before Concord composes any declarative injection
    ///     onto it. The injection method must be <see langword="static" /> and takes and returns
    ///     <c>IEnumerable&lt;CodeInstruction&gt;</c>, optionally with an
    ///     <see cref="ITranspilerContext" /> second parameter.
    /// </summary>
    /// <remarks>
    ///     A transpiler must be a pure function of its input body. Concord recomposes a target from its
    ///     raw IL on every patch and every unpatch of that target, including when an unrelated mod
    ///     unpatches it, so a transpiler runs an unpredictable number of times.
    ///     <para>
    ///         Adding or removing a <c>ret</c>, an inlined literal, or a call shifts the occurrences that
    ///         other mods' <c>At.Return(By:)</c>, <c>At.Constant(By:)</c> and <c>At.Invoke(By:)</c>
    ///         injections select against. Concord does not detect this and reports no diagnostic; the
    ///         failure surfaces in the other mod.
    ///     </para>
    ///     <para>
    ///         Order between transpilers from different mods is not currently stable. Set an explicit
    ///         <c>Priority</c> when order matters.
    ///     </para>
    /// </remarks>
    Transpiler,

    /// <summary>
    ///     Rewrite the fully composed wrapper IL, after every declarative injection is spliced in.
    /// </summary>
    /// <remarks>
    ///     No stability guarantee. The composed body's shape is a Concord implementation detail and may
    ///     change in ANY release, including patch releases. The body you receive contains Concord's
    ///     protocol locals appended after the target's own, every original <c>ret</c> rewritten into a
    ///     synthesized epilogue, and a cancel gate between head injections and the target body. Every
    ///     caveat on <see cref="Transpiler" /> applies here too.
    /// </remarks>
    TranspilerFinal,
}
