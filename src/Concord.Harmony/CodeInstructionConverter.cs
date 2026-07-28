using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Emit;
using HarmonyLib;

#nullable enable

namespace Concord.Harmony;

using CodeInstruction = HarmonyLib.CodeInstruction;
using ExceptionBlock = HarmonyLib.ExceptionBlock;
using ExceptionBlockType = HarmonyLib.ExceptionBlockType;
using Label = System.Reflection.Emit.Label;

/// <summary>
///     Converts between <see cref="HarmonyLib.CodeInstruction" /> streams and
///     <see cref="Concord.CodeInstruction" /> streams so <see cref="TranspilerParticipant" /> can compose
///     Concord's injections onto Harmony's own transpiler stream via
///     <see cref="WrapperComposer.TransformStream" />.
/// </summary>
internal static class CodeInstructionConverter {
    private const string CodeCalliRejected = "CONC124";
    private const string CodeUnknownBlockKind = "CONC125";

    /// <summary>Converts a Harmony transpiler stream into a Concord instruction stream.</summary>
    /// <param name="stream">The incoming Harmony stream.</param>
    /// <param name="context">The stream context obtained from <see cref="WrapperComposer.CreateStreamContext" />.</param>
    /// <param name="harmonyContext">The label/local identity to correlate the composed result back against.</param>
    /// <returns>The converted Concord instruction stream.</returns>
    /// <exception cref="ConcordEmitException">
    ///     Thrown with code <c>CONC124</c> when the stream contains a <c>calli</c> instruction, or with
    ///     code <c>CONC125</c> when a block carries an exception block kind neither model recognises.
    /// </exception>
    internal static List<Concord.CodeInstruction> ToConcord(List<CodeInstruction> stream, Concord.ITranspilerContext context, out HarmonyStreamContext harmonyContext) {
        HarmonyStreamContext built = new HarmonyStreamContext();
        Dictionary<Label, Concord.Label> concordLabelByHarmony = new Dictionary<Label, Concord.Label>();

        Concord.Label LabelFor(Label harmonyLabel) {
            if (concordLabelByHarmony.TryGetValue(harmonyLabel, out Concord.Label existing)) {
                return existing;
            }

            Concord.Label minted = context.DefineLabel();
            concordLabelByHarmony[harmonyLabel] = minted;
            built.HarmonyLabelByConcord[minted] = harmonyLabel;
            return minted;
        }

        List<Concord.LocalRef> localRefBySlot = DeclareLocalsForStream(stream, context, built);
        List<Concord.CodeInstruction> converted = ConvertInstructions(stream, LabelFor, localRefBySlot);

        if (HasExceptionBlocks(converted)) {
            AppendSacrificialNopAndShiftEnds(converted);
            built.AppendedSacrificialNop = true;
        }

        harmonyContext = built;
        return converted;
    }

    /// <summary>Converts a composed Concord instruction stream back into a Harmony transpiler stream.</summary>
    /// <param name="composed">The composed Concord stream, as returned by <see cref="WrapperComposer.TransformStream" />.</param>
    /// <param name="context">The label/local identity captured by <see cref="ToConcord" /> for this stream.</param>
    /// <param name="generator">Harmony's own <see cref="ILGenerator" />, used to mint labels/locals composition introduced.</param>
    /// <returns>The outgoing Harmony stream.</returns>
    internal static List<CodeInstruction> FromConcord(List<Concord.CodeInstruction> composed, HarmonyStreamContext context, ILGenerator generator) {
        if (context.AppendedSacrificialNop) {
            StripSacrificialNop(composed);
            UnshiftEndMarkersOntoPrecedingInstruction(composed);
        }

        Dictionary<Concord.LocalRef, LocalBuilder> mintedLocals = new Dictionary<Concord.LocalRef, LocalBuilder>();

        List<CodeInstruction> outgoing = new List<CodeInstruction>(composed.Count);
        foreach (Concord.CodeInstruction instruction in composed) {
            CodeInstruction emitted = new CodeInstruction(instruction.opcode, ConvertOperandBack(instruction.operand, context, generator, mintedLocals));
            foreach (Concord.Label label in instruction.labels) {
                emitted.labels.Add(ResolveLabel(label, context, generator));
            }

            foreach (Concord.ExceptionBlock block in instruction.blocks) {
                emitted.blocks.Add(MapBlockBack(block));
            }

            outgoing.Add(emitted);
        }

        return outgoing;
    }

