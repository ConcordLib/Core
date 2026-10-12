using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit.Tests;

public sealed class ComposedBodyFactoryTests {
    [Theory]
    [InlineData(nameof(TranspilerTargets.Simple))]
    [InlineData(nameof(TranspilerTargets.MultiCatch))]
    [InlineData(nameof(TranspilerTargets.Filtered))]
    [InlineData(nameof(TranspilerTargets.Finally))]
    [InlineData(nameof(TranspilerTargets.Switched))]
    [InlineData(nameof(TranspilerTargets.TryCatchFinally))]
    [InlineData(nameof(TranspilerTargets.TryCatchCatchFinally))]
    [InlineData(nameof(TranspilerTargets.TryNestedInCatch))]
    [InlineData(nameof(TranspilerTargets.TryNestedInFinally))]
    [InlineData(nameof(TranspilerTargets.AlwaysThrows))]
    [InlineData(nameof(TranspilerTargets.AlwaysThrowsFinally))]
    [InlineData(nameof(TranspilerTargets.Fault))]
    public void Clauses_RebuildTheSameCecilRegions(string name) {
        MethodBase target = typeof(TranspilerTargets).GetMethod(name)!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        AssertRegionsRebuild(source.Definition.Body, composed);
    }

    [Theory]
    [InlineData(nameof(TranspilerTargets.AlwaysThrows))]
    [InlineData(nameof(TranspilerTargets.AlwaysThrowsFinally))]
    public void HandlerRunningToEndOfBody_HasHandlerEndAtInstructionCount(string name) {
        MethodBase target = typeof(TranspilerTargets).GetMethod(name)!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        MethodBody body = source.Definition.Body;

        ExceptionHandler endOfBody = body.ExceptionHandlers[body.ExceptionHandlers.Count - 1];
        Assert.Null(endOfBody.HandlerEnd);
        Assert.Same(body.Instructions[body.Instructions.Count - 1], LastInstructionOfHandler(body, endOfBody));

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        ComposedClause clause = composed.Clauses[composed.Clauses.Count - 1];
        Assert.Equal(composed.Instructions.Count, clause.HandlerEnd);
        Assert.NotEqual(composed.Instructions.Count - 1, clause.HandlerEnd);
    }

