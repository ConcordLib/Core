using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class HandlerEndTrimmerTests {
    [Fact]
    public void FinallyEndingOnATrailingNop_ShrinksBackToTheEndfinally() {
        using DynamicMethodDefinition host = new DynamicMethodDefinition("TrimFinally", typeof(void), Type.EmptyTypes);
        MethodBody body = host.Definition.Body;
        body.Instructions.Clear();

        Instruction tryStart = Instruction.Create(OpCodes.Nop);
        Instruction leave = Instruction.Create(OpCodes.Nop);
        Instruction handlerStart = Instruction.Create(OpCodes.Nop);
        Instruction endFinally = Instruction.Create(OpCodes.Endfinally);
        Instruction stray = Instruction.Create(OpCodes.Nop);
        Instruction ret = Instruction.Create(OpCodes.Ret);
        foreach (Instruction instruction in new[] { tryStart, leave, handlerStart, endFinally, stray, ret }) {
            body.Instructions.Add(instruction);
        }

        leave.OpCode = OpCodes.Leave;
        leave.Operand = ret;

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = ret,
        });

        HandlerEndTrimmer.Apply(host.Definition);

        Assert.Same(stray, body.ExceptionHandlers[0].HandlerEnd);
    }

    [Fact]
    public void FinallyAlreadyEndingOnEndfinally_IsLeftAlone() {
        using DynamicMethodDefinition host = new DynamicMethodDefinition("KeepFinally", typeof(void), Type.EmptyTypes);
        MethodBody body = host.Definition.Body;
        body.Instructions.Clear();

        Instruction tryStart = Instruction.Create(OpCodes.Nop);
        Instruction leave = Instruction.Create(OpCodes.Nop);
        Instruction handlerStart = Instruction.Create(OpCodes.Nop);
        Instruction endFinally = Instruction.Create(OpCodes.Endfinally);
        Instruction ret = Instruction.Create(OpCodes.Ret);
        foreach (Instruction instruction in new[] { tryStart, leave, handlerStart, endFinally, ret }) {
            body.Instructions.Add(instruction);
        }

        leave.OpCode = OpCodes.Leave;
        leave.Operand = ret;

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = ret,
        });

        HandlerEndTrimmer.Apply(host.Definition);

        Assert.Same(ret, body.ExceptionHandlers[0].HandlerEnd);
    }
}
