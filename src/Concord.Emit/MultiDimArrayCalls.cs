using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Concord.Emit;

internal static class MultiDimArrayCalls {
    private const int SupportedRank = 2;

    internal static void Rewrite(MethodDefinition definition) {
        ModuleDefinition module = definition.Module;
        Mono.Collections.Generic.Collection<Instruction> instructions = definition.Body.Instructions;
        for (int i = 0; i < instructions.Count; i++) {
            Instruction instruction = instructions[i];
            if (instruction.Operand is not MethodReference reference
                || reference.DeclaringType is not ArrayType array
                || array.Rank != SupportedRank) {
                continue;
            }

            string? helper = HelperFor(instruction.OpCode, reference);
            if (helper is null) {
                continue;
            }

            instruction.OpCode = OpCodes.Call;
            instruction.Operand = Instantiate(module, helper, array.ElementType);
            if (i > 0 && instructions[i - 1].OpCode == OpCodes.Readonly) {
                instructions[i - 1].OpCode = OpCodes.Nop;
                instructions[i - 1].Operand = null;
            }
        }
    }

    private static string? HelperFor(OpCode opcode, MethodReference reference) {
        int parameters = reference.Parameters.Count;
        if (opcode == OpCodes.Newobj) {
            return reference.Name == ".ctor" && parameters == SupportedRank ? nameof(ArrayAccess.New) : null;
        }

        if (opcode != OpCodes.Call && opcode != OpCodes.Callvirt) {
            return null;
        }

        return reference.Name switch {
            "Get" when parameters == SupportedRank => nameof(ArrayAccess.Get),
            "Address" when parameters == SupportedRank => nameof(ArrayAccess.Address),
            "Set" when parameters == SupportedRank + 1 => nameof(ArrayAccess.Set),
            _ => null,
        };
    }

    private static GenericInstanceMethod Instantiate(ModuleDefinition module, string helper, TypeReference element) {
        MethodInfo definition = typeof(ArrayAccess).GetMethod(helper, BindingFlags.Public | BindingFlags.Static)
            ?? throw new ConcordEmitException(
                TokenizableOperands.UnencodableOperand,
                $"'{nameof(ArrayAccess)}.{helper}' is missing, so a multi-dimensional array access cannot be rewritten.");

        GenericInstanceMethod instance = new GenericInstanceMethod(module.ImportReference(definition));
        instance.GenericArguments.Add(module.ImportReference(element));
        return instance;
    }
}
