using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Concord.Emit;

/// <summary>
///     Bundles the per-call invariants that <see cref="BodyCopier.LowerWrapInstruction" /> needs alongside the
///     per-instruction source and the shared <see cref="LoweringContext" />.
/// </summary>
/// <param name="OperationArgIndex">Argument index of the injection method's operation parameter, or -1 if none.</param>
/// <param name="WrapEnd">The instruction the wrap injection's returns should branch to.</param>
/// <param name="ImportedOriginal">The original call site's method, imported into the destination module.</param>
/// <param name="ReceiverLocal">The local holding the spilled receiver of the wrapped call, or null if static.</param>
/// <param name="InvokeParameterTypes">The original call site's parameter types.</param>
/// <param name="OriginalOpCode">The opcode the original call site used to invoke the wrapped member.</param>
/// <param name="WrapArgBinding">Maps injection method argument indices to the locals holding the spilled call-site arguments.</param>
internal readonly record struct WrapLoweringSite(
    int OperationArgIndex,
    Instruction WrapEnd,
    MethodReference ImportedOriginal,
    VariableDefinition? ReceiverLocal,
    IReadOnlyList<Type> InvokeParameterTypes,
    OpCode OriginalOpCode,
    Dictionary<int, VariableDefinition> WrapArgBinding);
