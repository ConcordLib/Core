using System.Reflection.Emit;
using Concord.Emit;

namespace Concord;

/// <summary>
///     A fluent positional cursor over a transpiler's IL stream. Locate a sequence with
///     <see cref="MatchStartForward" /> or <see cref="MatchStartBackwards" />, then read or edit
///     at the cursor and chain onward.
/// </summary>
/// <remarks>
///     A failed match leaves <see cref="Pos" /> at <c>-1</c>. Every editing method (the two
///     <c>Set*AndAdvance</c> methods, <see cref="SetInstruction" />, <see cref="Insert" />,
///     <see cref="InsertAndAdvance" />, <see cref="RemoveInstruction" />, and
///     <see cref="RemoveInstructions" />) is a no-op while <see cref="IsInvalid" />, so a fluent
///     chain never throws before it reaches <see cref="ThrowIfInvalid" />. <see cref="Insert" /> and
///     <see cref="InsertAndAdvance" /> both need a valid <see cref="Pos" /> to insert before, so
///     there is no way to append after the working list's last instruction through the cursor;
///     append directly to <see cref="InstructionEnumeration" />'s returned list instead.
/// </remarks>
public sealed class CodeMatcher {
    private readonly List<CodeInstruction> codes;

    /// <summary>Wraps a fresh working copy of <paramref name="instructions" />, positioned before the first instruction.</summary>
    /// <param name="instructions">The instructions to search and edit.</param>
    public CodeMatcher(IEnumerable<CodeInstruction> instructions) {
        codes = new List<CodeInstruction>(instructions);
    }

    /// <summary>Wraps a fresh working copy of <paramref name="instructions" />, positioned before the first instruction.</summary>
    /// <param name="instructions">The instructions to search and edit.</param>
    /// <param name="context">The transpiler context this matcher's edits belong to. Not read by any member in this version.</param>
    public CodeMatcher(IEnumerable<CodeInstruction> instructions, ITranspilerContext context) : this(instructions) {
    }

    /// <summary>The zero-based index of the current instruction, or <c>-1</c> when invalid.</summary>
    public int Pos { get; private set; } = -1;

    /// <summary>Whether <see cref="Pos" /> refers to an instruction in the working list.</summary>
    public bool IsValid => Pos >= 0 && Pos < codes.Count;

    /// <summary>The negation of <see cref="IsValid" />.</summary>
    public bool IsInvalid => !IsValid;

    /// <summary>The instruction at <see cref="Pos" />.</summary>
    /// <exception cref="ConcordEmitException">Thrown with code <c>CONC120</c> when <see cref="IsInvalid" />.</exception>
    public CodeInstruction Instruction {
        get {
            if (IsInvalid) {
                throw new ConcordEmitException("CONC120", "Instruction was read while the matcher is invalid. Call ThrowIfInvalid (or check IsValid) before reading Instruction.");
            }

            return codes[Pos];
        }
    }

    /// <summary>
    ///     Searches forward for the first run of instructions matching <paramref name="matches" /> in order,
    ///     starting just after <see cref="Pos" /> (or at the start of the list when invalid).
    /// </summary>
    /// <param name="matches">
    ///     The consecutive sequence to find. <see cref="Pos" /> lands on its first instruction. An empty
    ///     array is treated as an immediate non-match rather than a match at the current search origin.
    /// </param>
    /// <returns>This matcher, positioned on the match, or invalid when none was found.</returns>
    public CodeMatcher MatchStartForward(params CodeMatch[] matches) {
        if (matches.Length == 0) {
            Reposition(-1);
            return this;
        }

        int start = IsValid ? Pos + 1 : 0;
        int lastStart = codes.Count - matches.Length;
        for (int i = start; i <= lastStart; i++) {
            if (MatchesAt(i, matches)) {
                Reposition(i);
                return this;
            }
        }

        Reposition(-1);
        return this;
    }

    /// <summary>
    ///     Searches backward for the first run of instructions matching <paramref name="matches" /> in order,
    ///     starting just before <see cref="Pos" /> (or at the end of the list when invalid).
    /// </summary>
    /// <param name="matches">
    ///     The consecutive sequence to find. <see cref="Pos" /> lands on its first instruction. An empty
    ///     array is treated as an immediate non-match rather than a match at the current search origin.
    /// </param>
    /// <returns>This matcher, positioned on the match, or invalid when none was found.</returns>
    public CodeMatcher MatchStartBackwards(params CodeMatch[] matches) {
        if (matches.Length == 0) {
            Reposition(-1);
            return this;
        }

        int lastStart = codes.Count - matches.Length;
        int start = IsValid ? Math.Min(Pos - 1, lastStart) : lastStart;
        for (int i = start; i >= 0; i--) {
            if (MatchesAt(i, matches)) {
                Reposition(i);
                return this;
            }
        }

        Reposition(-1);
        return this;
    }

