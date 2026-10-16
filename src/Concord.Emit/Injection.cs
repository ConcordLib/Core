using System;
using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Describes one injection method that should be copied into a generated wrapper.
/// </summary>
/// <param name="InjectionMethod">The injection method whose body supplies the injected IL.</param>
/// <param name="At">The position or call site where the injection method body is inserted.</param>
/// <param name="Owner">The stable owner id used for patch grouping and unpatching.</param>
/// <param name="Priority">The ordering priority for this injection relative to other owners.</param>
public sealed record Injection(MethodBase InjectionMethod, InjectAt At, string Owner, int Priority) {
    /// <summary>
    ///     Gets whether applying this injection writes the composed wrapper IL to the desktop debug log.
    /// </summary>
    public bool Debug { get; set; }

    /// <summary>
    ///     Which body this injection attaches to when the target is an async or iterator method.
    ///     Defaults to <see cref="PatchBody.Declared" />.
    /// </summary>
    public PatchBody Body { get; set; } = PatchBody.Declared;

    /// <summary>
    ///     Values bound to this injection's <see cref="BoundAttribute" /> parameters, keyed by parameter
    ///     name. Each one is emitted as a literal into the composed wrapper, so a single injection method
    ///     can carry a different value for every target it is registered on.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? BoundArguments { get; set; }

    /// <summary>
    ///     The constructed generic type this injection was requested for, when the target's compiled body
    ///     is shared across reference-type instantiations. Composition emits a receiver check in front of
    ///     this injection's body so it runs only for that instantiation. Null on every other target.
    /// </summary>
    public Type? RequestedInstantiation { get; set; }

    /// <summary>
    ///     Whether a foreign patch host owns this injection instead of Concord. Foreign injections keep
    ///     the host's own ordering and always run outside Concord's.
    /// </summary>
    public bool ForeignOrigin { get; set; }

    /// <summary>
    ///     The parameter index that reads the target's return value, or null when there is none. A by-ref
    ///     parameter writes it too. Only a return-position injection may set it.
    /// </summary>
    public int? ResultArgIndex { get; set; }

    /// <summary>
    ///     The parameter index that reads the target instance, or null when there is none. Only valid on
    ///     an injection whose target is an instance method.
    /// </summary>
    public int? InstanceArgIndex { get; set; }

    /// <summary>
    ///     The parameter index that reads whether the target body still runs, or null when there is none.
    ///     It reads the wrapper's cancel flag inverted.
    /// </summary>
    public int? RunOriginalArgIndex { get; set; }

    /// <summary>
    ///     The parameter index that reads every target argument boxed into an object array, or null when
    ///     there is none. The array is built fresh for the injection and never written back.
    /// </summary>
    public int? ArgsArgIndex { get; set; }

    /// <summary>
    ///     Maps a parameter index to the field on the target's declaring type it reads. A by-ref parameter
    ///     writes it too.
    /// </summary>
    public IReadOnlyDictionary<int, FieldInfo>? FieldArgs { get; set; }

    /// <summary>
    ///     Parameter indexes the body writes but the target passes by value, so the write lands in a scratch
    ///     local instead of the wrapper's own argument.
    /// </summary>
    public IReadOnlyCollection<int>? ScratchArgs { get; set; }

    /// <summary>
    ///     Parameter indexes declared by-ref against a target argument the target passes by value, so they
    ///     bind to the address of the wrapper's own argument and a write reaches the target body.
    /// </summary>
    public IReadOnlyCollection<int>? AddressArgs { get; set; }

    /// <summary>
    ///     Parameter index bound to the shared state slot this injection's declaring type owns on the target.
    /// </summary>
    public int? StateArgIndex { get; set; }

    /// <summary>
    ///     Type of the shared state slot <see cref="StateArgIndex" /> binds to.
    /// </summary>
    public Type? StateType { get; set; }

    /// <summary>
    ///     Parameter index bound to the patched method itself, which the wrapper loads from a handle table.
    /// </summary>
    public int? OriginalMethodArgIndex { get; set; }

    /// <summary>
    ///     Patch declaration whose state slot this injection reads, when it is not the injection's own.
    /// </summary>
    public Type? ForeignStateOwner { get; set; }

    /// <summary>
    ///     Type of the state slot named by <see cref="ForeignStateOwner" />.
    /// </summary>
    public Type? ForeignStateType { get; set; }

    /// <summary>
    ///     Parameter index that receives the foreign state slot, boxed. Bound to null when there is no owner.
    /// </summary>
    public int? ForeignStateArgIndex { get; set; }

    /// <summary>
    ///     Whether this injection returns the target's new return value, so its return stores into the
    ///     wrapper's return-value local.
    /// </summary>
    public bool ReturnsIntoResult { get; set; }

    /// <summary>
    ///     Whether this injection returns a bool that cancels the target when it is false, the shape
    ///     Harmony gives a prefix.
    /// </summary>
    public bool CancelsOnFalse { get; set; }

    /// <summary>
    ///     Whether a cancelling head injection ahead of this one skips it. Null works the answer out
    ///     from the injection's own signature.
    /// </summary>
    public bool? AffectsOriginal { get; set; }

    /// <summary>
    ///     Whether this injection also runs on the landing a cancelling head injection branches to,
    ///     which a Concord-owned injection is otherwise skipped on.
    /// </summary>
    public bool ReplayOnCancel { get; set; }

    /// <summary>
    ///     Whether this injection runs after every foreign injection on the target, whichever side of
    ///     Concord the position normally puts them on.
    /// </summary>
    public bool RunsAfterForeign { get; set; }

    /// <summary>
    ///     The patch owners that should run after this injection.
    /// </summary>
    public IReadOnlyList<string> BeforeOwners { get; set; } = [];

    /// <summary>
    ///     The patch owners that should run before this injection.
    /// </summary>
    public IReadOnlyList<string> AfterOwners { get; set; } = [];
}
