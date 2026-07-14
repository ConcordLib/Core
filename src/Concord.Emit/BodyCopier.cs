using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

/// <summary>
///     Copies Cecil method bodies into generated wrappers while remapping Concord control calls.
/// </summary>
internal static class BodyCopier {
    /// <summary>
    ///     Copies the original target body into a destination dynamic method definition.
    /// </summary>
    /// <param name="source">The source target method definition.</param>
    /// <param name="destination">The destination wrapper method definition.</param>
    public static void CopySpine(MethodDefinition source, MethodDefinition destination) {
        MethodBody sourceBody = source.Body;
        MethodBody destinationBody = destination.Body;

        destinationBody.Instructions.Clear();
        destinationBody.Variables.Clear();
        destinationBody.ExceptionHandlers.Clear();
        destinationBody.InitLocals = sourceBody.InitLocals;

        ModuleDefinition module = destination.Module;
        Dictionary<VariableDefinition, VariableDefinition> variableMap = AppendVariables(sourceBody, destinationBody, module);

        ILProcessor il = destinationBody.GetILProcessor();
        Dictionary<Instruction, Instruction> instructionMap = new Dictionary<Instruction, Instruction>(sourceBody.Instructions.Count);

        InjectedMemberMap emptyMembers = new InjectedMemberMap(new Dictionary<string, FieldInfo>(), new Dictionary<string, MethodInfo?>());
        foreach (Instruction source_instruction in sourceBody.Instructions) {
            Instruction copy = CloneInstruction(source_instruction, module, variableMap, emptyMembers);
            instructionMap[source_instruction] = copy;
            il.Append(copy);
        }

        foreach (Instruction copy in destinationBody.Instructions) {
            RemapBranchTargets(copy, instructionMap);
        }

        foreach (ExceptionHandler handler in sourceBody.ExceptionHandlers) {
            destinationBody.ExceptionHandlers.Add(CloneHandler(handler, instructionMap, module));
        }
    }

    /// <summary>
    ///     Copies an injection method body into a wrapper instruction list.
    /// </summary>
    /// <param name="injectionDefinition">The Cecil definition for the injection method.</param>
    /// <param name="destination">The destination wrapper method definition.</param>
    /// <param name="target">The target method being patched.</param>
    /// <param name="injectionMethod">The reflection method for the injection method body.</param>
    /// <param name="injectedMembers">The resolved injected-member map for the injection's declaring type.</param>
    /// <param name="locals">Wrapper locals used for cancellation and return-value protocol state.</param>
    /// <param name="returnBranchTarget">The instruction to branch to when the injection method returns.</param>
    /// <param name="spineTemplate">The whole-method Around spine template to splice a fresh copy from at each Invoke site, if any.</param>
    /// <param name="spineCopies">Collects one <see cref="SpineCopy" /> per Invoke site spliced while copying, if any.</param>
    /// <returns>The copied and lowered instruction sequence.</returns>
    public static List<Instruction> CopyInjection(
        MethodDefinition injectionDefinition,
        MethodDefinition destination,
        MethodBase target,
        MethodBase injectionMethod,
        InjectedMemberMap injectedMembers,
        ProtocolLocals locals,
        Instruction returnBranchTarget,
        SpineTemplate? spineTemplate = null,
        List<SpineCopy>? spineCopies = null) {
        MethodBody injectionBody = injectionDefinition.Body;
        ModuleDefinition module = destination.Module;

        Dictionary<int, int> argRemap = BuildArgRemap(target, injectionMethod);
        int controlHandleArgIndex = ControlHandleLowering.FindControlHandleArgIndex(injectionMethod);
        int operationArgIndex = ControlHandleLowering.FindOperationArgIndex(injectionMethod);

        Dictionary<VariableDefinition, VariableDefinition> variableMap = CopyInjectionLocals(injectionBody, destination.Body, module);
        LoweringContext ctx = new LoweringContext(module, variableMap, injectionBody.Variables, injectedMembers, argRemap, destination.Body.Variables);

        List<(Instruction Source, List<Instruction> Emitted)> entries =
            new List<(Instruction Source, List<Instruction> Emitted)>(injectionBody.Instructions.Count);

        foreach (Instruction source_instruction in injectionBody.Instructions) {
            List<Instruction> emitted = LowerInstruction(
                source_instruction,
                ctx,
                controlHandleArgIndex,
                operationArgIndex,
                locals,
                returnBranchTarget,
                injectionMethod,
                target,
                spineTemplate,
                destination,
                spineCopies);
            entries.Add((source_instruction, emitted));
        }

        Dictionary<Instruction, Instruction> instructionMap = BuildInstructionMap(entries);

        List<Instruction> result = new List<Instruction>(injectionBody.Instructions.Count);
        foreach ((Instruction _, List<Instruction> emitted) in entries) {
            foreach (Instruction copy in emitted) {
                RemapBranchTargets(copy, instructionMap);
                result.Add(copy);
            }
        }

        CopyInjectionHandlers(injectionBody, destination.Body, instructionMap, module);

        return result;
    }

