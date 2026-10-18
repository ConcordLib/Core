using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Concord.Emit;

internal static class HandlerEndTrimmer {
    internal static void Apply(MethodDefinition definition) {
        MethodBody body = definition.Body;
        foreach (ExceptionHandler handler in body.ExceptionHandlers) {
            if (handler.HandlerEnd is null) {
                continue;
            }

            string[] allowed = handler.HandlerType switch {
                ExceptionHandlerType.Finally => FlowTerminators.Finally,
                ExceptionHandlerType.Fault => FlowTerminators.Finally,
                ExceptionHandlerType.Filter => FlowTerminators.Catch,
                _ => FlowTerminators.Catch,
            };

            Instruction? trimmed = TrimBack(handler.HandlerStart, handler.HandlerEnd, allowed);
            if (trimmed is not null) {
                handler.HandlerEnd = trimmed;
            }
        }
    }

    private static Instruction? TrimBack(Instruction? start, Instruction end, string[] allowed) {
        Instruction? last = end.Previous;
        if (last is null || Array.IndexOf(allowed, last.OpCode.Name) >= 0) {
            return null;
        }

        Instruction? candidate = last;
        while (candidate is not null && candidate != start && candidate.OpCode == OpCodes.Nop) {
            Instruction? before = candidate.Previous;
            if (before is not null && Array.IndexOf(allowed, before.OpCode.Name) >= 0) {
                return candidate;
            }

            candidate = before;
        }

        return null;
    }
}
