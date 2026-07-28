using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;
using MethodCallingConvention = Mono.Cecil.MethodCallingConvention;

namespace Concord.Emit.Tests;

public sealed class IlAssertTests {
    [Fact]
    public void BodiesMatch_IdenticalBodies_Passes() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(target);

        IlAssert.BodiesMatch(a.Definition, b.Definition);
    }

    [Fact]
    public void BodiesMatch_DifferentBodies_Fails() {
        MethodBase add = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        MethodBase bump = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Bump))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(add);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(bump);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => IlAssert.BodiesMatch(a.Definition, b.Definition));
    }

    [Fact]
    public void BodiesMatch_InitLocalsDiffers_Fails() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(target);

        b.Definition.Body.InitLocals = !b.Definition.Body.InitLocals;

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => IlAssert.BodiesMatch(a.Definition, b.Definition));
    }

    [Fact]
    public void BodiesMatch_LocalVariableTypeDiffers_Fails() {
        MethodBase target = typeof(Counterish).GetMethod(nameof(Counterish.Step))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(target);

        Assert.NotEmpty(b.Definition.Body.Variables);
        VariableDefinition var0 = b.Definition.Body.Variables[0];
        var0.VariableType = b.Definition.Module.ImportReference(typeof(long));

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => IlAssert.BodiesMatch(a.Definition, b.Definition));
    }

    [Fact]
    public void BodiesMatch_ExceptionHandlerOrderDiffers_Fails() {
        MethodBase target = typeof(ExceptionHandlerTargets).GetMethod(nameof(ExceptionHandlerTargets.WithExceptionHandlers))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(target);

        Assert.True(b.Definition.Body.ExceptionHandlers.Count >= 2, "Target must declare at least two exception handlers to exercise ordering.");
        ExceptionHandler first = b.Definition.Body.ExceptionHandlers[0];
        ExceptionHandler second = b.Definition.Body.ExceptionHandlers[1];
        b.Definition.Body.ExceptionHandlers[0] = second;
        b.Definition.Body.ExceptionHandlers[1] = first;

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => IlAssert.BodiesMatch(a.Definition, b.Definition));
    }

    [Fact]
    public void BodiesMatch_ExceptionHandlerCatchTypeDiffers_Fails() {
        MethodBase target = typeof(ExceptionHandlerTargets).GetMethod(nameof(ExceptionHandlerTargets.WithExceptionHandlers))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(target);

        Assert.NotEmpty(b.Definition.Body.ExceptionHandlers);
        ExceptionHandler handler = b.Definition.Body.ExceptionHandlers[0];
        Assert.NotNull(handler.CatchType);
        handler.CatchType = b.Definition.Module.ImportReference(typeof(InvalidOperationException));

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => IlAssert.BodiesMatch(a.Definition, b.Definition));
    }

    [Fact]
    public void BodiesMatch_OpcodeShortVsLongDiffers_Fails() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(target);

        bool found = false;
        foreach (Instruction instr in b.Definition.Body.Instructions) {
            if (instr.OpCode == OpCodes.Ldarg_0) {
                instr.OpCode = OpCodes.Ldarg;
                instr.Operand = b.Definition.Parameters[0];
                found = true;
                break;
            }
        }

        Assert.True(found, "Target must contain an Ldarg_0 instruction to exercise short-vs-long opcode divergence.");
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => IlAssert.BodiesMatch(a.Definition, b.Definition));
    }

    [Fact]
    public void BodiesMatch_StringOperandQuoted_DistinguishesFromNull() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(target);

        Instruction instrA = a.Definition.Body.Instructions[0];
        Instruction instrB = b.Definition.Body.Instructions[0];

        instrA.OpCode = OpCodes.Ldstr;
        instrA.Operand = "-";

        instrB.OpCode = OpCodes.Nop;
        instrB.Operand = null;

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => IlAssert.BodiesMatch(a.Definition, b.Definition));
    }

    [Fact]
    public void BodiesMatch_CallSiteCallingConventionDiffers_Fails() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(target);

        CallSite siteA = new CallSite(b.Definition.Module.ImportReference(typeof(int))) {
            CallingConvention = (MethodCallingConvention)0x0040
        };
        CallSite siteB = new CallSite(b.Definition.Module.ImportReference(typeof(int))) {
            CallingConvention = (MethodCallingConvention)0x0080
        };

        Instruction lastA = a.Definition.Body.Instructions[a.Definition.Body.Instructions.Count - 1];
        lastA.OpCode = OpCodes.Calli;
        lastA.Operand = siteA;

        Instruction lastB = b.Definition.Body.Instructions[b.Definition.Body.Instructions.Count - 1];
        lastB.OpCode = OpCodes.Calli;
        lastB.Operand = siteB;

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => IlAssert.BodiesMatch(a.Definition, b.Definition));
    }
}