    /// <summary>
    ///     Copies a <c>T M(T original)</c> value-injection body: the leading parameter's loads read from
    ///     <paramref name="valueLocal" />, and every <c>ret</c> lowers to storing the returned value into a fresh
    ///     result local and branching to a trailing splice-end block that reloads it, leaving exactly the
    ///     replacement value on the evaluation stack.
    /// </summary>
    public static List<Instruction> CopyValueInjection(
        MethodDefinition injectionDefinition,
        MethodDefinition destination,
        MethodBase target,
        MethodBase injectionMethod,
        InjectedMemberMap injectedMembers,
        VariableDefinition valueLocal) {
        MethodBody injectionBody = injectionDefinition.Body;
        ModuleDefinition module = destination.Module;

        Dictionary<int, int> argRemap = BuildArgRemap(target, injectionMethod);
        int valueArgIndex = injectionMethod.IsStatic ? 0 : 1;
        argRemap.Remove(valueArgIndex);

        VariableDefinition resultLocal = new VariableDefinition(valueLocal.VariableType);
        destination.Body.Variables.Add(resultLocal);
        destination.Body.InitLocals = true;

        Instruction spliceEnd = Instruction.Create(OpCodes.Nop);

        Dictionary<VariableDefinition, VariableDefinition> variableMap = CopyInjectionLocals(injectionBody, destination.Body, module);
        LoweringContext ctx = new LoweringContext(module, variableMap, injectionBody.Variables, injectedMembers, argRemap, destination.Body.Variables);

        List<(Instruction Source, List<Instruction> Emitted)> entries =
            new List<(Instruction Source, List<Instruction> Emitted)>(injectionBody.Instructions.Count);

        foreach (Instruction source_instruction in injectionBody.Instructions) {
            List<Instruction> emitted = LowerValueInstruction(source_instruction, ctx, valueArgIndex, valueLocal, resultLocal, spliceEnd);
            entries.Add((source_instruction, emitted));
        }

        Dictionary<Instruction, Instruction> instructionMap = BuildInstructionMap(entries);

        List<Instruction> result = new List<Instruction>(injectionBody.Instructions.Count + 2);
        foreach ((Instruction _, List<Instruction> emitted) in entries) {
            foreach (Instruction copy in emitted) {
                RemapBranchTargets(copy, instructionMap);
                result.Add(copy);
            }
        }

        CopyInjectionHandlers(injectionBody, destination.Body, instructionMap, module);

        result.Add(spliceEnd);
        result.Add(Instruction.Create(OpCodes.Ldloc, resultLocal));

        return result;
    }

    /// <summary>
    ///     Copies a call-site wrap injection method body and lowers Operation.Invoke calls to the original call.
    /// </summary>
    /// <param name="injectionDefinition">The Cecil definition for the injection method.</param>
    /// <param name="destination">The destination wrapper method definition.</param>
    /// <param name="target">The target method being patched.</param>
    /// <param name="injectionMethod">The reflection method for the injection method body.</param>
    /// <param name="injectedMembers">The resolved injected-member map for the injection's declaring type.</param>
    /// <param name="wrapEnd">The instruction to branch to when the wrap injection method returns.</param>
    /// <param name="originalCall">The original call-site method reference.</param>
    /// <param name="receiverLocal">The spilled receiver local, or <see langword="null" /> for a static call site.</param>
    /// <param name="argLocals">The spilled argument locals, in call-site parameter order.</param>
    /// <param name="originalOpCode">The original call site's opcode (<c>call</c> or <c>callvirt</c>).</param>
    /// <param name="shape">The matched call site's resolved shape.</param>
    /// <returns>The copied and lowered instruction sequence.</returns>
    public static List<Instruction> CopyWrapInjection(
        MethodDefinition injectionDefinition,
        MethodDefinition destination,
        MethodBase target,
        MethodBase injectionMethod,
        InjectedMemberMap injectedMembers,
        Instruction wrapEnd,
        MethodReference originalCall,
        VariableDefinition? receiverLocal,
        IReadOnlyList<VariableDefinition> argLocals,
        OpCode originalOpCode,
        CallSiteShape shape) {
        MethodBody injectionBody = injectionDefinition.Body;
        ModuleDefinition module = destination.Module;

        Dictionary<int, VariableDefinition> wrapArgBinding = BuildWrapArgBinding(injectionMethod, argLocals, shape);
        Dictionary<int, int> thisRemap = injectionMethod.IsStatic ? new Dictionary<int, int>() : new Dictionary<int, int> { [0] = 0 };
        int operationArgIndex = ControlHandleLowering.FindOperationArgIndex(injectionMethod);

        MethodBase resolvedOriginal = originalCall.ResolveReflection();
        MethodReference importedOriginal = resolvedOriginal is ConstructorInfo originalConstructor
            ? module.ImportReference(originalConstructor)
            : module.ImportReference((MethodInfo)resolvedOriginal);

        ParameterInfo[] originalParameters = resolvedOriginal.GetParameters();
        Type[] invokeParameterTypes = new Type[originalParameters.Length];
        for (int i = 0; i < originalParameters.Length; i++) {
            invokeParameterTypes[i] = originalParameters[i].ParameterType;
        }

        Dictionary<VariableDefinition, VariableDefinition> variableMap = CopyInjectionLocals(injectionBody, destination.Body, module);
        LoweringContext ctx = new LoweringContext(module, variableMap, injectionBody.Variables, injectedMembers, thisRemap, destination.Body.Variables);

        List<(Instruction Source, List<Instruction> Emitted)> entries =
            new List<(Instruction Source, List<Instruction> Emitted)>(injectionBody.Instructions.Count);

        foreach (Instruction source_instruction in injectionBody.Instructions) {
            List<Instruction> emitted = LowerWrapInstruction(
                source_instruction,
                ctx,
                operationArgIndex,
                wrapEnd,
                importedOriginal,
                receiverLocal,
                invokeParameterTypes,
                originalOpCode,
                wrapArgBinding);
            entries.Add((source_instruction, emitted));
        }

        Dictionary<Instruction, Instruction> instructionMap = BuildInstructionMap(entries);

        List<Instruction> result = new List<Instruction>(injectionBody.Instructions.Count);
        foreach ((Instruction _, List<Instruction> emitted) in entries) {
            foreach (Instruction copy in emitted) {
                RemapBranchTargets(copy, instructionMap);
                result.Add(copy);
            }
        }

        CopyInjectionHandlers(injectionBody, destination.Body, instructionMap, module);

        return result;
    }

