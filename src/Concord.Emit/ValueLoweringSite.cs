using Mono.Cecil.Cil;

namespace Concord.Emit;

internal readonly record struct ValueLoweringSite(
    int ValueArgIndex,
    VariableDefinition ValueLocal,
    VariableDefinition ResultLocal,
    Instruction SpliceEnd,
    IReadOnlyDictionary<int, VariableDefinition>? LocalBinding,
    LocalHandleLowering? LocalHandles);
