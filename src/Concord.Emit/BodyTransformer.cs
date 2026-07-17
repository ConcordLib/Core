using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Concord.Emit;

/// <summary>
///     Composes Concord injections onto a body the caller supplies, rather than the target
///     method's own IL. This lets a caller compose against a body it got from somewhere else,
///     such as an instruction stream handed in by another patching library.
/// </summary>
public static class BodyTransformer {
    /// <summary>Extracts the canonical target's current IL as a neutral body.</summary>
    /// <param name="target">The method to read the current IL from.</param>
    /// <returns>A neutral body representing <paramref name="target" />'s current IL.</returns>
    public static NeutralBody FromMethod(MethodBase target) {
        MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(target);
        using DynamicMethodDefinition source = new DynamicMethodDefinition(resolved);
        return CecilToNeutralConverter.Convert(source.Definition);
    }

    /// <summary>Composes ordered injections onto a supplied body and returns the composed neutral body.</summary>
    /// <param name="canonicalTarget">The method that defines the composed body's shape (return type and parameters).</param>
    /// <param name="source">The neutral body to compose the injections onto.</param>
    /// <param name="ordered">The injections to compose, in application order.</param>
    /// <returns>A new neutral body containing <paramref name="source" /> with <paramref name="ordered" /> composed in.</returns>
    public static NeutralBody Transform(MethodBase canonicalTarget, NeutralBody source, IReadOnlyList<Injection> ordered) {
        MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(canonicalTarget);
        Type returnType = WrapperComposer.ResolveReturnType(resolved);
        Type[] parameterTypes = WrapperComposer.ResolveParameterTypes(resolved);
        using DynamicMethodDefinition wrapper = new DynamicMethodDefinition(WrapperComposer.WrapperName(resolved), returnType, parameterTypes);
        Dictionary<Instruction, List<int>> provenance = NeutralToCecilConverter.Populate(source, wrapper.Definition);
        WrapperComposer.AssembleInto(wrapper.Definition, resolved, ordered, returnType);
        return CecilToNeutralConverter.Convert(wrapper.Definition, provenance, NextFreshLabelId(source));
    }

    private static int NextFreshLabelId(NeutralBody source) {
        int max = -1;
        foreach (NeutralInstruction instruction in source.Instructions) {
            foreach (int labelId in instruction.Labels) {
                max = Math.Max(max, labelId);
            }

            if (instruction.Operand.Kind == NeutralOperandKind.Label) {
                max = Math.Max(max, instruction.Operand.AsLabelId());
            } else if (instruction.Operand.Kind == NeutralOperandKind.SwitchLabels) {
                foreach (int labelId in instruction.Operand.AsSwitchLabelIds()) {
                    max = Math.Max(max, labelId);
                }
            }
        }

        foreach (NeutralRegionEvent regionEvent in source.RegionEvents) {
            max = Math.Max(max, regionEvent.PositionLabelId);
        }

        return max + 1;
    }
}
