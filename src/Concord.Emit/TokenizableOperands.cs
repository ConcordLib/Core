using System.Reflection;
using DynamicMethod = System.Reflection.Emit.DynamicMethod;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace Concord.Emit;

/// <summary>
///     Refuses a composed body whose operands, local types or catch types have no metadata token in an
///     arbitrary target module, which a body-replacement backend could never encode.
/// </summary>
internal static class TokenizableOperands {
    internal const string UnencodableOperand = "CONC157";

    private const int MaxNesting = 32;

    private const string Tail = "A body-replacement backend has no way to write that as a metadata token, so this target " +
        "keeps the detour backend.";

    /// <summary>
    ///     Reports the first type in the body that cannot be written as a metadata token. Covers all three
    ///     token-bearing channels: instruction operands, local signatures, and catch types.
    /// </summary>
    /// <param name="body">The composed body.</param>
    /// <param name="target">The method the body was composed for, named in the message.</param>
    /// <returns>A diagnostic message, or <see langword="null" /> when the whole body is encodable.</returns>
    internal static string? Reject(ComposedBody body, MethodBase target) {
        for (int i = 0; i < body.Locals.Count; i++) {
            string? reason = DescribeType(body.Locals[i].Type, 0);
            if (reason is not null) {
                return $"{UnencodableOperand}: composed body for '{Name(target)}' declares local {i} that {reason}. " + Tail;
            }
        }

        for (int i = 0; i < body.Clauses.Count; i++) {
            string? reason = DescribeType(body.Clauses[i].CatchType, 0);
            if (reason is not null) {
                return $"{UnencodableOperand}: composed body for '{Name(target)}' catches a type in clause {i} that {reason}. " + Tail;
            }
        }

        List<CodeInstruction> stream = new List<CodeInstruction>(body.Instructions.Count);
        foreach (ComposedInstruction instruction in body.Instructions) {
            stream.Add(instruction.Instruction);
        }

        return Reject(stream, target);
    }

    /// <summary>
    ///     Reports the first operand in the stream that cannot be written as a metadata token.
    /// </summary>
    /// <param name="instructions">The composed instruction stream.</param>
    /// <param name="target">The method the stream was composed for, named in the message.</param>
    /// <returns>A diagnostic message, or <see langword="null" /> when every operand is encodable.</returns>
    internal static string? Reject(IReadOnlyList<CodeInstruction> instructions, MethodBase target) {
        for (int i = 0; i < instructions.Count; i++) {
            string? reason = Describe(instructions[i]);
            if (reason is not null) {
                return $"{UnencodableOperand}: composed body for '{Name(target)}' {reason} at instruction {i}. " + Tail;
            }
        }

        return null;
    }

    private static string? Describe(CodeInstruction instruction) {
        if (instruction.opcode == OpCodes.Jmp) {
            return "uses 'jmp', which no encoder accepts";
        }

        object? operand = instruction.operand;
        if (operand is null || operand is Label || operand is Label[] || operand is LocalRef || IsInlineValue(operand)) {
            return null;
        }

        if (operand is CallSiteSignature) {
            return "uses a 'calli' call-site signature, which is a standalone signature rather than a member";
        }

        if (operand is MethodBase method) {
            if ((method.CallingConvention & CallingConventions.VarArgs) != 0) {
                return $"calls the vararg method '{Name(method)}', whose call site is a standalone signature rather than a member";
            }

            if (method is DynamicMethod) {
                return $"references the dynamic method '{Name(method)}'";
            }

            return DescribeMember(method);
        }

        if (operand is FieldInfo field) {
            return DescribeMember(field);
        }

        if (operand is Type type) {
            return DescribeType(type, 0);
        }

        return $"carries an operand of type '{operand.GetType().Name}', which is not a member, a label, a local, or an inline value";
    }

    private static bool IsInlineValue(object operand) {
        return operand is string
            || operand is sbyte
            || operand is byte
            || operand is int
            || operand is long
            || operand is float
            || operand is double;
    }

    // A member's own module is not enough. List<T> instantiated over a TypeBuilder type still
    // reports corelib, so the dynamic assembly only shows up in the generic arguments.
    private static string? DescribeMember(MemberInfo member) {
        if (member.Module.Assembly.IsDynamic) {
            return $"references '{Name(member)}' from a dynamic assembly";
        }

        string? declaring = DescribeType(member.DeclaringType, 0);
        if (declaring is not null) {
            return declaring;
        }

        if (member is MethodBase { IsGenericMethod: true } generic) {
            foreach (Type argument in generic.GetGenericArguments()) {
                string? reason = DescribeType(argument, 0);
                if (reason is not null) {
                    return reason;
                }
            }
        }

        return null;
    }

    // Depth-capped rather than cycle-tracked: refusing is the safe answer, and a stream that nests
    // this deep keeps the detour backend instead of being mistaken for encodable.
    private static string? DescribeType(Type? type, int depth) {
        if (type is null) {
            return null;
        }

        if (depth > MaxNesting) {
            return $"nests '{Name(type)}' deeper than the operand check walks, so no part of it can be shown encodable";
        }

        if (type.Assembly.IsDynamic) {
            return $"references the type '{Name(type)}' from a dynamic assembly";
        }

        if (type.HasElementType) {
            string? element = DescribeType(type.GetElementType(), depth + 1);
            if (element is not null) {
                return element;
            }
        }

        foreach (Type argument in type.GetGenericArguments()) {
            string? reason = DescribeType(argument, depth + 1);
            if (reason is not null) {
                return reason;
            }
        }

        return null;
    }

    private static string Name(MemberInfo member) {
        if (member is Type type) {
            return type.FullName ?? type.Name;
        }

        return member.DeclaringType is null ? member.Name : member.DeclaringType.Name + "." + member.Name;
    }
}
