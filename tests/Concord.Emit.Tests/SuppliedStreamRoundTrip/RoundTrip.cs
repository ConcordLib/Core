using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Emit;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Emit.Tests.SuppliedStreamRoundTrip;

public static class RoundTrip {
    public static MethodInfo ComposeThroughStream(MethodBase target, IReadOnlyList<Injection> ordered) {
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        TranspilerContext concrete = (TranspilerContext)context;
        List<CodeInstruction> source = Extract(concrete);

        // Extract() just set concrete.ExistingLocalCount from the SOURCE method's own locals as a
        // read-direction side effect of CecilCodeConverter.ToInstructions. TransformStream writes
        // source onto a brand new, empty wrapper (no locals physically present), so those locals
        // must instead be densely re-declared from a clean slate - the same contract a coexistence
        // bridge follows (see Concord.Harmony.CodeInstructionConverter.ToConcord). Resetting the
        // counter back to 0 before declaring is what makes DeclareLocal hand back indices 0..N-1
        // instead of continuing past the count ToInstructions already recorded.
        concrete.ExistingLocalCount = 0;
        StreamRoundTrip.DeclareReferencedLocals(source, concrete);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(target, source, ordered, context);
        return StreamRoundTrip.Generate(composed, target);
    }

    public static List<CodeInstruction> Extract(MethodBase target) {
        MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(target);
        return Extract(new TranspilerContext(resolved));
    }

    private static List<CodeInstruction> Extract(TranspilerContext context) {
        using DynamicMethodDefinition source = new DynamicMethodDefinition(context.Original);
        List<CodeInstruction> instructions = CecilCodeConverter.ToInstructions(source.Definition, context);
        NormalizeLocalMacros(instructions, source.Definition.Body);
        return instructions;
    }

    // A genuinely compiled method's IL favors the space-saving ldloc.0-3/stloc.0-3 macro opcodes,
    // which carry no operand at all - the local index is baked into the opcode identity, and there
    // is no way to recover the local's type from the instruction alone. CecilCodeConverter.ToInstructions
    // preserves that as-is, which round-trips fine only when writing back onto the SAME MethodDefinition
    // (its Variables collection is never cleared, so the implicit slot still exists). TransformStream
    // always writes onto a fresh, separate wrapper, so this stream must instead carry every local
    // explicitly - the general Ldloc/Stloc opcode plus a LocalRef operand - matching what every
    // hand-authored source in TransformStreamTests already does and what StreamRoundTrip.Generate's
    // own DeclareReferencedLocals helper already expects to find.
    private static void NormalizeLocalMacros(List<CodeInstruction> instructions, MethodBody body) {
        foreach (CodeInstruction instruction in instructions) {
            (int Index, bool IsLoad)? macro = LocalMacroSlot(instruction.opcode);
            if (macro is null) {
                continue;
            }

            Type localType = body.Variables[macro.Value.Index].VariableType.ResolveReflection() ?? typeof(object);
            instruction.opcode = macro.Value.IsLoad ? OpCodes.Ldloc : OpCodes.Stloc;
            instruction.operand = new LocalRef(macro.Value.Index, localType);
        }
    }

    private static (int Index, bool IsLoad)? LocalMacroSlot(OpCode opcode) {
        if (opcode == OpCodes.Ldloc_0) {
            return (0, true);
        }

        if (opcode == OpCodes.Ldloc_1) {
            return (1, true);
        }

        if (opcode == OpCodes.Ldloc_2) {
            return (2, true);
        }

        if (opcode == OpCodes.Ldloc_3) {
            return (3, true);
        }

        if (opcode == OpCodes.Stloc_0) {
            return (0, false);
        }

        if (opcode == OpCodes.Stloc_1) {
            return (1, false);
        }

        if (opcode == OpCodes.Stloc_2) {
            return (2, false);
        }

        if (opcode == OpCodes.Stloc_3) {
            return (3, false);
        }

        return null;
    }
}
