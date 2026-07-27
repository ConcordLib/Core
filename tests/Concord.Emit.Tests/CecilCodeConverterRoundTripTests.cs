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
    [InlineData(nameof(TranspilerTargets.TryCatchFinally))]
    [InlineData(nameof(TranspilerTargets.TryCatchCatchFinally))]
    [InlineData(nameof(TranspilerTargets.TryNestedInCatch))]
    [InlineData(nameof(TranspilerTargets.TryNestedInFinally))]
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

        // Structural equality is not sufficient: a body can format identically to IlAssert while
        // still being invalid IL the CLR rejects at JIT time (this happened with nested try/catch/
        // finally regions sharing a TryStart before the envelope-nesting fix). Generate and actually
        // invoke both bodies so a structurally-plausible-but-broken result fails loudly here instead
        // of passing a comparison that can't see it.
        AssertExecutionMatches(target, expected, actual);
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

        MethodInfo expectedMethod = expected.Generate();
        MethodInfo actualMethod = actual.Generate();
        Assert.Equal(expectedMethod.Invoke(null, ["probe"]), actualMethod.Invoke(null, ["probe"]));
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

    [Fact]
    public void RoundTrip_Calli_PreservesCallSite() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        using DynamicMethodDefinition expected = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition actual = new DynamicMethodDefinition(target);

        // calli has no C# source form; a real CallSite is built directly via Cecil (matching how
        // BodiesMatch_CallSiteCallingConventionDiffers_Fails in IlAssertTests.cs already does this).
        BuildCalliBody(expected.Definition);
        BuildCalliBody(actual.Definition);

        TranspilerContext context = new TranspilerContext(target);
        List<CodeInstruction> instructions = CecilCodeConverter.ToInstructions(actual.Definition, context);

        Assert.Contains(instructions, i => i.operand is CallSiteSignature);

        CecilCodeConverter.WriteBack(actual.Definition, instructions, context);

        IlAssert.BodiesMatch(expected.Definition, actual.Definition);
    }

    private static void AssertExecutionMatches(MethodBase target, DynamicMethodDefinition expected, DynamicMethodDefinition actual) {
        MethodInfo expectedMethod = expected.Generate();
        MethodInfo actualMethod = actual.Generate();

        foreach (object?[] arguments in RepresentativeArgumentSets(target)) {
            object? expectedResult = expectedMethod.Invoke(null, arguments);
            object? actualResult = actualMethod.Invoke(null, arguments);
            Assert.Equal(expectedResult, actualResult);
        }
    }

    private static IEnumerable<object?[]> RepresentativeArgumentSets(MethodBase target) {
        ParameterInfo[] parameters = target.GetParameters();
        if (parameters.Length == 0) {
            yield return Array.Empty<object?>();
            yield break;
        }

        foreach (int value in new[] { 0, 1, 2, 5 }) {
            object?[] arguments = new object?[parameters.Length];
            for (int i = 0; i < arguments.Length; i++) {
                arguments[i] = value;
            }

            yield return arguments;
        }
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

    private static void BuildCalliBody(MethodDefinition definition) {
        MethodBody body = definition.Body;
        body.Instructions.Clear();

        ModuleDefinition module = definition.Module;
        CallSite callSite = new CallSite(module.TypeSystem.Int32) {
            CallingConvention = MethodCallingConvention.StdCall,
        };
        callSite.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));

        ILProcessor il = body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Conv_I));
        il.Append(il.Create(OpCodes.Calli, callSite));
        il.Append(il.Create(OpCodes.Ret));
    }
}
