using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Wrapper locals used to lower <see cref="ControlHandle" /> and <see cref="ControlHandle{T}" /> operations.
/// </summary>
/// <param name="Cancel">Boolean local that records whether the original target body should be skipped.</param>
/// <param name="HasReturn">Boolean local that records whether an explicit return value was supplied.</param>
/// <param name="ReturnValue">Local that stores the wrapper return value for non-void targets.</param>
/// <param name="SpliceValue">Local that carries the original body's result across an Around splice for non-void targets.</param>
internal sealed record ProtocolLocals(
    VariableDefinition Cancel,
    VariableDefinition? HasReturn,
    VariableDefinition? ReturnValue,
    VariableDefinition? SpliceValue = null);
