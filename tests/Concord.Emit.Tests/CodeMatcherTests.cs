using System.Reflection;
using System.Reflection.Emit;
using MonoMod.Utils;
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

    [Fact]
    public void MatchStartForward_ZeroLengthMatches_OnFreshMatcher_DoesNotClaimFalseMatch() {
        CodeMatcher matcher = new CodeMatcher(Sample()).MatchStartForward();

        Assert.True(matcher.IsInvalid);
        Assert.Equal(-1, matcher.Pos);
    }

    [Fact]
    public void MatchStartForward_ZeroLengthMatches_FromLastIndex_NormalizesToNegativeOne() {
        CodeMatcher matcher = new CodeMatcher(Sample()).End().MatchStartForward();

        Assert.True(matcher.IsInvalid);
        Assert.Equal(-1, matcher.Pos);
    }

    [Fact]
    public void MatchStartBackwards_ZeroLengthMatches_OnFreshMatcher_NormalizesToNegativeOne() {
        CodeMatcher matcher = new CodeMatcher(Sample()).MatchStartBackwards();

        Assert.True(matcher.IsInvalid);
        Assert.Equal(-1, matcher.Pos);
    }

    [Fact]
    public void MatchStartBackwards_ZeroLengthMatches_FromEnd_DoesNotClaimFalseMatch() {
        CodeMatcher matcher = new CodeMatcher(Sample()).End().MatchStartBackwards();

        Assert.True(matcher.IsInvalid);
        Assert.Equal(-1, matcher.Pos);
    }

    [Fact]
    public void Instruction_WhileInvalid_ThrowsConcordEmitExceptionNotBclException() {
        CodeMatcher matcher = new CodeMatcher(Sample()).MatchStartForward(new CodeMatch(OpCodes.Ldc_I8, 99L));

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => matcher.Instruction);
        Assert.Equal("CONC120", ex.Code);
    }

    // A hand-built try/finally probe: total = mode; try { leave to `after` } finally { endfinally };
    // after: return total. Every exception-block marker sits on a dedicated Nop so tests can remove
    // exactly one marker without touching the instructions that do real work.
    //   0: Ldarg_0
    //   1: Stloc_0                                   // total = mode
    //   2: Nop    [BeginExceptionBlock]               // try start
    //   3: Leave -> label
    //   4: Nop    [BeginFinallyBlock]                 // handler start / try end
    //   5: Endfinally
    //   6: Nop    [EndExceptionBlock] [label]          // handler end / leave target
    //   7: Ldloc_0
    //   8: Ret
    private static List<CodeInstruction> BuildFinallyProbe(out Label after) {
        after = LabelFactory.Create(1);

        CodeInstruction beginTry = new CodeInstruction(OpCodes.Nop);
        beginTry.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));

        CodeInstruction beginFinally = new CodeInstruction(OpCodes.Nop);
        beginFinally.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock));

        CodeInstruction endMarker = new CodeInstruction(OpCodes.Nop);
        endMarker.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        endMarker.labels.Add(after);

        return [
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Stloc_0),
            beginTry,
            new CodeInstruction(OpCodes.Leave, after),
            beginFinally,
            new CodeInstruction(OpCodes.Endfinally),
            endMarker,
            new CodeInstruction(OpCodes.Ldloc_0),
            new CodeInstruction(OpCodes.Ret),
        ];
    }

    private static DynamicMethodDefinition NewFinallyShapedMethod() {
        MethodBase shape = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Finally))!;
        DynamicMethodDefinition method = new DynamicMethodDefinition(shape);
        method.Definition.Body.Variables.Clear();
        return method;
    }

    private static TranspilerContext NewFinallyShapedContext() {
        MethodBase shape = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Finally))!;
        TranspilerContext context = new TranspilerContext(shape);
        context.DeclareLocal(typeof(int));
        return context;
    }

    [Fact]
    public void RemoveInstruction_TransfersExceptionBlockForward_ComposesAndRuns() {
        List<CodeInstruction> instructions = BuildFinallyProbe(out _);

        List<CodeInstruction> output = new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(i => i.blocks.Exists(b => b.blockType == ExceptionBlockType.BeginExceptionBlock)))
            .RemoveInstruction()
            .InstructionEnumeration();

        Assert.Contains(output, i => i.blocks.Exists(b => b.blockType == ExceptionBlockType.BeginExceptionBlock));

        using DynamicMethodDefinition method = NewFinallyShapedMethod();
        TranspilerContext context = NewFinallyShapedContext();
        CecilCodeConverter.WriteBack(method.Definition, output, context);

        MethodInfo generated = method.Generate();
        Assert.Equal(7, generated.Invoke(null, [7]));
    }

    [Fact]
    public void RemoveInstructions_WholeFrameAtEndOfList_ComposesWithoutCONC118() {
        List<CodeInstruction> instructions = BuildFinallyProbe(out _);

        CodeMatcher matcher = new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(i => i.blocks.Exists(b => b.blockType == ExceptionBlockType.BeginExceptionBlock)))
            .RemoveInstructions(100);

        List<CodeInstruction> output = matcher.InstructionEnumeration();
        Assert.True(matcher.IsInvalid);
        Assert.DoesNotContain(output, i => i.blocks.Count > 0);

        // No valid Pos remains to Insert at (fix 4's documented gap), so the replacement tail is
        // appended directly to the working list - the escape hatch the class docs point to.
        output.Add(new CodeInstruction(OpCodes.Ldloc_0));
        output.Add(new CodeInstruction(OpCodes.Ret));

        using DynamicMethodDefinition method = NewFinallyShapedMethod();
        TranspilerContext context = NewFinallyShapedContext();
        CecilCodeConverter.WriteBack(method.Definition, output, context);

        MethodInfo generated = method.Generate();
        Assert.Equal(9, generated.Invoke(null, [9]));
    }

    [Fact]
    public void RemoveInstructions_HandlerCloseAtEndOfList_ThrowsCONC118InsteadOfCorruptingRegion() {
        List<CodeInstruction> instructions = BuildFinallyProbe(out _);

        List<CodeInstruction> output = new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(i => i.blocks.Exists(b => b.blockType == ExceptionBlockType.BeginFinallyBlock)))
            .RemoveInstructions(100)
            .InstructionEnumeration();

        Assert.Equal(4, output.Count);
        Assert.Contains(output, i => i.blocks.Exists(b => b.blockType == ExceptionBlockType.BeginExceptionBlock));

        using DynamicMethodDefinition method = NewFinallyShapedMethod();
        TranspilerContext context = NewFinallyShapedContext();

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => CecilCodeConverter.WriteBack(method.Definition, output, context));
        Assert.Equal("CONC118", ex.Code);
    }
}
