using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Wrapper locals used to lower <see cref="ControlHandle" /> and <see cref="ControlHandle{T}" /> operations.
/// </summary>
/// <param name="Cancel">Boolean local that records whether the original target body should be skipped.</param>
/// <param name="HasReturn">Boolean local that records whether an explicit return value was supplied.</param>
/// <param name="ReturnValue">Local that stores the wrapper return value for non-void targets.</param>
/// <param name="SpliceValue">Local that carries the original body's result across an Around splice for non-void targets.</param>
/// <param name="CtorBodyRan">Boolean local set the first time a constructor Around's spliced body copy runs.</param>
/// <param name="CtorBodyRanTwice">Boolean local set when a second entry into any constructor Around body copy is blocked.</param>
/// <param name="State">One state local per patch declaration type that uses a state slot on this target.</param>
internal sealed record ProtocolLocals(
    VariableDefinition Cancel,
    VariableDefinition? HasReturn,
    VariableDefinition? ReturnValue,
    VariableDefinition? SpliceValue = null,
    VariableDefinition? CtorBodyRan = null,
    VariableDefinition? CtorBodyRanTwice = null,
    IReadOnlyDictionary<Type, VariableDefinition>? State = null) {
    /// <summary>
    ///     How many of the wrapper's locals came from the raw target body. Locals at or past this
    ///     index were added by a transpiler.
    /// </summary>
    public int RawLocalCount { get; init; }

    /// <summary>
    ///     How many of the wrapper's leading locals a <see cref="LocalAttribute" /> may select from:
    ///     the target's own locals plus any a transpiler added, and nothing Concord declared after.
    /// </summary>
    public int SearchLocalCount { get; init; }

    /// <summary>
    ///     Indices of the wrapper locals some instruction in the post-transpiler spine names. A slot
    ///     outside this set has no live use, so binding it would read zero forever.
    /// </summary>
    public HashSet<int> ReferencedSlots { get; init; } = [];
}
