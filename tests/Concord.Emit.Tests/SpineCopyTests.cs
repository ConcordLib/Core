using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit.Tests;

public sealed class SpineCopyTests {
    private static (ModuleDefinition Module, MethodDefinition Wrapper) NewWrapper(string name) {
        AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition($"SpineCopyTests.{name}", new Version(1, 0)),
            $"SpineCopyTests.{name}",
            ModuleKind.Dll);
        TypeDefinition type = new TypeDefinition("SpineCopyTests", "Wrapper", TypeAttributes.Public | TypeAttributes.Class, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(type);
        MethodDefinition wrapper = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, assembly.MainModule.TypeSystem.Int32);
        type.Methods.Add(wrapper);
        return (assembly.MainModule, wrapper);
    }

    private static (List<Instruction> Instructions, List<ExceptionHandler> Handlers, VariableDefinition Local) BuildTryCatch(ModuleDefinition module) {
        VariableDefinition local = new VariableDefinition(module.TypeSystem.Int32);
        Instruction tryStart = Instruction.Create(OpCodes.Ldc_I4_1);
        Instruction storeInTry = Instruction.Create(OpCodes.Stloc, local);
        Instruction tryEnd = Instruction.Create(OpCodes.Nop);
        Instruction handlerStart = Instruction.Create(OpCodes.Pop);
        Instruction storeInHandler = Instruction.Create(OpCodes.Stloc, local);
        Instruction handlerEnd = Instruction.Create(OpCodes.Nop);
        Instruction loadResult = Instruction.Create(OpCodes.Ldloc, local);
        Instruction ret = Instruction.Create(OpCodes.Ret);

        List<Instruction> instructions = [tryStart, storeInTry, tryEnd, handlerStart, storeInHandler, handlerEnd, loadResult, ret];

        ExceptionHandler handler = new ExceptionHandler(ExceptionHandlerType.Catch) {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = handlerEnd,
            CatchType = module.ImportReference(typeof(Exception)),
        };

        return (instructions, [handler], local);
    }

    private static (List<Instruction> Instructions, List<ExceptionHandler> Handlers, VariableDefinition Local) BuildTryCatchFilter(ModuleDefinition module) {
        VariableDefinition local = new VariableDefinition(module.TypeSystem.Int32);
        Instruction tryStart = Instruction.Create(OpCodes.Ldc_I4_1);
        Instruction storeInTry = Instruction.Create(OpCodes.Stloc, local);
        Instruction filterStart = Instruction.Create(OpCodes.Pop);
        Instruction loadInFilter = Instruction.Create(OpCodes.Ldloc, local);
        Instruction filterResult = Instruction.Create(OpCodes.Ldc_I4_1);
        Instruction endFilter = Instruction.Create(OpCodes.Endfilter);
        Instruction handlerStart = Instruction.Create(OpCodes.Pop);
        Instruction storeInHandler = Instruction.Create(OpCodes.Stloc, local);
        Instruction handlerEnd = Instruction.Create(OpCodes.Nop);
        Instruction loadResult = Instruction.Create(OpCodes.Ldloc, local);
        Instruction ret = Instruction.Create(OpCodes.Ret);

        List<Instruction> instructions = [
            tryStart, storeInTry, filterStart, loadInFilter, filterResult, endFilter,
            handlerStart, storeInHandler, handlerEnd, loadResult, ret,
        ];

        ExceptionHandler handler = new ExceptionHandler(ExceptionHandlerType.Filter) {
            TryStart = tryStart,
            TryEnd = filterStart,
            FilterStart = filterStart,
            HandlerStart = handlerStart,
            HandlerEnd = handlerEnd,
        };

        return (instructions, [handler], local);
    }

    private static (List<Instruction> Instructions, List<ExceptionHandler> Handlers, VariableDefinition Local) BuildTryFault(ModuleDefinition module) {
        VariableDefinition local = new VariableDefinition(module.TypeSystem.Int32);
        Instruction tryStart = Instruction.Create(OpCodes.Ldc_I4_1);
        Instruction storeInTry = Instruction.Create(OpCodes.Stloc, local);
        Instruction tryEnd = Instruction.Create(OpCodes.Nop);
        Instruction handlerStart = Instruction.Create(OpCodes.Ldloc, local);
        Instruction storeInHandler = Instruction.Create(OpCodes.Stloc, local);
        Instruction endFault = Instruction.Create(OpCodes.Endfinally);
        Instruction loadResult = Instruction.Create(OpCodes.Ldloc, local);
        Instruction ret = Instruction.Create(OpCodes.Ret);

        List<Instruction> instructions = [tryStart, storeInTry, tryEnd, handlerStart, storeInHandler, endFault, loadResult, ret];

        ExceptionHandler handler = new ExceptionHandler(ExceptionHandlerType.Fault) {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = loadResult,
        };

        return (instructions, [handler], local);
    }

    private static (List<Instruction> Instructions, List<ExceptionHandler> Handlers, VariableDefinition Local) BuildTryFinally(ModuleDefinition module) {
        VariableDefinition local = new VariableDefinition(module.TypeSystem.Int32);
        Instruction tryStart = Instruction.Create(OpCodes.Ldc_I4_1);
        Instruction storeInTry = Instruction.Create(OpCodes.Stloc, local);
        Instruction leave = Instruction.Create(OpCodes.Leave_S, Instruction.Create(OpCodes.Nop));
        Instruction handlerStart = Instruction.Create(OpCodes.Ldloc, local);
        Instruction storeInHandler = Instruction.Create(OpCodes.Stloc, local);
        Instruction endFinally = Instruction.Create(OpCodes.Endfinally);
        Instruction afterHandler = Instruction.Create(OpCodes.Ldloc, local);
        Instruction ret = Instruction.Create(OpCodes.Ret);
        leave.Operand = afterHandler;

        List<Instruction> instructions = [tryStart, storeInTry, leave, handlerStart, storeInHandler, endFinally, afterHandler, ret];

        ExceptionHandler handler = new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = afterHandler,
        };

        return (instructions, [handler], local);
    }

    private static (List<Instruction> Instructions, List<ExceptionHandler> Handlers, VariableDefinition Local) BuildNestedTryFinallyInsideTryCatch(ModuleDefinition module) {
        VariableDefinition local = new VariableDefinition(module.TypeSystem.Int32);

        Instruction outerTryStart = Instruction.Create(OpCodes.Ldc_I4_1);
        Instruction innerTryStart = Instruction.Create(OpCodes.Stloc, local);
        Instruction innerLeave = Instruction.Create(OpCodes.Leave_S, Instruction.Create(OpCodes.Nop));
        Instruction innerHandlerStart = Instruction.Create(OpCodes.Ldloc, local);
        Instruction innerStoreInHandler = Instruction.Create(OpCodes.Stloc, local);
        Instruction innerEndFinally = Instruction.Create(OpCodes.Endfinally);
        Instruction afterInnerHandler = Instruction.Create(OpCodes.Nop);
        Instruction outerTryEnd = Instruction.Create(OpCodes.Nop);
        Instruction outerHandlerStart = Instruction.Create(OpCodes.Pop);
        Instruction outerStoreInHandler = Instruction.Create(OpCodes.Stloc, local);
        Instruction outerHandlerEnd = Instruction.Create(OpCodes.Nop);
        Instruction loadResult = Instruction.Create(OpCodes.Ldloc, local);
        Instruction ret = Instruction.Create(OpCodes.Ret);
        innerLeave.Operand = afterInnerHandler;

        List<Instruction> instructions = [
            outerTryStart, innerTryStart, innerLeave, innerHandlerStart, innerStoreInHandler, innerEndFinally, afterInnerHandler,
            outerTryEnd, outerHandlerStart, outerStoreInHandler, outerHandlerEnd, loadResult, ret,
        ];

        ExceptionHandler innerFinally = new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart = innerTryStart,
            TryEnd = innerHandlerStart,
            HandlerStart = innerHandlerStart,
            HandlerEnd = afterInnerHandler,
        };

        ExceptionHandler outerCatch = new ExceptionHandler(ExceptionHandlerType.Catch) {
            TryStart = outerTryStart,
            TryEnd = outerHandlerStart,
            HandlerStart = outerHandlerStart,
            HandlerEnd = outerHandlerEnd,
            CatchType = module.ImportReference(typeof(Exception)),
        };

        return (instructions, [innerFinally, outerCatch], local);
    }

    private static void AssertCopyIndependenceAndBoundaries(
        (List<Instruction> Instructions, List<ExceptionHandler> Handlers, VariableDefinition Local) fixture) {
        (ModuleDefinition module, MethodDefinition wrapperA) = NewWrapper("A");
        (_, MethodDefinition wrapperB) = NewWrapper("B");

        SpineTemplate template = SpineTemplate.Capture(fixture.Instructions, fixture.Handlers, new HashSet<VariableDefinition>());

        SpineCopy copyA = SpineCopy.Create(template, wrapperA);
        SpineCopy copyB = SpineCopy.Create(template, wrapperB);

        Assert.Equal(fixture.Instructions.Count, copyA.Instructions.Count);
        Assert.Equal(fixture.Instructions.Count, copyB.Instructions.Count);

        for (int i = 0; i < copyA.Instructions.Count; i++) {
            Assert.NotSame(copyA.Instructions[i], copyB.Instructions[i]);
            Assert.NotSame(fixture.Instructions[i], copyA.Instructions[i]);
            Assert.NotSame(fixture.Instructions[i], copyB.Instructions[i]);
            Assert.Equal(copyA.Instructions[i].OpCode, copyB.Instructions[i].OpCode);
        }

        HashSet<VariableDefinition> localsA = CollectLocals(copyA.Instructions);
        HashSet<VariableDefinition> localsB = CollectLocals(copyB.Instructions);
        Assert.NotEmpty(localsA);
        Assert.NotEmpty(localsB);
        foreach (VariableDefinition variable in localsA) {
            Assert.DoesNotContain(variable, localsB);
            Assert.DoesNotContain(variable, fixture.Instructions.Select(GetVariableOperand).OfType<VariableDefinition>());
        }

        Assert.Equal(fixture.Handlers.Count, copyA.Handlers.Count);
        Assert.Equal(fixture.Handlers.Count, copyB.Handlers.Count);

        for (int i = 0; i < fixture.Handlers.Count; i++) {
            AssertHandlerResolvesWithinCopy(copyA.Handlers[i], copyA.Instructions);
            AssertHandlerResolvesWithinCopy(copyB.Handlers[i], copyB.Instructions);
            Assert.NotSame(copyA.Handlers[i], copyB.Handlers[i]);
        }

        bool anyProtected = false;
        foreach (Instruction instruction in copyA.Instructions) {
            if (WrapperComposer.IsInsideProtectedRegion(instruction, copyA.Handlers)) {
                anyProtected = true;
                break;
            }
        }

        Assert.True(anyProtected);
    }

    private static VariableDefinition? GetVariableOperand(Instruction instruction) {
        return instruction.Operand as VariableDefinition;
    }

    private static HashSet<VariableDefinition> CollectLocals(List<Instruction> instructions) {
        HashSet<VariableDefinition> locals = [];
        foreach (Instruction instruction in instructions) {
            if (instruction.Operand is VariableDefinition variable) {
                locals.Add(variable);
            }
        }

        return locals;
    }

    private static void AssertHandlerResolvesWithinCopy(ExceptionHandler handler, List<Instruction> copyInstructions) {
        if (handler.TryStart is not null) {
            Assert.Contains(handler.TryStart, copyInstructions);
        }

        if (handler.TryEnd is not null) {
            Assert.Contains(handler.TryEnd, copyInstructions);
        }

        if (handler.HandlerStart is not null) {
            Assert.Contains(handler.HandlerStart, copyInstructions);
        }

        if (handler.HandlerEnd is not null) {
            Assert.Contains(handler.HandlerEnd, copyInstructions);
        }

        if (handler.FilterStart is not null) {
            Assert.Contains(handler.FilterStart, copyInstructions);
        }
    }

    [Fact]
    public void Create_TryCatch_CopiesAreIndependent() {
        (ModuleDefinition module, MethodDefinition _) = NewWrapper("Probe");
        AssertCopyIndependenceAndBoundaries(BuildTryCatch(module));
    }

    [Fact]
    public void Create_TryCatchFilter_CopiesAreIndependent() {
        (ModuleDefinition module, MethodDefinition _) = NewWrapper("Probe");
        AssertCopyIndependenceAndBoundaries(BuildTryCatchFilter(module));
    }

    [Fact]
    public void Create_TryFault_CopiesAreIndependent() {
        (ModuleDefinition module, MethodDefinition _) = NewWrapper("Probe");
        AssertCopyIndependenceAndBoundaries(BuildTryFault(module));
    }

    [Fact]
    public void Create_TryFinally_CopiesAreIndependent() {
        (ModuleDefinition module, MethodDefinition _) = NewWrapper("Probe");
        AssertCopyIndependenceAndBoundaries(BuildTryFinally(module));
    }

    [Fact]
    public void Create_NestedTryFinallyInsideTryCatch_CopiesAreIndependent() {
        (ModuleDefinition module, MethodDefinition _) = NewWrapper("Probe");
        AssertCopyIndependenceAndBoundaries(BuildNestedTryFinallyInsideTryCatch(module));
    }

    [Fact]
    public void Create_TryCatchFilter_FilterStartResolvesWithinCopy() {
        (ModuleDefinition module, MethodDefinition _) = NewWrapper("Probe");
        (List<Instruction> instructions, List<ExceptionHandler> handlers, VariableDefinition _) = BuildTryCatchFilter(module);
        SpineTemplate template = SpineTemplate.Capture(instructions, handlers, new HashSet<VariableDefinition>());

        (ModuleDefinition _, MethodDefinition wrapperA) = NewWrapper("FilterA");
        SpineCopy copy = SpineCopy.Create(template, wrapperA);

        ExceptionHandler copiedHandler = Assert.Single(copy.Handlers);
        Assert.NotNull(copiedHandler.FilterStart);
        Assert.Contains(copiedHandler.FilterStart!, copy.Instructions);
        Assert.NotSame(handlers[0].FilterStart, copiedHandler.FilterStart);
    }

    [Fact]
    public void Create_NestedTryFinallyInsideTryCatch_NestedBoundariesStayNested() {
        (ModuleDefinition module, MethodDefinition _) = NewWrapper("Probe");
        (List<Instruction> instructions, List<ExceptionHandler> handlers, VariableDefinition _) = BuildNestedTryFinallyInsideTryCatch(module);
        SpineTemplate template = SpineTemplate.Capture(instructions, handlers, new HashSet<VariableDefinition>());

        (ModuleDefinition _, MethodDefinition wrapperA) = NewWrapper("NestedA");
        SpineCopy copy = SpineCopy.Create(template, wrapperA);

        ExceptionHandler copiedInner = copy.Handlers[0];
        ExceptionHandler copiedOuter = copy.Handlers[1];

        int outerTryStartIndex = copy.Instructions.IndexOf(copiedOuter.TryStart!);
        int outerTryEndIndex = copy.Instructions.IndexOf(copiedOuter.TryEnd!);
        int innerTryStartIndex = copy.Instructions.IndexOf(copiedInner.TryStart!);
        int innerHandlerEndIndex = copy.Instructions.IndexOf(copiedInner.HandlerEnd!);

        Assert.True(outerTryStartIndex < innerTryStartIndex);
        Assert.True(innerHandlerEndIndex <= outerTryEndIndex);

        Assert.True(WrapperComposer.IsInsideProtectedRegion(copiedInner.TryStart!, copy.Handlers));
        Assert.True(WrapperComposer.IsInsideProtectedRegion(copiedInner.HandlerStart!, copy.Handlers));
    }
}
