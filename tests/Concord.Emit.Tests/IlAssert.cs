using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Concord.Emit.Tests;

internal static class IlAssert {
    internal static void BodiesMatch(MethodDefinition expected, MethodDefinition actual) {
        Assert.Equal(Format(expected), Format(actual));
    }

    internal static string Format(MethodDefinition definition) {
        StringBuilder builder = new StringBuilder();
        MethodBody body = definition.Body;

        builder.Append("initlocals ").Append(body.InitLocals).AppendLine();
        foreach (VariableDefinition variable in body.Variables) {
            builder.Append("  local ").Append(variable.Index).Append(' ').Append(variable.VariableType.FullName);
            if (variable.IsPinned) {
                builder.Append(" pinned");
            }

            builder.AppendLine();
        }

        Dictionary<Instruction, int> offsets = new Dictionary<Instruction, int>();
        for (int i = 0; i < body.Instructions.Count; i++) {
            offsets[body.Instructions[i]] = i;
        }

        for (int i = 0; i < body.Instructions.Count; i++) {
            Instruction instruction = body.Instructions[i];
            builder.Append("  ").Append(i.ToString("D4")).Append(' ').Append(instruction.OpCode.Name);
            builder.Append(' ').Append(FormatOperand(instruction.Operand, offsets));
            builder.AppendLine();
        }

        foreach (ExceptionHandler handler in body.ExceptionHandlers) {
            builder.Append("  handler ").Append(handler.HandlerType);
            builder.Append(" try=").Append(Index(handler.TryStart, offsets)).Append('-').Append(Index(handler.TryEnd, offsets));
            builder.Append(" handler=").Append(Index(handler.HandlerStart, offsets)).Append('-').Append(Index(handler.HandlerEnd, offsets));
            builder.Append(" filter=").Append(Index(handler.FilterStart, offsets));
            builder.Append(" catch=").Append(handler.CatchType?.FullName ?? "-");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatOperand(object? operand, Dictionary<Instruction, int> offsets) {
        if (operand is null) {
            return "<null>";
        }

        if (operand is Instruction target) {
            return "@" + Index(target, offsets);
        }

        if (operand is Instruction[] targets) {
            StringBuilder builder = new StringBuilder("[");
            for (int i = 0; i < targets.Length; i++) {
                if (i > 0) {
                    builder.Append(',');
                }

                builder.Append('@').Append(Index(targets[i], offsets));
            }

            return builder.Append(']').ToString();
        }

        if (operand is VariableDefinition variable) {
            return "V_" + variable.Index;
        }

        if (operand is ParameterDefinition parameter) {
            return "A_" + parameter.Index;
        }

        if (operand is string stringLiteral) {
            return "\"" + stringLiteral.Replace("\"", "\\\"") + "\"";
        }

        if (operand is CallSite callSite) {
            StringBuilder builder = new StringBuilder("calli(");
            builder.Append("cc=").Append(callSite.CallingConvention);
            builder.Append(",hasThis=").Append(callSite.HasThis);
            builder.Append(",explicitThis=").Append(callSite.ExplicitThis);
            builder.Append(",ret=").Append(callSite.ReturnType.FullName);
            for (int i = 0; i < callSite.Parameters.Count; i++) {
                builder.Append(",p").Append(i).Append('=').Append(callSite.Parameters[i].ParameterType.FullName);
            }

            return builder.Append(')').ToString();
        }

        return operand.ToString() ?? "-";
    }

    private static string Index(Instruction? instruction, Dictionary<Instruction, int> offsets) {
        if (instruction is null) {
            return "end";
        }

        if (!offsets.TryGetValue(instruction, out int index)) {
            throw new InvalidOperationException($"Dangling instruction reference: {instruction.OpCode.Name}");
        }

        return index.ToString("D4");
    }
}
