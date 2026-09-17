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
    ///     The patch owners that should run after this injection.
    /// </summary>
    public IReadOnlyList<string> BeforeOwners { get; set; } = [];

    /// <summary>
    ///     The patch owners that should run before this injection.
    /// </summary>
    public IReadOnlyList<string> AfterOwners { get; set; } = [];
}