    /// <summary>
    ///     Declares one Concord local per slot the Harmony stream touches, so slot indices in the
    ///     incoming stream stay usable as positions into the returned list.
    /// </summary>
    /// <remarks>
    ///     Slots are scanned from both directions Harmony can express them: an explicit
    ///     <see cref="LocalVariableInfo" /> operand, and the compact <c>ldloc.0</c>-style opcodes that
    ///     carry the slot in the opcode itself. A gap in the middle of the range gets a filler local
    ///     typed <see cref="object" />, because the list is indexed by slot and cannot be sparse.
    /// </remarks>
    private static List<Concord.LocalRef> DeclareLocalsForStream(List<CodeInstruction> stream, Concord.ITranspilerContext context, HarmonyStreamContext built) {
        int maxSlot = -1;
        Dictionary<int, LocalVariableInfo> localBySlot = new Dictionary<int, LocalVariableInfo>();
        foreach (CodeInstruction instruction in stream) {
            if (instruction.operand is LocalVariableInfo localInfo) {
                localBySlot[localInfo.LocalIndex] = localInfo;
                if (localInfo.LocalIndex > maxSlot) {
                    maxSlot = localInfo.LocalIndex;
                }
            }

            int compactSlot = CompactLocalSlot(instruction.opcode);
            if (compactSlot > maxSlot) {
                maxSlot = compactSlot;
            }
        }

        List<Concord.LocalRef> localRefBySlot = new List<Concord.LocalRef>(maxSlot + 1);
        for (int slot = 0; slot <= maxSlot; slot++) {
            localBySlot.TryGetValue(slot, out LocalVariableInfo? original);
            Type localType = original is null ? typeof(object) : original.LocalType;
            Concord.LocalRef declared = context.DeclareLocal(localType);
            localRefBySlot.Add(declared);
            if (original is not null) {
                built.HarmonyLocalByRef[declared] = original;
            }
        }

        return localRefBySlot;
    }

    /// <summary>Maps each Harmony instruction, its labels, and its exception blocks onto the Concord model.</summary>
    /// <exception cref="ConcordEmitException">Thrown with code <c>CONC124</c> when the stream contains a <c>calli</c>.</exception>
    private static List<Concord.CodeInstruction> ConvertInstructions(List<CodeInstruction> stream, Func<Label, Concord.Label> labelFor, List<Concord.LocalRef> localRefBySlot) {
        List<Concord.CodeInstruction> converted = new List<Concord.CodeInstruction>(stream.Count);
        foreach (CodeInstruction instruction in stream) {
            if (instruction.opcode == OpCodes.Calli) {
                throw new ConcordEmitException(
                    CodeCalliRejected,
                    "The Harmony coexistence bridge cannot compose a calli instruction: Harmony carries its call-site operand as HarmonyLib.InlineSignature, whose constructor is internal, so Concord has no way to build a call site from it.");
            }

            Concord.CodeInstruction mapped = new Concord.CodeInstruction(instruction.opcode, ConvertOperand(instruction.operand, labelFor, localRefBySlot));
            foreach (Label label in instruction.labels) {
                mapped.labels.Add(labelFor(label));
            }

            foreach (ExceptionBlock block in instruction.blocks) {
                mapped.blocks.Add(MapBlock(block));
            }

            converted.Add(mapped);
        }

        return converted;
    }

    private static object? ConvertOperand(object? operand, Func<Label, Concord.Label> labelFor, IReadOnlyList<Concord.LocalRef> localRefBySlot) {
        switch (operand) {
            case null:
                return null;
            case Label label:
                return labelFor(label);
            case Label[] labels: {
                Concord.Label[] mapped = new Concord.Label[labels.Length];
                for (int i = 0; i < labels.Length; i++) {
                    mapped[i] = labelFor(labels[i]);
                }

                return mapped;
            }

            case LocalVariableInfo localInfo:
                return localRefBySlot[localInfo.LocalIndex];
            case short argumentSlot:
                return (int)argumentSlot;
            default:
                return operand;
        }
    }