    private static void CopyInjectionHandlers(
        MethodBody injectionBody,
        MethodBody destinationBody,
        Dictionary<Instruction, Instruction> instructionMap,
        ModuleDefinition module) {
        foreach (ExceptionHandler handler in injectionBody.ExceptionHandlers) {
            destinationBody.ExceptionHandlers.Add(CloneHandler(handler, instructionMap, module));
        }
    }

    private static List<Instruction> LowerWrapInstruction(
        Instruction source,
        LoweringContext ctx,
        int operationArgIndex,
        Instruction wrapEnd,
        MethodReference importedOriginal,
        VariableDefinition? receiverLocal,
        IReadOnlyList<Type> invokeParameterTypes,
        OpCode originalOpCode,
        Dictionary<int, VariableDefinition> wrapArgBinding) {
        if (ControlHandleLowering.IsOperationReceiverLoad(source, operationArgIndex)) {
            return new List<Instruction>(0);
        }

        if (ControlHandleLowering.IsOperationInvoke(source)) {
            return LowerOperationInvoke(ctx, importedOriginal, receiverLocal, invokeParameterTypes, originalOpCode);
        }

        if (source.OpCode == OpCodes.Ret) {
            return new List<Instruction> { Instruction.Create(OpCodes.Br, wrapEnd) };
        }

        if (TryLowerWrapArgBinding(source, wrapArgBinding, out Instruction? bound)) {
            return new List<Instruction> { bound! };
        }

        if (TryLowerProjectedMethodCall(source, ctx, out List<Instruction>? projectedCall)) {
            return projectedCall;
        }

        if (TryNormalizeLocal(source, ctx.VariableMap, ctx.InjectionMethodLocals, out Instruction? local)) {
            return new List<Instruction> { local! };
        }

        Instruction copy = CloneInstruction(source, ctx.Module, ctx.VariableMap, ctx.InjectedMembers);
        RemapArgInstruction(copy, ctx.ArgRemap);
        return new List<Instruction> { copy };
    }

    private static bool TryLowerWrapArgBinding(Instruction source, Dictionary<int, VariableDefinition> wrapArgBinding, out Instruction? lowered) {
        lowered = null;

        bool isAddress = source.OpCode == OpCodes.Ldarga || source.OpCode == OpCodes.Ldarga_S;
        bool isLoad = IsLoadArgOpCode(source.OpCode);
        if (!isAddress && !isLoad) {
            return false;
        }

        int argIndex = GetArgIndex(source);
        if (argIndex < 0 || !wrapArgBinding.TryGetValue(argIndex, out VariableDefinition? local)) {
            return false;
        }

        lowered = Instruction.Create(isAddress ? OpCodes.Ldloca : OpCodes.Ldloc, local);
        return true;
    }

    private static bool IsLoadArgOpCode(OpCode opCode) {
        return opCode == OpCodes.Ldarg
            || opCode == OpCodes.Ldarg_S
            || opCode == OpCodes.Ldarg_0
            || opCode == OpCodes.Ldarg_1
            || opCode == OpCodes.Ldarg_2
            || opCode == OpCodes.Ldarg_3;
    }

    private static List<VariableDefinition> SpillInvokeArgs(LoweringContext ctx, MethodBase target, List<Instruction> spilled) {
        ParameterInfo[] targetParameters = target.GetParameters();

        List<VariableDefinition> argLocals = new List<VariableDefinition>(targetParameters.Length);
        for (int i = 0; i < targetParameters.Length; i++) {
            VariableDefinition local = new VariableDefinition(ctx.Module.ImportReference(targetParameters[i].ParameterType));
            ctx.DestinationVariables.Add(local);
            argLocals.Add(local);
        }

        for (int i = argLocals.Count - 1; i >= 0; i--) {
            spilled.Add(Instruction.Create(OpCodes.Stloc, argLocals[i]));
        }

        return argLocals;
    }

    private static void RewriteSpliceArgs(List<Instruction> spliceBody, Dictionary<int, VariableDefinition> argLocals) {
        foreach (Instruction instruction in spliceBody) {
            bool isAddress = instruction.OpCode == OpCodes.Ldarga || instruction.OpCode == OpCodes.Ldarga_S;
            bool isStore = instruction.OpCode == OpCodes.Starg || instruction.OpCode == OpCodes.Starg_S;
            bool isLoad = IsLoadArgOpCode(instruction.OpCode);
            if (!isAddress && !isStore && !isLoad) {
                continue;
            }

            int argIndex = GetArgIndex(instruction);
            if (argIndex < 0 || !argLocals.TryGetValue(argIndex, out VariableDefinition? local)) {
                continue;
            }

            instruction.OpCode = isAddress ? OpCodes.Ldloca : isStore ? OpCodes.Stloc : OpCodes.Ldloc;
            instruction.Operand = local;
        }
    }

