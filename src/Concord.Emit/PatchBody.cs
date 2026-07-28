namespace Concord;

// This enum is APPEND-ONLY for the same reason At is: InjectedMemberAnalyzer compares raw ordinals.

/// <summary>
///     Selects which body an injection attaches to when the target is an <see langword="async" /> or
///     iterator method. The C# compiler splits those into two methods: the declared one, which just
///     builds and returns the state machine, and the state machine's <c>MoveNext</c>, which holds the
///     body that was written.
/// </summary>
public enum PatchBody {
    /// <summary>
    ///     Patch the method as declared (default). For an async or iterator target this is the stub the
    ///     compiler emits: it runs once per call and returns the <c>Task</c> or <c>IEnumerable</c>, so a
    ///     <see cref="At.Return" /> injection reads and replaces that value. It contains none of the
    ///     body that was written, so <see cref="At.Around" />, <see cref="At.Transpiler" />,
    ///     <see cref="At.Constant" /> and <see cref="At.Argument" /> have nothing to act on there.
    /// </summary>
    Declared,

    /// <summary>
    ///     Patch the generated state machine's <c>MoveNext</c>, where the body that was written lives.
    ///     It runs once per step rather than once per call, and it returns <see cref="bool" /> - not the
    ///     declared return type - so a <see cref="At.Return" /> injection here sees a step flag.
    /// </summary>
    StateMachine,
}
