using Mono.Cecil.Cil;

namespace Concord.Emit;

internal readonly record struct DispatchState(
    bool HasHead,
    Injection? AroundInjection,
    Instruction? LastExit);
