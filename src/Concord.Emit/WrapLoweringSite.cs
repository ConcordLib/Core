using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Concord.Emit;

internal readonly record struct WrapLoweringSite(
    int OperationArgIndex,
    Instruction WrapEnd,
    MethodReference ImportedOriginal,
    VariableDefinition? ReceiverLocal,
    IReadOnlyList<Type> InvokeParameterTypes,
    OpCode OriginalOpCode,
    Dictionary<int, VariableDefinition> WrapArgBinding,
    Dictionary<int, VariableDefinition> BoundConstants);