    private static object? ConvertOperandBack(object? operand, HarmonyStreamContext context, ILGenerator generator, Dictionary<Concord.LocalRef, LocalBuilder> mintedLocals) {
        switch (operand) {
            case null:
                return null;
            case Concord.Label label:
                return ResolveLabel(label, context, generator);
            case Concord.Label[] labels: {
                Label[] mapped = new Label[labels.Length];
                for (int i = 0; i < labels.Length; i++) {
                    mapped[i] = ResolveLabel(labels[i], context, generator);
                }

                return mapped;
            }

            case Concord.LocalRef localRef:
                return ResolveLocal(localRef, context, generator, mintedLocals);
            default:
                return operand;
        }
    }

    private static Label ResolveLabel(Concord.Label label, HarmonyStreamContext context, ILGenerator generator) {
        if (context.HarmonyLabelByConcord.TryGetValue(label, out Label existing)) {
            return existing;
        }

        Label minted = generator.DefineLabel();
        context.HarmonyLabelByConcord[label] = minted;
        return minted;
    }

    private static LocalBuilder ResolveLocal(Concord.LocalRef localRef, HarmonyStreamContext context, ILGenerator generator, Dictionary<Concord.LocalRef, LocalBuilder> mintedLocals) {
        if (context.HarmonyLocalByRef.TryGetValue(localRef, out LocalVariableInfo? original) && original is LocalBuilder builder) {
            return builder;
        }

        if (mintedLocals.TryGetValue(localRef, out LocalBuilder? already)) {
            return already;
        }

        LocalBuilder minted = generator.DeclareLocal(localRef.Type);
        mintedLocals[localRef] = minted;
        return minted;
    }

    private static int CompactLocalSlot(OpCode opcode) {
        if (opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Stloc_0) {
            return 0;
        }

        if (opcode == OpCodes.Ldloc_1 || opcode == OpCodes.Stloc_1) {
            return 1;
        }

        if (opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Stloc_2) {
            return 2;
        }

        if (opcode == OpCodes.Ldloc_3 || opcode == OpCodes.Stloc_3) {
            return 3;
        }

        return -1;
    }

    private static Concord.ExceptionBlock MapBlock(ExceptionBlock block) {
        return new Concord.ExceptionBlock(MapBlockType(block.blockType), block.catchType);
    }

    private static Concord.ExceptionBlockType MapBlockType(ExceptionBlockType blockType) {
        return blockType switch {
            ExceptionBlockType.BeginExceptionBlock => Concord.ExceptionBlockType.BeginExceptionBlock,
            ExceptionBlockType.BeginCatchBlock => Concord.ExceptionBlockType.BeginCatchBlock,
            ExceptionBlockType.BeginExceptFilterBlock => Concord.ExceptionBlockType.BeginExceptFilterBlock,
            ExceptionBlockType.BeginFaultBlock => Concord.ExceptionBlockType.BeginFaultBlock,
            ExceptionBlockType.BeginFinallyBlock => Concord.ExceptionBlockType.BeginFinallyBlock,
            ExceptionBlockType.EndExceptionBlock => Concord.ExceptionBlockType.EndExceptionBlock,
            _ => throw new ConcordEmitException(CodeUnknownBlockKind, $"Unrecognized Harmony exception block kind '{blockType}'."),
        };
    }

    private static ExceptionBlock MapBlockBack(Concord.ExceptionBlock block) {
        return new ExceptionBlock(MapBlockTypeBack(block.blockType), block.catchType);
    }

    private static ExceptionBlockType MapBlockTypeBack(Concord.ExceptionBlockType blockType) {
        return blockType switch {
            Concord.ExceptionBlockType.BeginExceptionBlock => ExceptionBlockType.BeginExceptionBlock,
            Concord.ExceptionBlockType.BeginCatchBlock => ExceptionBlockType.BeginCatchBlock,
            Concord.ExceptionBlockType.BeginExceptFilterBlock => ExceptionBlockType.BeginExceptFilterBlock,
            Concord.ExceptionBlockType.BeginFaultBlock => ExceptionBlockType.BeginFaultBlock,
            Concord.ExceptionBlockType.BeginFinallyBlock => ExceptionBlockType.BeginFinallyBlock,
            Concord.ExceptionBlockType.EndExceptionBlock => ExceptionBlockType.EndExceptionBlock,
            _ => throw new ConcordEmitException(CodeUnknownBlockKind, $"Unrecognized Concord exception block kind '{blockType}'."),
        };
    }

