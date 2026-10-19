using System.Reflection.Emit;
using System.Text;

namespace Concord.Emit;

internal static class ControlFlowValidator {
    internal const string Code = "CONC174";

    internal static string? FindIllegalBranch(IReadOnlyList<CodeInstruction> instructions) {
        string[] regions = MapRegions(instructions, out string[] entryRegions);
        Dictionary<Label, int> targets = MapLabels(instructions);

        for (int i = 0; i < instructions.Count; i++) {
            CodeInstruction instruction = instructions[i];
            if (instruction.opcode == OpCodes.Ret && regions[i].Length > 0) {
                return $"'{instruction.opcode.Name}' at instruction {i} sits inside a protected region. A try, catch, filter, fault or finally body has to exit with 'leave'.";
            }

            if (instruction.opcode == OpCodes.Leave || instruction.opcode == OpCodes.Leave_S) {
                continue;
            }

            foreach (Label label in BranchTargets(instruction)) {
                if (!targets.TryGetValue(label, out int target) || regions[target] == regions[i] || entryRegions[target] == regions[i]) {
                    continue;
                }

                return $"'{instruction.opcode.Name}' at instruction {i} branches to instruction {target}, which is in a different exception region. " +
                       "A branch that crosses a try, catch, filter, fault or finally boundary has to be a 'leave'.";
            }
        }

        return null;
    }

    private static IEnumerable<Label> BranchTargets(CodeInstruction instruction) {
        if (instruction.opcode.OperandType is not (OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget or OperandType.InlineSwitch)) {
            yield break;
        }

        if (instruction.operand is Label single) {
            yield return single;
        } else if (instruction.operand is IEnumerable<Label> many) {
            foreach (Label label in many) {
                yield return label;
            }
        }
    }

    private static Dictionary<Label, int> MapLabels(IReadOnlyList<CodeInstruction> instructions) {
        Dictionary<Label, int> map = new Dictionary<Label, int>();
        for (int i = 0; i < instructions.Count; i++) {
            foreach (Label label in instructions[i].labels) {
                map[label] = i;
            }
        }

        return map;
    }

    private static string[] MapRegions(IReadOnlyList<CodeInstruction> instructions, out string[] entryRegions) {
        string[] regions = new string[instructions.Count];
        string[] entries = new string[instructions.Count];
        List<int> frames = new List<int>();
        List<int> sections = new List<int>();
        int nextFrame = 0;

        for (int i = 0; i < instructions.Count; i++) {
            foreach (ExceptionBlock block in instructions[i].blocks) {
                if (block.blockType == ExceptionBlockType.BeginExceptionBlock) {
                    entries[i] ??= Describe(frames, sections);
                    frames.Add(nextFrame++);
                    sections.Add(0);
                } else if (block.blockType == ExceptionBlockType.EndExceptionBlock) {
                    if (frames.Count == 0) {
                        entryRegions = EveryInstructionUnregioned(instructions.Count);
                        return EveryInstructionUnregioned(instructions.Count);
                    }

                    frames.RemoveAt(frames.Count - 1);
                    sections.RemoveAt(sections.Count - 1);
                } else if (frames.Count > 0) {
                    sections[sections.Count - 1]++;
                }
            }

            regions[i] = Describe(frames, sections);
            entries[i] ??= regions[i];
        }

        if (frames.Count != 0) {
            entryRegions = EveryInstructionUnregioned(instructions.Count);
            return EveryInstructionUnregioned(instructions.Count);
        }

        entryRegions = entries;
        return regions;
    }

    private static string[] EveryInstructionUnregioned(int count) {
        string[] regions = new string[count];
        for (int i = 0; i < count; i++) {
            regions[i] = string.Empty;
        }

        return regions;
    }

    private static string Describe(List<int> frames, List<int> sections) {
        if (frames.Count == 0) {
            return string.Empty;
        }

        StringBuilder path = new StringBuilder();
        for (int i = 0; i < frames.Count; i++) {
            path.Append(frames[i]).Append('/').Append(sections[i]).Append('|');
        }

        return path.ToString();
    }
}
