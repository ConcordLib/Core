using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

internal static class ComposedBodyFactory {
    private const string ConversionFailed = "CONC118";

    internal static ComposedBody Create(MethodDefinition definition, TranspilerContext context) {
        MethodBody body = definition.Body;
        List<CodeInstruction> instructions = CecilCodeConverter.ToInstructions(definition, context);

        Dictionary<Instruction, int> indexOf = new Dictionary<Instruction, int>(body.Instructions.Count);
        int next = 0;
        foreach (Instruction instruction in body.Instructions) {
            indexOf[instruction] = next++;
        }

        return new ComposedBody(
            Pair(instructions),
            Locals(body),
            Clauses(body, indexOf, instructions.Count),
            body.InitLocals);
    }

    // Both channels are read off the one instruction object, so neither can drift out of step with
    // the other. -1 is the internal no-origin marker and null is the contract's.
    private static IReadOnlyList<ComposedInstruction> Pair(List<CodeInstruction> instructions) {
        List<ComposedInstruction> composed = new List<ComposedInstruction>(instructions.Count);
        for (int i = 0; i < instructions.Count; i++) {
            int offset = InstructionOrigins.OffsetOf(instructions[i]);
            MethodBase? injection = InstructionOrigins.InjectionOf(instructions[i]);
            if (offset != InstructionOrigins.None && injection is not null) {
                throw new ConcordEmitException(ConversionFailed, $"Instruction {i} claims both target offset {offset} and injection '{injection.DeclaringType?.Name}.{injection.Name}'. A copier recorded provenance twice, so neither answer can be trusted.");
            }

            composed.Add(new ComposedInstruction(instructions[i], offset == InstructionOrigins.None ? null : (int?)offset, injection));
        }

        return composed;
    }

    private static IReadOnlyList<ComposedLocal> Locals(MethodBody body) {
        List<ComposedLocal> locals = new List<ComposedLocal>(body.Variables.Count);
        foreach (VariableDefinition variable in body.Variables) {
            locals.Add(new ComposedLocal(ResolveType(Unwrap(variable.VariableType)), variable.IsPinned));
        }

        return locals;
    }

    // Pinned and modopt/modreq are TypeSpecifications with no System.Type to land on; IsPinned
    // already carries the pin.
    private static TypeReference Unwrap(TypeReference type) {
        while (true) {
            switch (type) {
                case PinnedType pinned:
                    type = pinned.ElementType;
                    continue;
                case IModifierType modifier:
                    type = modifier.ElementType;
                    continue;
                default:
                    return type;
            }
        }
    }

    private static IReadOnlyList<ComposedClause> Clauses(MethodBody body, Dictionary<Instruction, int> indexOf, int count) {
        List<ComposedClause> clauses = new List<ComposedClause>(body.ExceptionHandlers.Count);
        foreach (ExceptionHandler handler in body.ExceptionHandlers) {
            ClauseKind kind = Kind(handler);
            clauses.Add(new ComposedClause(
                kind,
                At(handler.TryStart, indexOf, "TryStart"),
                End(handler.TryEnd, indexOf, count, "TryEnd"),
                At(handler.HandlerStart, indexOf, "HandlerStart"),
                End(handler.HandlerEnd, indexOf, count, "HandlerEnd"),
                kind == ClauseKind.Catch && handler.CatchType is not null ? ResolveType(handler.CatchType) : null,
                kind == ClauseKind.Filter ? At(handler.FilterStart, indexOf, "FilterStart") : -1));
        }

        return clauses;
    }

    private static ClauseKind Kind(ExceptionHandler handler) {
        return handler.HandlerType switch {
            ExceptionHandlerType.Catch => ClauseKind.Catch,
            ExceptionHandlerType.Filter => ClauseKind.Filter,
            ExceptionHandlerType.Finally => ClauseKind.Finally,
            ExceptionHandlerType.Fault => ClauseKind.Fault,
            _ => throw new ConcordEmitException(ConversionFailed, $"Exception region kind '{handler.HandlerType}' has no ComposedClause equivalent."),
        };
    }

    private static int At(Instruction? boundary, Dictionary<Instruction, int> indexOf, string name) {
        if (boundary is null) {
            throw new ConcordEmitException(ConversionFailed, $"An exception region names no {name}. A composed body cannot describe a region with an open start.");
        }

        return Resolve(boundary, indexOf, name);
    }

    // Cecil leaves an end null when the region runs to the literal end of the body; the contract
    // spells that as the instruction count, and anything else truncates the region by one.
    private static int End(Instruction? boundary, Dictionary<Instruction, int> indexOf, int count, string name) {
        return boundary is null ? count : Resolve(boundary, indexOf, name);
    }

    private static int Resolve(Instruction boundary, Dictionary<Instruction, int> indexOf, string name) {
        if (indexOf.TryGetValue(boundary, out int at)) {
            return at;
        }

        throw new ConcordEmitException(ConversionFailed, $"An exception region's {name} names an instruction that is not in the body being composed.");
    }

    private static Type ResolveType(TypeReference type) {
        return type.ResolveReflection() ?? throw new ConcordEmitException(ConversionFailed, $"Could not resolve CLR type for '{type.FullName}'. Pass a typeof(...) the mod can reference, and make sure its assembly is loaded before the patch is applied.");
    }
}