    /// <summary>
    ///     Lowers an <c>Operation.Invoke</c> call: pops the invoke's evaluated arguments into fresh temps,
    ///     re-pushes the spilled receiver and the temps in call order, then emits the original call.
    /// </summary>
    private static List<Instruction> LowerOperationInvoke(
        LoweringContext ctx,
        MethodReference importedOriginal,
        VariableDefinition? receiverLocal,
        IReadOnlyList<Type> invokeParameterTypes,
        OpCode originalOpCode) {
        List<Instruction> emitted = new List<Instruction>();

        List<VariableDefinition> temps = new List<VariableDefinition>(invokeParameterTypes.Count);
        for (int i = 0; i < invokeParameterTypes.Count; i++) {
            VariableDefinition temp = new VariableDefinition(ctx.Module.ImportReference(invokeParameterTypes[i]));
            ctx.DestinationVariables.Add(temp);
            temps.Add(temp);
        }

        for (int i = temps.Count - 1; i >= 0; i--) {
            emitted.Add(Instruction.Create(OpCodes.Stloc, temps[i]));
        }

        if (receiverLocal is not null) {
            emitted.Add(Instruction.Create(OpCodes.Ldloc, receiverLocal));
        }

        for (int i = 0; i < temps.Count; i++) {
            emitted.Add(Instruction.Create(OpCodes.Ldloc, temps[i]));
        }

        emitted.Add(Instruction.Create(originalOpCode, importedOriginal));
        return emitted;
    }

    private static List<Instruction> LowerInstruction(
        Instruction source,
        LoweringContext ctx,
        int controlHandleArgIndex,
        int operationArgIndex,
        ProtocolLocals locals,
        Instruction returnBranchTarget,
        MethodBase injectionMethod,
        MethodBase? target = null,
        SpineTemplate? spineTemplate = null,
        MethodDefinition? destination = null,
        List<SpineCopy>? spineCopies = null) {
        if (ControlHandleLowering.IsControlHandleReceiverLoad(source, controlHandleArgIndex)) {
            EnsureNotStrayControlHandleUse(source, controlHandleArgIndex, target, injectionMethod);
            return new List<Instruction>(0);
        }

        if (IsControlHandleDup(source, controlHandleArgIndex)) {
            EnsureNotStrayControlHandleUse(source, controlHandleArgIndex, target, injectionMethod);
            return new List<Instruction>(0);
        }

        if (spineTemplate is not null && ControlHandleLowering.IsOperationReceiverLoad(source, operationArgIndex)) {
            EnsureNotStrayOperationUse(source, operationArgIndex, injectionMethod);
            return new List<Instruction>(0);
        }

        if (spineTemplate is not null && IsOperationDup(source, operationArgIndex)) {
            EnsureNotStrayOperationUse(source, operationArgIndex, injectionMethod);
            return new List<Instruction>(0);
        }

        if (target is not null && spineTemplate is not null && ControlHandleLowering.IsOperationInvoke(source)) {
            SpineCopy spineCopy = SpineCopy.Create(spineTemplate, destination!);
            spineCopies!.Add(spineCopy);

            List<Instruction> spliced = new List<Instruction>(target.GetParameters().Length + spineCopy.Instructions.Count);
            List<VariableDefinition> argLocals = SpillInvokeArgs(ctx, target, spliced);

            int targetOffset = target.IsStatic ? 0 : 1;
            for (int i = 0; i < argLocals.Count; i++) {
                spineCopy.ArgLocals[i + targetOffset] = argLocals[i];
            }

            RewriteSpliceArgs(spineCopy.Instructions, spineCopy.ArgLocals);

            spliced.AddRange(spineCopy.Instructions);
            return spliced;
        }

        if (target is not null && spineTemplate is not null && ControlHandleLowering.IsOriginalBodySpliceCall(source, target)) {
            int consumed = target.GetParameters().Length + (target.IsStatic ? 0 : 1);
            EnsureVerbatimArgForwards(source, consumed, injectionMethod);

            SpineCopy spineCopy = SpineCopy.Create(spineTemplate, destination!);
            spineCopies!.Add(spineCopy);

            List<Instruction> spliced = new List<Instruction>(consumed + spineCopy.Instructions.Count);
            for (int p = 0; p < consumed; p++) {
                spliced.Add(Instruction.Create(OpCodes.Pop));
            }

            spliced.AddRange(spineCopy.Instructions);
            return spliced;
        }

        ControlHandleLowering.ControlCallKind controlCall = ControlHandleLowering.ClassifyCall(source);
        if (controlCall == ControlHandleLowering.ControlCallKind.Cancel) {
            return new List<Instruction> { Instruction.Create(OpCodes.Ldc_I4_1), Instruction.Create(OpCodes.Stloc, locals.Cancel) };
        }

        if (controlCall == ControlHandleLowering.ControlCallKind.GetReturnValue) {
            return new List<Instruction> { Instruction.Create(OpCodes.Ldloc, locals.ReturnValue!) };
        }

        if (controlCall == ControlHandleLowering.ControlCallKind.SetReturnValue) {
            return new List<Instruction> {
                Instruction.Create(OpCodes.Stloc, locals.ReturnValue!),
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Stloc, locals.HasReturn!),
            };
        }

