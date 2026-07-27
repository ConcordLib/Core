using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit.Tests;

public sealed class CecilCodeConverterRoundTripTests {
    [Theory]
    [InlineData(nameof(TranspilerTargets.Simple))]
    [InlineData(nameof(TranspilerTargets.Priced))]
    [InlineData(nameof(TranspilerTargets.MultiCatch))]
    [InlineData(nameof(TranspilerTargets.Filtered))]
    [InlineData(nameof(TranspilerTargets.Finally))]
    [InlineData(nameof(TranspilerTargets.Switched))]
    [InlineData(nameof(TranspilerTargets.Fault))]
    public void RoundTrip_IsStructurallyIdentical(string name) {
        MethodBase target = typeof(TranspilerTargets).GetMethod(name)!;
        using DynamicMethodDefinition expected = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition actual = new DynamicMethodDefinition(target);

        // Modern Roslyn no longer emits a `fault` region for `fixed` over a pinned local (verified:
        // it lowers to a plain pinned-local pin/unpin with no exception handling at all). `fault`
        // regions are effectively unreachable from C#, so the Fault case is graphed directly via
        // Cecil here to exercise the ExceptionHandlerType.Fault round-trip path specifically.
        if (name == nameof(TranspilerTargets.Fault)) {
            BuildSyntheticFaultRegion(expected.Definition);
            BuildSyntheticFaultRegion(actual.Definition);
        }

        TranspilerContext context = new TranspilerContext(target);
        List<CodeInstruction> instructions = CecilCodeConverter.ToInstructions(actual.Definition, context);
        CecilCodeConverter.WriteBack(actual.Definition, instructions, context);

        IlAssert.BodiesMatch(expected.Definition, actual.Definition);
    }

    [Fact]
    public void RoundTrip_GenericMethod_IsStructurallyIdentical() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Generic))!
            .MakeGenericMethod(typeof(string));
        using DynamicMethodDefinition expected = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition actual = new DynamicMethodDefinition(target);

        TranspilerContext context = new TranspilerContext(target);
        List<CodeInstruction> instructions = CecilCodeConverter.ToInstructions(actual.Definition, context);
        CecilCodeConverter.WriteBack(actual.Definition, instructions, context);

        IlAssert.BodiesMatch(expected.Definition, actual.Definition);
    }

    [Fact]
    public void ToInstructions_PreservesShortFormEncoding() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Simple))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);

        TranspilerContext context = new TranspilerContext(target);
        List<CodeInstruction> instructions = CecilCodeConverter.ToInstructions(source.Definition, context);

        Assert.Contains(instructions, i => i.opcode == System.Reflection.Emit.OpCodes.Ldarg_0);
        Assert.DoesNotContain(instructions, i => i.opcode == System.Reflection.Emit.OpCodes.Ldarg);
    }

    [Fact]
    public void ToInstructions_FilterRegion_ProducesFilterBlock() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Filtered))!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);

        TranspilerContext context = new TranspilerContext(target);
        List<CodeInstruction> instructions = CecilCodeConverter.ToInstructions(source.Definition, context);

        Assert.Contains(
            instructions,
            i => i.blocks.Exists(b => b.blockType == ExceptionBlockType.BeginExceptFilterBlock));
    }

    private static void BuildSyntheticFaultRegion(MethodDefinition definition) {
        MethodBody body = definition.Body;
        body.Instructions.Clear();
        body.ExceptionHandlers.Clear();
        body.Variables.Clear();

        VariableDefinition local = new VariableDefinition(definition.Module.TypeSystem.Int32);
        body.Variables.Add(local);

        ILProcessor il = body.GetILProcessor();
        Instruction ldarg = il.Create(OpCodes.Ldarg_0);
        Instruction stloc = il.Create(OpCodes.Stloc_0);
        Instruction ldloc = il.Create(OpCodes.Ldloc_0);
        Instruction leave = il.Create(OpCodes.Leave_S, ldloc);
        Instruction ldcFault = il.Create(OpCodes.Ldc_I4_0);
        Instruction stlocFault = il.Create(OpCodes.Stloc_0);
        Instruction endfinally = il.Create(OpCodes.Endfinally);
        Instruction ret = il.Create(OpCodes.Ret);

        il.Append(ldarg);
        il.Append(stloc);
        il.Append(leave);
        il.Append(ldcFault);
        il.Append(stlocFault);
        il.Append(endfinally);
        il.Append(ldloc);
        il.Append(ret);

        ExceptionHandler handler = new ExceptionHandler(ExceptionHandlerType.Fault) {
            TryStart = ldarg,
            TryEnd = ldcFault,
            HandlerStart = ldcFault,
            HandlerEnd = ldloc,
        };
        body.ExceptionHandlers.Add(handler);
    }
}