    [Fact]
    public void HandBuiltHandlerRunningToEndOfBody_HasHandlerEndAtInstructionCount() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        BuildNullEndedFinally(source.Definition);

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        ComposedClause clause = Assert.Single(composed.Clauses);
        Assert.Equal(ClauseKind.Finally, clause.Kind);
        Assert.Equal(composed.Instructions.Count, clause.HandlerEnd);
        AssertRegionsRebuild(source.Definition.Body, composed);
    }

    [Fact]
    public void TryRegionEndingAtTheLastInstruction_KeepsTheExclusiveEnd() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        BuildTryEndingAtLastInstruction(source.Definition);

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        ComposedClause clause = Assert.Single(composed.Clauses);
        Assert.Equal(0, clause.TryStart);
        Assert.Equal(composed.Instructions.Count - 1, clause.TryEnd);
        Assert.Equal(composed.Instructions.Count - 1, clause.HandlerStart);
        Assert.Equal(composed.Instructions.Count, clause.HandlerEnd);
        AssertRegionsRebuild(source.Definition.Body, composed);
    }

    [Fact]
    public void FilterClause_PutsTheFilterBeforeTheHandler() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Filtered))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        ComposedClause filter = Assert.Single(composed.Clauses, c => c.Kind == ClauseKind.Filter);
        Assert.InRange(filter.FilterStart, filter.TryEnd, filter.HandlerStart - 1);
        Assert.Null(filter.CatchType);
    }

    [Fact]
    public void NonFilterClauses_HaveNoFilterStart() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.TryCatchCatchFinally))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        Assert.NotEmpty(composed.Clauses);
        foreach (ComposedClause clause in composed.Clauses) {
            Assert.NotEqual(ClauseKind.Filter, clause.Kind);
            Assert.Equal(-1, clause.FilterStart);
        }

        Assert.Equal(2, CountOf(composed, ClauseKind.Catch));
        Assert.Equal(1, CountOf(composed, ClauseKind.Finally));
        Assert.Contains(composed.Clauses, c => c.CatchType == typeof(InvalidOperationException));
        Assert.Contains(composed.Clauses, c => c.CatchType == typeof(ArgumentException));
    }

    [Fact]
    public void NestedClauses_KeepTheirEnclosingRanges() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.TryNestedInFinally))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        ComposedClause inner = Assert.Single(composed.Clauses, c => c.Kind == ClauseKind.Catch);
        ComposedClause outer = Assert.Single(composed.Clauses, c => c.Kind == ClauseKind.Finally);
        Assert.InRange(inner.TryStart, outer.HandlerStart, outer.HandlerEnd - 1);
        Assert.InRange(inner.HandlerEnd, outer.HandlerStart, outer.HandlerEnd);
    }

    [Fact]
    public void Locals_MatchTheBodySlotForSlot() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.TwoLocals))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        Assert.Equal(source.Definition.Body.Variables.Count, composed.Locals.Count);
        Assert.Equal(source.Definition.Body.InitLocals, composed.InitLocals);
        foreach (ComposedLocal local in composed.Locals) {
            Assert.Equal(typeof(int), local.Type);
            Assert.False(local.IsPinned);
        }
    }

    [Fact]
    public void PinnedLocal_IsFlaggedAndCarriesTheUnpinnedType() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Fault))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        MethodBody body = source.Definition.Body;

        int slot = body.Variables.ToList().FindIndex(v => v.IsPinned);
        Assert.InRange(slot, 0, body.Variables.Count - 1);
        Assert.IsType<PinnedType>(body.Variables[slot].VariableType);

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        Assert.True(composed.Locals[slot].IsPinned);
        Assert.Equal(typeof(int).MakeByRefType(), composed.Locals[slot].Type);
    }

    [Fact]
    public void HandBuiltPinnedAndModifiedLocals_ResolveToTheTypeUnderneath() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        ModuleDefinition module = source.Definition.Module;
        MethodBody body = source.Definition.Body;

        int pinnedSlot = body.Variables.Count;
        body.Variables.Add(new VariableDefinition(new PinnedType(module.ImportReference(typeof(int[])))));
        body.Variables.Add(new VariableDefinition(new OptionalModifierType(
            module.ImportReference(typeof(System.Runtime.CompilerServices.IsVolatile)),
            module.TypeSystem.Int32)));

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        Assert.Equal(typeof(int[]), composed.Locals[pinnedSlot].Type);
        Assert.True(composed.Locals[pinnedSlot].IsPinned);
        Assert.Equal(typeof(int), composed.Locals[pinnedSlot + 1].Type);
        Assert.False(composed.Locals[pinnedSlot + 1].IsPinned);
    }

    [Fact]
    public void Instructions_MirrorTheBodyOpcodeForOpcode() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Switched))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        MethodBody body = source.Definition.Body;

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        Assert.Equal(body.Instructions.Count, composed.Instructions.Count);
        for (int i = 0; i < composed.Instructions.Count; i++) {
            Assert.Equal(body.Instructions[i].OpCode.Name, composed.Instructions[i].Instruction.opcode.Name);
        }

        Assert.Contains(composed.Instructions, c => c.Instruction.operand is Label[]);
    }

    [Fact]
    public void Origins_AreNullWhereNothingWasRecorded() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Simple))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);

        ComposedBody composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));

        Assert.NotEmpty(composed.Instructions);
        foreach (ComposedInstruction instruction in composed.Instructions) {
            Assert.Null(instruction.OriginOffset);
        }
    }

    [Fact]
    public void Origins_LandOnTheInstructionTheyWereRecordedFor() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Finally))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        MethodBody body = source.Definition.Body;

        List<int?> expected = new List<int?>();
        InstructionOrigins.Enabled = true;
        ComposedBody composed;
        try {
            for (int i = 0; i < body.Instructions.Count; i++) {
                if (i % 3 == 0) {
                    InstructionOrigins.Record(body.Instructions[i], 1000 + i);
                    expected.Add(1000 + i);
                } else {
                    expected.Add(null);
                }
            }

            composed = ComposedBodyFactory.Create(source.Definition, new TranspilerContext(target));
        } finally {
            InstructionOrigins.Enabled = false;
        }

        Assert.Equal(expected.Count, composed.Instructions.Count);
        for (int i = 0; i < composed.Instructions.Count; i++) {
            Assert.Equal(expected[i], composed.Instructions[i].OriginOffset);
        }
    }

    private static int CountOf(ComposedBody composed, ClauseKind kind) {
        int count = 0;
        foreach (ComposedClause clause in composed.Clauses) {
            if (clause.Kind == kind) {
                count++;
            }
        }

        return count;
    }

    // Turns every clause back into the Cecil boundaries it came from and compares by reference, so a
    // clause that names one instruction too few fails here instead of silently truncating a region.
    private static void AssertRegionsRebuild(MethodBody body, ComposedBody composed) {
        Assert.Equal(body.ExceptionHandlers.Count, composed.Clauses.Count);
        Assert.Equal(body.Instructions.Count, composed.Instructions.Count);

        for (int i = 0; i < composed.Clauses.Count; i++) {
            ExceptionHandler expected = body.ExceptionHandlers[i];
            ComposedClause clause = composed.Clauses[i];

            Assert.Equal(KindOf(expected.HandlerType), clause.Kind);
            Assert.Same(expected.TryStart, body.Instructions[clause.TryStart]);
            Assert.Same(expected.TryEnd, Boundary(body, clause.TryEnd));
            Assert.Same(expected.HandlerStart, body.Instructions[clause.HandlerStart]);
            Assert.Same(expected.HandlerEnd, Boundary(body, clause.HandlerEnd));

            if (clause.Kind == ClauseKind.Filter) {
                Assert.Same(expected.FilterStart, body.Instructions[clause.FilterStart]);
            } else {
                Assert.Equal(-1, clause.FilterStart);
            }

            Type? catchType = expected.CatchType is null ? null : expected.CatchType.ResolveReflection();
            Assert.Equal(catchType, clause.CatchType);
        }
    }

    private static Instruction? Boundary(MethodBody body, int end) {
        return end == body.Instructions.Count ? null : body.Instructions[end];
    }

    private static ClauseKind KindOf(ExceptionHandlerType type) {
        return type switch {
            ExceptionHandlerType.Catch => ClauseKind.Catch,
            ExceptionHandlerType.Filter => ClauseKind.Filter,
            ExceptionHandlerType.Finally => ClauseKind.Finally,
            _ => ClauseKind.Fault,
        };
    }

    private static Instruction LastInstructionOfHandler(MethodBody body, ExceptionHandler handler) {
        Assert.Null(handler.HandlerEnd);
        return body.Instructions[body.Instructions.Count - 1];
    }

    private static void BuildNullEndedFinally(MethodDefinition definition) {
        MethodBody body = definition.Body;
        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();

        ILProcessor il = body.GetILProcessor();
        Instruction tryStart = il.Create(OpCodes.Nop);
        Instruction handlerStart = il.Create(OpCodes.Nop);
        Instruction endfinally = il.Create(OpCodes.Endfinally);

        il.Append(tryStart);
        il.Append(handlerStart);
        il.Append(endfinally);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = null,
        });
    }

    // The try stops one instruction short of the end and the finally fills the rest, so TryEnd is the
    // last real index while HandlerEnd has no instruction to name.
    private static void BuildTryEndingAtLastInstruction(MethodDefinition definition) {
        MethodBody body = definition.Body;
        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();

        ILProcessor il = body.GetILProcessor();
        Instruction tryStart = il.Create(OpCodes.Nop);
        Instruction tryBody = il.Create(OpCodes.Nop);
        Instruction endfinally = il.Create(OpCodes.Endfinally);

        il.Append(tryStart);
        il.Append(tryBody);
        il.Append(endfinally);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart = tryStart,
            TryEnd = endfinally,
            HandlerStart = endfinally,
            HandlerEnd = null,
        });
    }
}