        if (source.OpCode == OpCodes.Ret) {
            if (spineTemplate is not null && locals.ReturnValue is not null) {
                return new List<Instruction> {
                    Instruction.Create(OpCodes.Stloc, locals.ReturnValue), Instruction.Create(OpCodes.Br, returnBranchTarget),
                };
            }

            if (ControlHandleLowering.ReturnsControl(injectionMethod)) {
                return new List<Instruction> {
                    Instruction.Create(OpCodes.Ldloc, locals.Cancel),
                    Instruction.Create(OpCodes.Or),
                    Instruction.Create(OpCodes.Stloc, locals.Cancel),
                    Instruction.Create(OpCodes.Br, returnBranchTarget),
                };
            }

            return new List<Instruction> { Instruction.Create(OpCodes.Br, returnBranchTarget) };
        }

        if (TryLowerProjectedMethodCall(source, ctx, out List<Instruction>? projectedCall)) {
            return projectedCall;
        }

        if (TryNormalizeLocal(source, ctx.VariableMap, ctx.InjectionMethodLocals, out Instruction? local)) {
            return new List<Instruction> { local! };
        }

        Instruction copy = CloneInstruction(source, ctx.Module, ctx.VariableMap, ctx.InjectedMembers);
        RemapArgInstruction(copy, ctx.ArgRemap);
        return new List<Instruction> { copy };
    }

    private static List<Instruction> LowerValueInstruction(
        Instruction source,
        LoweringContext ctx,
        int valueArgIndex,
        VariableDefinition valueLocal,
        VariableDefinition resultLocal,
        Instruction spliceEnd) {
        if (IsLoadArgOpCode(source.OpCode) && GetArgIndex(source) == valueArgIndex) {
            return new List<Instruction> { Instruction.Create(OpCodes.Ldloc, valueLocal) };
        }

        int argIndex = GetArgIndex(source);
        bool isAddressOfValue = (source.OpCode == OpCodes.Ldarga || source.OpCode == OpCodes.Ldarga_S) && argIndex == valueArgIndex;
        bool isReassignValue = (source.OpCode == OpCodes.Starg || source.OpCode == OpCodes.Starg_S) && argIndex == valueArgIndex;

        if (isAddressOfValue || isReassignValue) {
            throw new ConcordEmitException(
                "CONC039",
                $"Value injection cannot take the address of or reassign its 'original' parameter; only by-value reads are supported.");
        }

        if (source.OpCode == OpCodes.Ret) {
            return new List<Instruction> {
                Instruction.Create(OpCodes.Stloc, resultLocal), Instruction.Create(OpCodes.Br, spliceEnd),
            };
        }

        if (TryLowerProjectedMethodCall(source, ctx, out List<Instruction>? projectedCall)) {
            return projectedCall;
        }

        if (TryNormalizeLocal(source, ctx.VariableMap, ctx.InjectionMethodLocals, out Instruction? local)) {
            return new List<Instruction> { local! };
        }

        Instruction copy = CloneInstruction(source, ctx.Module, ctx.VariableMap, ctx.InjectedMembers);
        RemapArgInstruction(copy, ctx.ArgRemap);
        return new List<Instruction> { copy };
    }

    private static bool IsControlHandleDup(Instruction instruction, int controlHandleArgIndex) {
        if (instruction.OpCode != OpCodes.Dup || instruction.Previous is null) {
            return false;
        }

        Instruction previous = instruction.Previous;
        return ControlHandleLowering.IsControlHandleReceiverLoad(previous, controlHandleArgIndex)
            || IsControlHandleDup(previous, controlHandleArgIndex);
    }

    private static bool IsOperationDup(Instruction instruction, int operationArgIndex) {
        if (instruction.OpCode != OpCodes.Dup || instruction.Previous is null) {
            return false;
        }

        Instruction previous = instruction.Previous;
        return ControlHandleLowering.IsOperationReceiverLoad(previous, operationArgIndex)
            || IsOperationDup(previous, operationArgIndex);
    }

    private static void EnsureNotStrayControlHandleUse(Instruction receiverLoad, int controlHandleArgIndex, MethodBase? target, MethodBase injectionMethod) {
        Instruction? next = receiverLoad.Next;
        if (next is null) {
            return;
        }

        bool isStore = IsStoreOpCode(next.OpCode);
        bool isUnrelatedCall = IsCallOpCode(next.OpCode)
            && ControlHandleLowering.ClassifyCall(next) == ControlHandleLowering.ControlCallKind.None
            && !(target is not null && ControlHandleLowering.IsOriginalBodySpliceCall(next, target));
        bool isChainedReceiverLoad = ControlHandleLowering.IsControlHandleReceiverLoad(next, controlHandleArgIndex)
            || next.OpCode == OpCodes.Dup;

        if (isChainedReceiverLoad) {
            EnsureNotStrayControlHandleUse(next, controlHandleArgIndex, target, injectionMethod);
            return;
        }

        if (isStore || isUnrelatedCall) {
            throw new ConcordEmitException(
                "CONC013",
                "The control handle parameter of injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name +
                "' must be used only for direct control calls (Cancel/ReturnValue/original invoke); " +
                "it cannot be stored to a local, captured, or passed elsewhere.");
        }
    }

    private static void EnsureNotStrayOperationUse(Instruction receiverLoad, int operationArgIndex, MethodBase injectionMethod) {
        Instruction? next = receiverLoad.Next;
        if (next is null) {
            return;
        }

        bool isStore = IsStoreOpCode(next.OpCode);
        bool isUnrelatedCall = IsCallOpCode(next.OpCode) && !ControlHandleLowering.IsOperationInvoke(next);
        bool isChainedReceiverLoad = ControlHandleLowering.IsOperationReceiverLoad(next, operationArgIndex)
            || next.OpCode == OpCodes.Dup;

        if (isChainedReceiverLoad) {
            EnsureNotStrayOperationUse(next, operationArgIndex, injectionMethod);
            return;
        }

        if (isStore || isUnrelatedCall) {
            throw new ConcordEmitException(
                "CONC013",
                "The Operation handle parameter of injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name +
                "' must be used only as the direct receiver of Invoke(...); " +
                "it cannot be stored to a local, captured, or passed elsewhere.");
        }
    }

    private static bool IsStoreOpCode(OpCode opCode) {
        return opCode == OpCodes.Stloc
            || opCode == OpCodes.Stloc_S
            || opCode == OpCodes.Stloc_0
            || opCode == OpCodes.Stloc_1
            || opCode == OpCodes.Stloc_2
            || opCode == OpCodes.Stloc_3
            || opCode == OpCodes.Starg
            || opCode == OpCodes.Starg_S;
    }

    private static bool IsCallOpCode(OpCode opCode) {
        return opCode == OpCodes.Call
            || opCode == OpCodes.Callvirt
            || opCode == OpCodes.Newobj;
    }

    private static void EnsureVerbatimArgForwards(Instruction spliceCall, int consumed, MethodBase injectionMethod) {
        Instruction? cursor = spliceCall.Previous;
        for (int p = 0; p < consumed; p++) {
            if (cursor is null || ControlHandleLowering.GetLoadArgIndex(cursor) < 0) {
                throw new ConcordEmitException(
                    "CONC014",
                    "The original-body call in injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name +
                    "' must forward its arguments verbatim (plain parameter loads); computed or modified arguments are not supported.");
            }

            cursor = cursor.Previous;
        }
    }

    private static bool TryLowerProjectedMethodCall(Instruction source, LoweringContext ctx, out List<Instruction> lowered) {
        lowered = [];

        if (source.OpCode != OpCodes.Call && source.OpCode != OpCodes.Callvirt) {
            return false;
        }

        if (source.Operand is not MethodReference method) {
            return false;
        }

        MethodBase resolved = method.ResolveReflection();
        if (!ctx.InjectedMembers.TryGetMethod(resolved, out MethodInfo? target)) {
            return false;
        }

        if (target is null) {
            return true;
        }

        lowered.Add(Instruction.Create(CallOpCodeFor(target), ctx.Module.ImportReference(target)));
        return true;
    }

    private static Dictionary<Instruction, Instruction> BuildInstructionMap(List<(Instruction Source, List<Instruction> Emitted)> entries) {
        Dictionary<Instruction, Instruction> map = new Dictionary<Instruction, Instruction>(entries.Count);

        for (int i = 0; i < entries.Count; i++) {
            Instruction? mapped = null;
            for (int j = i; j < entries.Count; j++) {
                if (entries[j].Emitted.Count > 0) {
                    mapped = entries[j].Emitted[0];
                    break;
                }
            }

            if (mapped is not null) {
                map[entries[i].Source] = mapped;
            }
        }

        return map;
    }

    private static Dictionary<int, int> BuildArgRemap(MethodBase target, MethodBase injectionMethod) {
        ParameterInfo[] targetParams = target.GetParameters();
        ParameterInfo[] injectionParameters = injectionMethod.GetParameters();

        int injectionOffset = injectionMethod.IsStatic ? 0 : 1;
        int targetOffset = target.IsStatic ? 0 : 1;

        Dictionary<int, int> remap = new Dictionary<int, int>();

        if (!injectionMethod.IsStatic) {
            remap[0] = 0;
        }

        for (int ti = 0; ti < injectionParameters.Length; ti++) {
            int injectionArgIndex = ti + injectionOffset;

            for (int si = 0; si < targetParams.Length; si++) {
                if (injectionParameters[ti].Name == targetParams[si].Name &&
                    injectionParameters[ti].ParameterType == targetParams[si].ParameterType) {
                    remap[injectionArgIndex] = si + targetOffset;
                    break;
                }
            }
        }

        return remap;
    }

    private static Dictionary<int, VariableDefinition> BuildWrapArgBinding(
        MethodBase injectionMethod,
        IReadOnlyList<VariableDefinition> argLocals,
        CallSiteShape shape) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int offset = injectionMethod.IsStatic ? 0 : 1;

        Dictionary<int, VariableDefinition> binding = new Dictionary<int, VariableDefinition>();
        int siteArg = 0;
        for (int i = 0; i < parameters.Length; i++) {
            if (ControlHandleLowering.IsOperationType(parameters[i].ParameterType)) {
                break;
            }

            if (siteArg >= argLocals.Count) {
                throw new ConcordEmitException(
                    "CONC039",
                    $"Around-invoke injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' declares more leading parameters than the matched call has arguments.");
            }

            if (parameters[i].ParameterType != shape.ParameterTypes[siteArg]) {
                throw new ConcordEmitException(
                    "CONC039",
                    $"Around-invoke injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' declares leading parameter '{parameters[i].Name}' " +
                    $"of type '{parameters[i].ParameterType.Name}' but the matched call's argument {siteArg} is of type '{shape.ParameterTypes[siteArg].Name}'.");
            }

            binding[i + offset] = argLocals[siteArg];
            siteArg++;
        }

        return binding;
    }

    private static void RemapArgInstruction(Instruction instruction, Dictionary<int, int> argRemap) {
        int injectionArgIndex = GetArgIndex(instruction);
        if (injectionArgIndex < 0) {
            return;
        }

        if (!argRemap.TryGetValue(injectionArgIndex, out int wrapperArgIndex)) {
            return;
        }

        bool indexUnchanged = injectionArgIndex == wrapperArgIndex;
        bool operandNeedsCanonicalizing = instruction.Operand is ParameterDefinition;
        if (indexUnchanged && !operandNeedsCanonicalizing) {
            return;
        }

        OpCode baseOpCode = instruction.OpCode;
        bool isAddress = baseOpCode == OpCodes.Ldarga || baseOpCode == OpCodes.Ldarga_S;
        bool isStore = baseOpCode == OpCodes.Starg || baseOpCode == OpCodes.Starg_S;

        if (isStore) {
            instruction.OpCode = wrapperArgIndex <= byte.MaxValue ? OpCodes.Starg_S : OpCodes.Starg;
            instruction.Operand = wrapperArgIndex;
            return;
        }

        if (isAddress) {
            instruction.OpCode = wrapperArgIndex <= byte.MaxValue ? OpCodes.Ldarga_S : OpCodes.Ldarga;
            instruction.Operand = wrapperArgIndex;
            return;
        }

        instruction.OpCode = wrapperArgIndex switch {
            0 => OpCodes.Ldarg_0,
            1 => OpCodes.Ldarg_1,
            2 => OpCodes.Ldarg_2,
            3 => OpCodes.Ldarg_3,
            _ when wrapperArgIndex <= byte.MaxValue => OpCodes.Ldarg_S,
            _ => OpCodes.Ldarg,
        };

        instruction.Operand = wrapperArgIndex switch {
            0 or 1 or 2 or 3 => null,
            _ when wrapperArgIndex <= byte.MaxValue => (byte)wrapperArgIndex,
            _ => wrapperArgIndex,
        };
    }

    private static int GetArgIndex(Instruction instruction) {
        if (instruction.OpCode == OpCodes.Ldarg_0) {
            return 0;
        }

        if (instruction.OpCode == OpCodes.Ldarg_1) {
            return 1;
        }

        if (instruction.OpCode == OpCodes.Ldarg_2) {
            return 2;
        }

        if (instruction.OpCode == OpCodes.Ldarg_3) {
            return 3;
        }

        if (instruction.OpCode == OpCodes.Ldarg_S || instruction.OpCode == OpCodes.Ldarg) {
            return ArgIndexOperand(instruction.Operand);
        }

        if (instruction.OpCode == OpCodes.Ldarga_S || instruction.OpCode == OpCodes.Ldarga) {
            return ArgIndexOperand(instruction.Operand);
        }

        if (instruction.OpCode == OpCodes.Starg_S || instruction.OpCode == OpCodes.Starg) {
            return ArgIndexOperand(instruction.Operand);
        }

        return -1;
    }

    private static int ArgIndexOperand(object? operand) {
        if (operand is ParameterDefinition parameter) {
            return parameter.Sequence;
        }

        return Convert.ToInt32(operand);
    }

    private static Dictionary<VariableDefinition, VariableDefinition> AppendVariables(
        MethodBody source,
        MethodBody destination,
        ModuleDefinition module) {
        Dictionary<VariableDefinition, VariableDefinition> map =
            new Dictionary<VariableDefinition, VariableDefinition>(source.Variables.Count);
        foreach (VariableDefinition variable in source.Variables) {
            VariableDefinition copy = new VariableDefinition(module.ImportReference(variable.VariableType.ResolveReflection()));
            destination.Variables.Add(copy);
            map[variable] = copy;
        }

        if (source.Variables.Count > 0) {
            destination.InitLocals = true;
        }

        return map;
    }

    private static Dictionary<VariableDefinition, VariableDefinition> CopyInjectionLocals(
        MethodBody injectionBody,
        MethodBody destination,
        ModuleDefinition module) {
        Dictionary<VariableDefinition, VariableDefinition> map =
            new Dictionary<VariableDefinition, VariableDefinition>(injectionBody.Variables.Count);
        foreach (VariableDefinition variable in injectionBody.Variables) {
            VariableDefinition copy = new VariableDefinition(module.ImportReference(variable.VariableType.ResolveReflection()));
            destination.Variables.Add(copy);
            map[variable] = copy;
        }

        if (injectionBody.Variables.Count > 0) {
            destination.InitLocals = true;
        }

        return map;
    }

    private static bool TryNormalizeLocal(
        Instruction source,
        Dictionary<VariableDefinition, VariableDefinition> variableMap,
        IList<VariableDefinition> injectionLocals,
        out Instruction? normalized) {
        OpCode opCode = source.OpCode;

        int loadIndex = MacroLoadIndex(opCode);
        if (loadIndex >= 0) {
            normalized = Instruction.Create(OpCodes.Ldloc, variableMap[injectionLocals[loadIndex]]);
            return true;
        }

        int storeIndex = MacroStoreIndex(opCode);
        if (storeIndex >= 0) {
            normalized = Instruction.Create(OpCodes.Stloc, variableMap[injectionLocals[storeIndex]]);
            return true;
        }

        if (source.Operand is VariableDefinition variable) {
            OpCode remapped;
            if (opCode == OpCodes.Ldloca_S || opCode == OpCodes.Ldloca) {
                remapped = OpCodes.Ldloca;
            } else if (opCode == OpCodes.Ldloc_S || opCode == OpCodes.Ldloc) {
                remapped = OpCodes.Ldloc;
            } else {
                remapped = OpCodes.Stloc;
            }

            normalized = Instruction.Create(remapped, variableMap[variable]);
            return true;
        }

        normalized = null;
        return false;
    }

    private static int MacroLoadIndex(OpCode opCode) {
        if (opCode == OpCodes.Ldloc_0) {
            return 0;
        }

        if (opCode == OpCodes.Ldloc_1) {
            return 1;
        }

        if (opCode == OpCodes.Ldloc_2) {
            return 2;
        }

        if (opCode == OpCodes.Ldloc_3) {
            return 3;
        }

        return -1;
    }

    private static int MacroStoreIndex(OpCode opCode) {
        if (opCode == OpCodes.Stloc_0) {
            return 0;
        }

        if (opCode == OpCodes.Stloc_1) {
            return 1;
        }

        if (opCode == OpCodes.Stloc_2) {
            return 2;
        }

        if (opCode == OpCodes.Stloc_3) {
            return 3;
        }

        return -1;
    }

    private static Instruction CloneInstruction(
        Instruction source,
        ModuleDefinition module,
        Dictionary<VariableDefinition, VariableDefinition> variableMap,
        InjectedMemberMap injectedMembers) {
        return source.Operand switch {
            TypeReference type => Instruction.Create(source.OpCode, module.ImportReference(type.ResolveReflection())),
            FieldReference field => CloneFieldInstruction(source.OpCode, field, module, injectedMembers),
            MethodReference method => ImportMethod(source.OpCode, method, module),
            VariableDefinition variable => Instruction.Create(source.OpCode, variableMap[variable]),
            _ => CloneNonImported(source),
        };
    }

    private static Instruction CloneFieldInstruction(
        OpCode opCode,
        FieldReference field,
        ModuleDefinition module,
        InjectedMemberMap injectedMembers) {
        if (injectedMembers.TryGetField(field.Name, out FieldInfo? realField)) {
            return Instruction.Create(opCode, module.ImportReference(realField));
        }

        return Instruction.Create(opCode, module.ImportReference(field.ResolveReflection()));
    }

    private static Instruction ImportMethod(OpCode opCode, MethodReference method, ModuleDefinition module) {
        MethodBase resolved = method.ResolveReflection();
        return resolved is ConstructorInfo constructor
            ? Instruction.Create(opCode, module.ImportReference(constructor))
            : Instruction.Create(opCode, module.ImportReference((MethodInfo)resolved));
    }

    private static OpCode CallOpCodeFor(MethodInfo method) {
        if (method.IsStatic || method.IsPrivate || !method.IsVirtual || method.IsFinal) {
            return OpCodes.Call;
        }

        return OpCodes.Callvirt;
    }

    private static Instruction CloneNonImported(Instruction source) {
        Instruction copy = Instruction.Create(OpCodes.Nop);
        copy.OpCode = source.OpCode;
        copy.Operand = source.Operand;
        return copy;
    }

    private static void RemapBranchTargets(Instruction instruction, Dictionary<Instruction, Instruction> map) {
        if (instruction.Operand is Instruction target) {
            if (map.TryGetValue(target, out Instruction? mapped)) {
                instruction.Operand = mapped;
            }

            return;
        }

        if (instruction.Operand is Instruction[] targets) {
            Instruction[] remapped = new Instruction[targets.Length];
            for (int i = 0; i < targets.Length; i++) {
                remapped[i] = map.TryGetValue(targets[i], out Instruction? mapped) ? mapped : targets[i];
            }

            instruction.Operand = remapped;
        }
    }

    private static ExceptionHandler CloneHandler(
        ExceptionHandler source,
        Dictionary<Instruction, Instruction> map,
        ModuleDefinition module) {
        ExceptionHandler copy = new ExceptionHandler(source.HandlerType) {
            TryStart = Resolve(source.TryStart, map),
            TryEnd = Resolve(source.TryEnd, map),
            HandlerStart = Resolve(source.HandlerStart, map),
            HandlerEnd = Resolve(source.HandlerEnd, map),
            FilterStart = Resolve(source.FilterStart, map),
        };

        if (source.CatchType is not null) {
            copy.CatchType = module.ImportReference(source.CatchType.ResolveReflection());
        }

        return copy;
    }

    private static Instruction? Resolve(Instruction? source, Dictionary<Instruction, Instruction> map) {
        return source is null ? null : map[source];
    }
}