    /// <summary>Moves the cursor by <paramref name="offset" /> instructions. A no-op while invalid.</summary>
    /// <param name="offset">The number of instructions to move forward, or backward when negative.</param>
    /// <returns>This matcher, at the new position, or invalid when the move landed outside the list.</returns>
    public CodeMatcher Advance(int offset) {
        if (IsInvalid) {
            return this;
        }

        Reposition(Pos + offset);
        return this;
    }

    /// <summary>Moves the cursor to the first instruction.</summary>
    /// <returns>This matcher, positioned at index 0, or invalid when the working list is empty.</returns>
    public CodeMatcher Start() {
        Reposition(0);
        return this;
    }

    /// <summary>Moves the cursor to the last instruction.</summary>
    /// <returns>This matcher, positioned at the last index, or invalid when the working list is empty.</returns>
    public CodeMatcher End() {
        Reposition(codes.Count - 1);
        return this;
    }

    /// <summary>Throws when the cursor is invalid.</summary>
    /// <param name="message">The failure detail included in the thrown exception.</param>
    /// <returns>This matcher, unchanged, when valid.</returns>
    /// <exception cref="ConcordEmitException">Thrown with code <c>CONC120</c> when <see cref="IsInvalid" />.</exception>
    public CodeMatcher ThrowIfInvalid(string message) {
        if (IsInvalid) {
            throw new ConcordEmitException("CONC120", message);
        }

        return this;
    }

    /// <summary>Sets the opcode of the current instruction, then advances by one. A no-op while invalid.</summary>
    /// <param name="opcode">The opcode to assign.</param>
    /// <returns>This matcher, past the edited instruction.</returns>
    public CodeMatcher SetOpcodeAndAdvance(OpCode opcode) {
        if (IsInvalid) {
            return this;
        }

        codes[Pos].opcode = opcode;
        return Advance(1);
    }

    /// <summary>Sets the operand of the current instruction, then advances by one. A no-op while invalid.</summary>
    /// <param name="operand">The operand to assign, or <see langword="null" /> to clear it.</param>
    /// <returns>This matcher, past the edited instruction.</returns>
    public CodeMatcher SetOperandAndAdvance(object? operand) {
        if (IsInvalid) {
            return this;
        }

        codes[Pos].operand = operand;
        return Advance(1);
    }

    /// <summary>Replaces the current instruction outright. A no-op while invalid.</summary>
    /// <param name="instruction">The replacement instruction.</param>
    /// <returns>This matcher, still positioned at the replaced index.</returns>
    public CodeMatcher SetInstruction(CodeInstruction instruction) {
        if (IsInvalid) {
            return this;
        }

        codes[Pos] = instruction;
        return this;
    }

    /// <summary>
    ///     Inserts <paramref name="instructions" /> before the cursor without moving it, so
    ///     <see cref="Instruction" /> now returns the first inserted instruction. A no-op while invalid.
    /// </summary>
    /// <param name="instructions">The instructions to insert.</param>
    /// <returns>This matcher, still at the same numeric position.</returns>
    public CodeMatcher Insert(params CodeInstruction[] instructions) {
        if (IsInvalid) {
            return this;
        }

        codes.InsertRange(Pos, instructions);
        return this;
    }

    /// <summary>
    ///     Inserts <paramref name="instructions" /> before the cursor and advances past all of them, so the
    ///     cursor lands back on the instruction that was current before the call. A no-op while invalid.
    /// </summary>
    /// <param name="instructions">The instructions to insert.</param>
    /// <returns>This matcher, positioned just after the inserted instructions.</returns>
    public CodeMatcher InsertAndAdvance(params CodeInstruction[] instructions) {
        if (IsInvalid) {
            return this;
        }

        foreach (CodeInstruction instruction in instructions) {
            codes.Insert(Pos, instruction);
            Advance(1);
        }

        return this;
    }

    /// <summary>
    ///     Removes the current instruction. Any labels it carried are moved onto the following instruction
    ///     (or the preceding one, if the removal reached the end of the list) so branch targets stay
    ///     resolvable. Exception-block boundaries only move onto a following instruction; removing the
    ///     boundary at the very end of the list with nothing left to carry it forward drops it, which
    ///     surfaces as <c>CONC118</c> (unbalanced exception blocks) when the rewritten body is composed,
    ///     rather than silently producing a malformed region. A no-op while invalid.
    /// </summary>
    /// <returns>This matcher, at the same numeric position, or invalid when the removed instruction was last.</returns>
    public CodeMatcher RemoveInstruction() {
        if (IsInvalid) {
            return this;
        }

        TransferBoundaries(Pos, 1);
        codes.RemoveAt(Pos);
        Reposition(Pos);
        return this;
    }