    private static bool HasExceptionBlocks(List<Concord.CodeInstruction> stream) {
        foreach (Concord.CodeInstruction instruction in stream) {
            if (instruction.blocks.Count > 0) {
                return true;
            }
        }

        return false;
    }

    // Harmony places EndExceptionBlock on a handler's LAST instruction (HandlerOffset + HandlerLength
    // - 1); Concord places it on the instruction AFTER the region. Appending one sacrificial Nop
    // guarantees every End marker has an i+1 to shift onto, even one Harmony placed on the stream's
    // own last instruction - stripped back out in FromConcord. Walking backward means an End just
    // shifted onto i+1 is never revisited as "current" later in the same pass. Moving marker(s) to
    // the FRONT of the target's own block list, rather than appending, keeps End-before-Begin
    // ordering intact for the case where a shifted End lands on the same instruction as the next
    // region's own Begin (e.g. a catch immediately followed by a finally) - stack-based semantics in
    // CecilCodeConverter.ApplyBlock require the closing marker to be processed first.
    private static void AppendSacrificialNopAndShiftEnds(List<Concord.CodeInstruction> stream) {
        stream.Add(new Concord.CodeInstruction(OpCodes.Nop));

        for (int i = stream.Count - 2; i >= 0; i--) {
            Concord.CodeInstruction current = stream[i];
            List<Concord.ExceptionBlock> ends = new List<Concord.ExceptionBlock>();
            foreach (Concord.ExceptionBlock block in current.blocks) {
                if (block.blockType == Concord.ExceptionBlockType.EndExceptionBlock) {
                    ends.Add(block);
                }
            }

            if (ends.Count == 0) {
                continue;
            }

            foreach (Concord.ExceptionBlock end in ends) {
                current.blocks.Remove(end);
            }

            stream[i + 1].blocks.InsertRange(0, ends);
        }
    }

    // The filler is found structurally - the sole Nop with no operand carrying an EndExceptionBlock
    // marker - because TransformStream's Cecil round trip hands back entirely new instruction
    // objects, so it cannot be located by reference. When nothing follows it, the Nop is left in
    // place rather than stripped: that is exactly the terminal, non-null HandlerEnd anchor the
    // sacrificial instruction exists to provide, so removing it would recreate the null-HandlerEnd
    // problem it was added to avoid.
    private static void StripSacrificialNop(List<Concord.CodeInstruction> composed) {
        int index = composed.FindIndex(instruction =>
            instruction.opcode == OpCodes.Nop &&
            instruction.operand is null &&
            instruction.blocks.Exists(block => block.blockType == Concord.ExceptionBlockType.EndExceptionBlock));

        if (index < 0 || index == composed.Count - 1) {
            return;
        }

        Concord.CodeInstruction filler = composed[index];
        Concord.CodeInstruction next = composed[index + 1];
        next.blocks.InsertRange(0, filler.blocks);
        next.labels.AddRange(filler.labels);
        composed.RemoveAt(index);
    }

    // Harmony's own emission (MethodPatcher.EmitCodes: MarkBlockBefore runs before an
    // instruction's opcode is emitted and only reacts to Begin* markers; MarkBlockAfter runs right
    // after and only reacts to EndExceptionBlock) expects EndExceptionBlock on the region's LAST
    // instruction - the mirror image of Concord's own "instruction after the region" placement.
    // This walks FORWARD, the reverse of AppendSacrificialNopAndShiftEnds's backward walk: by the
    // time a marker is added onto i-1, that instruction's own native Ends (if any) were already
    // read and cleared in the iteration where it was "current", so nothing is ever reordered
    // relative to what is being added.
    private static void UnshiftEndMarkersOntoPrecedingInstruction(List<Concord.CodeInstruction> stream) {
        for (int i = 1; i < stream.Count; i++) {
            Concord.CodeInstruction current = stream[i];
            List<Concord.ExceptionBlock> ends = new List<Concord.ExceptionBlock>();
            foreach (Concord.ExceptionBlock block in current.blocks) {
                if (block.blockType == Concord.ExceptionBlockType.EndExceptionBlock) {
                    ends.Add(block);
                }
            }

            if (ends.Count == 0) {
                continue;
            }

            foreach (Concord.ExceptionBlock end in ends) {
                current.blocks.Remove(end);
            }

            stream[i - 1].blocks.AddRange(ends);
        }
    }
}
