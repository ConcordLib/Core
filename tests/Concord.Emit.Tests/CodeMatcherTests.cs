using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public static class MatcherTranspilers {
    public static IEnumerable<CodeInstruction> FiveToTwelve(IEnumerable<CodeInstruction> instructions) {
        return new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(OpCodes.Ldc_I4_5))
            .ThrowIfInvalid("price literal")
            .SetInstruction(new CodeInstruction(OpCodes.Ldc_I4, 12))
            .InstructionEnumeration();
    }
}

public sealed class CodeMatcherTests {
    private static List<CodeInstruction> Sample() {
        return [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldc_I4, 5),
            new CodeInstruction(OpCodes.Add),
            new CodeInstruction(OpCodes.Ret),
        ];
    }

    [Fact]
    public void MatchStartForward_FindsPosition() {
        CodeMatcher matcher = new CodeMatcher(Sample()).MatchStartForward(new CodeMatch(OpCodes.Ldc_I4, 5));

        Assert.True(matcher.IsValid);
        Assert.Equal(1, matcher.Pos);
    }

    [Fact]
    public void SetOperandAndAdvance_EditsAndMoves() {
        CodeMatcher matcher = new CodeMatcher(Sample())
            .MatchStartForward(new CodeMatch(OpCodes.Ldc_I4, 5))
            .SetOperandAndAdvance(10);

        List<CodeInstruction> output = matcher.InstructionEnumeration();
        Assert.Equal(10, output[1].operand);
        Assert.Equal(2, matcher.Pos);
    }

    [Fact]
    public void ThrowIfInvalid_OnNoMatch_Throws() {
        CodeMatcher matcher = new CodeMatcher(Sample()).MatchStartForward(new CodeMatch(OpCodes.Ldc_I8, 99L));

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => matcher.ThrowIfInvalid("price literal"));
        Assert.Equal("CONC120", ex.Code);
        Assert.Contains("price literal", ex.Message);
    }

    [Fact]
    public void InsertAndAdvance_AddsInstructions() {
        List<CodeInstruction> output = new CodeMatcher(Sample())
            .MatchStartForward(new CodeMatch(OpCodes.Add))
            .InsertAndAdvance(new CodeInstruction(OpCodes.Nop))
            .InstructionEnumeration();

        Assert.Equal(5, output.Count);
        Assert.Equal(OpCodes.Nop, output[2].opcode);
    }

    [Fact]
    public void RemoveInstructions_RemovesRange() {
        List<CodeInstruction> output = new CodeMatcher(Sample())
            .MatchStartForward(new CodeMatch(OpCodes.Ldc_I4, 5))
            .RemoveInstructions(2)
            .InstructionEnumeration();

        Assert.Equal(2, output.Count);
        Assert.Equal(OpCodes.Ret, output[1].opcode);
    }

    [Fact]
    public void MatchStartForward_MultiMatch_MatchesSequence() {
        CodeMatcher matcher = new CodeMatcher(Sample()).MatchStartForward(
            new CodeMatch(OpCodes.Ldc_I4, 5),
            new CodeMatch(OpCodes.Add));

        Assert.True(matcher.IsValid);
        Assert.Equal(1, matcher.Pos);
    }

    [Fact]
    public void CodeMatcher_DrivesRealTranspiler() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase method = typeof(MatcherTranspilers).GetMethod(nameof(MatcherTranspilers.FiveToTwelve))!;

        ComposeResult result = WrapperComposer.Compose(target, [new Injection(method, new InjectAt.Transpiler(false), "test", 0)]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(12, run());
    }

    [Fact]
    public void MatchStartForward_ChainedCalls_SearchFromNextPosition() {
        List<CodeInstruction> instructions = [
            new CodeInstruction(OpCodes.Ldc_I4, 1),
            new CodeInstruction(OpCodes.Ldc_I4, 1),
            new CodeInstruction(OpCodes.Ret),
        ];

        CodeMatcher matcher = new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(OpCodes.Ldc_I4, 1))
            .MatchStartForward(new CodeMatch(OpCodes.Ldc_I4, 1));

        Assert.True(matcher.IsValid);
        Assert.Equal(1, matcher.Pos);
    }

    [Fact]
    public void MatchStartForward_NoMatch_LaterEditsAreNoOps() {
        List<CodeInstruction> output = new CodeMatcher(Sample())
            .MatchStartForward(new CodeMatch(OpCodes.Ldc_I8, 99L))
            .SetOperandAndAdvance(123)
            .Insert(new CodeInstruction(OpCodes.Nop))
            .RemoveInstruction()
            .InstructionEnumeration();

        Assert.Equal(4, output.Count);
        Assert.Equal(OpCodes.Ldarg_0, output[0].opcode);
        Assert.Equal(OpCodes.Ldc_I4, output[1].opcode);
        Assert.Equal(5, output[1].operand);
        Assert.Equal(OpCodes.Add, output[2].opcode);
        Assert.Equal(OpCodes.Ret, output[3].opcode);
    }

    [Fact]
    public void MatchStartBackwards_FindsNearestMatchBehindCursor() {
        List<CodeInstruction> instructions = [
            new CodeInstruction(OpCodes.Ldc_I4, 1),
            new CodeInstruction(OpCodes.Nop),
            new CodeInstruction(OpCodes.Ldc_I4, 1),
            new CodeInstruction(OpCodes.Ret),
        ];

        CodeMatcher matcher = new CodeMatcher(instructions).End().MatchStartBackwards(new CodeMatch(OpCodes.Ldc_I4, 1));

        Assert.True(matcher.IsValid);
        Assert.Equal(2, matcher.Pos);
    }

    [Fact]
    public void StartAndEnd_PositionAtListBoundaries() {
        CodeMatcher matcher = new CodeMatcher(Sample());

        matcher.Start();
        Assert.Equal(0, matcher.Pos);

        matcher.End();
        Assert.Equal(3, matcher.Pos);
    }

    [Fact]
    public void StartAndEnd_OnEmptyList_AreInvalid() {
        CodeMatcher matcher = new CodeMatcher(new List<CodeInstruction>());

        matcher.Start();
        Assert.True(matcher.IsInvalid);

        matcher.End();
        Assert.True(matcher.IsInvalid);
    }

    [Fact]
    public void Advance_PastEnd_BecomesInvalid() {
        CodeMatcher matcher = new CodeMatcher(Sample()).End().Advance(1);

        Assert.True(matcher.IsInvalid);
        Assert.Equal(-1, matcher.Pos);
    }

    [Fact]
    public void Insert_PlacesBeforeCursorWithoutAdvancing() {
        CodeMatcher matcher = new CodeMatcher(Sample())
            .MatchStartForward(new CodeMatch(OpCodes.Add))
            .Insert(new CodeInstruction(OpCodes.Nop));

        List<CodeInstruction> output = matcher.InstructionEnumeration();
        Assert.Equal(5, output.Count);
        Assert.Equal(OpCodes.Nop, output[2].opcode);
        Assert.Equal(2, matcher.Pos);
        Assert.Equal(OpCodes.Nop, matcher.Instruction.opcode);
    }

    [Fact]
    public void RemoveInstruction_OnLastElement_BecomesInvalid() {
        CodeMatcher matcher = new CodeMatcher(Sample()).End().RemoveInstruction();

        Assert.True(matcher.IsInvalid);
        Assert.Equal(-1, matcher.Pos);
        Assert.Equal(3, matcher.InstructionEnumeration().Count);
    }

    [Fact]
    public void RemoveInstructions_CountRunsPastEnd_ClampsAndInvalidates() {
        CodeMatcher matcher = new CodeMatcher(Sample())
            .MatchStartForward(new CodeMatch(OpCodes.Add))
            .RemoveInstructions(10);

        List<CodeInstruction> output = matcher.InstructionEnumeration();
        Assert.Equal(2, output.Count);
        Assert.Equal(OpCodes.Ldarg_0, output[0].opcode);
        Assert.Equal(OpCodes.Ldc_I4, output[1].opcode);
        Assert.True(matcher.IsInvalid);
    }

    [Fact]
    public void RemoveInstruction_TransfersLabelToFollowingInstruction() {
        Label label = LabelFactory.Create(1);
        CodeInstruction branchTarget = new CodeInstruction(OpCodes.Nop);
        branchTarget.labels.Add(label);
        List<CodeInstruction> instructions = [
            new CodeInstruction(OpCodes.Br, label),
            branchTarget,
            new CodeInstruction(OpCodes.Ret),
        ];

        CodeMatcher matcher = new CodeMatcher(instructions).Start().Advance(1).RemoveInstruction();

        List<CodeInstruction> output = matcher.InstructionEnumeration();
        Assert.Equal(2, output.Count);
        Assert.Equal(OpCodes.Br, output[0].opcode);
        Assert.Contains(label, output[1].labels);
        Assert.Equal(1, matcher.Pos);
    }

    [Fact]
    public void Constructor_WithContext_AcceptsContextWithoutUsingIt() {
        MethodBase original = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        TranspilerContext context = new TranspilerContext(original);

        CodeMatcher matcher = new CodeMatcher(Sample(), context);

        Assert.True(matcher.IsInvalid);
        Assert.Equal(4, matcher.InstructionEnumeration().Count);
    }
}