    /// <summary>
    ///     Removes up to <paramref name="count" /> instructions starting at the cursor, clamped to the
    ///     instructions actually remaining. Labels carried by the removed range are moved onto the following
    ///     instruction (or the preceding one, if the removal reached the end of the list). Exception-block
    ///     boundaries only move onto a following instruction; a boundary stranded at the very end of the
    ///     list is dropped, which surfaces as <c>CONC118</c> when the rewritten body is composed rather than
    ///     silently producing a malformed region. A no-op while invalid.
    /// </summary>
    /// <param name="count">The number of instructions to remove.</param>
    /// <returns>This matcher, at the same numeric position, or invalid when the removal reached the end.</returns>
    public CodeMatcher RemoveInstructions(int count) {
        if (IsInvalid) {
            return this;
        }

        int actualCount = Math.Max(0, Math.Min(count, codes.Count - Pos));
        if (actualCount > 0) {
            TransferBoundaries(Pos, actualCount);
            codes.RemoveRange(Pos, actualCount);
        }

        Reposition(Pos);
        return this;
    }

    /// <summary>The working list of instructions, including every edit made so far.</summary>
    /// <returns>The working list. A transpiler returns this to hand its rewritten body back to Concord.</returns>
    public List<CodeInstruction> InstructionEnumeration() {
        return codes;
    }

    private bool MatchesAt(int start, CodeMatch[] matches) {
        for (int offset = 0; offset < matches.Length; offset++) {
            if (!matches[offset].Matches(codes[start + offset])) {
                return false;
            }
        }

        return true;
    }

    private void Reposition(int candidate) {
        Pos = candidate >= 0 && candidate < codes.Count ? candidate : -1;
    }

    // Labels are safe to move in either direction - a label is just a name for an instruction, and
    // any surviving instruction can serve as a jump target regardless of order. Exception-block
    // boundaries are not: they encode an ordered open/close pair, and Begin*/End* markers moved
    // backward onto an instruction that is still part of the frame being closed (e.g. the last real
    // instruction of a finally handler) would exclude that instruction from its own region -
    // WriteBack's ApplyBlocks would not catch this (it only checks that every frame it opens is
    // eventually closed, not that the closing position still makes sense), so the resulting method
    // can pass WriteBack cleanly and then fail confusingly at code generation instead. So blocks only
    // transfer onto a forward survivor; a boundary stranded at the end of the list is dropped, and
    // the frame it belonged to either balances out (its other marker was removed too) or surfaces as
    // a clean CONC118 "opened and never closed" from WriteBack - never a corrupted region.
    private void TransferBoundaries(int start, int count) {
        List<Label> labels = new List<Label>();
        List<ExceptionBlock> blocks = new List<ExceptionBlock>();
        for (int i = start; i < start + count; i++) {
            labels.AddRange(codes[i].labels);
            blocks.AddRange(codes[i].blocks);
        }

        if (labels.Count == 0 && blocks.Count == 0) {
            return;
        }

        bool hasForwardSurvivor = start + count < codes.Count;
        int survivorIndex = hasForwardSurvivor ? start + count : start - 1;
        if (survivorIndex < 0) {
            return;
        }

        CodeInstruction survivor = codes[survivorIndex];
        foreach (Label label in labels) {
            if (!survivor.labels.Contains(label)) {
                survivor.labels.Add(label);
            }
        }

        if (!hasForwardSurvivor) {
            return;
        }

        foreach (ExceptionBlock block in blocks) {
            if (!survivor.blocks.Contains(block)) {
                survivor.blocks.Add(block);
            }
        }
    }
}

/// <summary>
///     One step of a <see cref="CodeMatcher" /> search: either a specific opcode with an optional
///     operand, or an arbitrary predicate a candidate instruction must satisfy.
/// </summary>
public sealed class CodeMatch {
    private readonly OpCode? opcode;
    private readonly object? operand;
    private readonly Predicate<CodeInstruction>? predicate;

    /// <summary>Matches a specific opcode and, optionally, its operand.</summary>
    /// <param name="opcode">The opcode a candidate instruction must carry.</param>
    /// <param name="operand">The operand to require, or <see langword="null" /> to match any operand.</param>
    /// <param name="name">Reserved for a future named-capture lookup. Not read by any member in this version.</param>
    public CodeMatch(OpCode opcode, object? operand = null, string? name = null) {
        this.opcode = opcode;
        this.operand = operand;
    }

    /// <summary>Matches any instruction that satisfies an arbitrary predicate.</summary>
    /// <param name="predicate">The test a candidate instruction must satisfy.</param>
    /// <param name="name">Reserved for a future named-capture lookup. Not read by any member in this version.</param>
    public CodeMatch(Predicate<CodeInstruction> predicate, string? name = null) {
        this.predicate = predicate;
    }

    internal bool Matches(CodeInstruction instruction) {
        return opcode.HasValue ? instruction.Is(opcode.Value, operand) : predicate!(instruction);
    }
}
