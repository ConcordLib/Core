using System.Reflection;
using Concord.AttachedData;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

/// <summary>
///     Copies Cecil method bodies into generated wrappers while remapping Concord control calls.
/// </summary>
internal static class BodyCopier {
    private static readonly MethodInfo AttachedGet = typeof(AttachedStorage).GetMethod(nameof(AttachedStorage.Get))!;
    private static readonly MethodInfo AttachedSet = typeof(AttachedStorage).GetMethod(nameof(AttachedStorage.Set))!;
    private static readonly MethodInfo AttachedRef = typeof(AttachedStorage).GetMethod(nameof(AttachedStorage.GetOrAddRef))!;
    private static readonly MethodInfo GetTypeFromHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
    private static readonly MethodInfo TypeAssemblyGetter = typeof(Type).GetProperty(nameof(Type.Assembly))!.GetGetMethod()!;

    /// <summary>
    ///     Copies the original target body into a destination dynamic method definition.
    /// </summary>
    /// <param name="source">The source target method definition.</param>
    /// <param name="destination">The destination wrapper method definition.</param>
    /// <param name="declaringType">The type declaring the source method, so copied <c>GetExecutingAssembly</c> calls keep observing it.</param>
    public static void CopySpine(MethodDefinition source, MethodDefinition destination, Type declaringType) {
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

        InjectedMemberMap emptyMembers = new InjectedMemberMap(new Dictionary<string, FieldInfo>(), new Dictionary<string, MethodInfo?>(), new Dictionary<string, AttachedFieldSlot>());
        foreach (Instruction source_instruction in sourceBody.Instructions) {
            Instruction copy = CloneInstruction(source_instruction, module, variableMap, emptyMembers);
            instructionMap[source_instruction] = copy;
            il.Append(copy);
            if (IsGetExecutingAssemblyCall(source_instruction)) {
                copy.OpCode = OpCodes.Ldtoken;
                copy.Operand = module.ImportReference(declaringType);
                il.Append(Instruction.Create(OpCodes.Call, module.ImportReference(GetTypeFromHandle)));
                il.Append(Instruction.Create(OpCodes.Callvirt, module.ImportReference(TypeAssemblyGetter)));
            }
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
    /// <param name="request">The injection identity and target being copied.</param>
    /// <param name="locals">Wrapper locals used for cancellation and return-value protocol state.</param>
    /// <param name="returnBranchTarget">The instruction to branch to when the injection method returns.</param>
    /// <param name="spineTemplate">The whole-method Around spine template to splice a fresh copy from at each Invoke site, if any.</param>
    /// <param name="spineCopies">Collects one <see cref="SpineCopy" /> per Invoke site spliced while copying, if any.</param>
    /// <param name="insideAround">
    ///     <see langword="true" /> when this injection body is being spliced inside a whole-method Around's
    ///     <see cref="SpineCopy" />, so its <c>ControlHandle</c> return-value protocol reads/writes
    ///     <see cref="ProtocolLocals.SpliceValue" /> instead of <see cref="ProtocolLocals.ReturnValue" />.
    /// </param>
    /// <param name="captureBinding">Maps an injection argument index to the call-site spill local it reads, if any.</param>
    /// <returns>The copied and lowered instruction sequence.</returns>
    public static List<Instruction> CopyInjection(
        InjectionCopyRequest request,
        ProtocolLocals locals,
        Instruction returnBranchTarget,
        SpineTemplate? spineTemplate = null,
        List<SpineCopy>? spineCopies = null,
        bool insideAround = false,
        IReadOnlyDictionary<int, VariableDefinition>? captureBinding = null) {
        MethodBody injectionBody = request.InjectionDefinition.Body;
        RejectConstructedLocalHandle(injectionBody, request.InjectionMethod);
        ModuleDefinition module = request.Destination.Module;

        Dictionary<int, int> argRemap = BuildArgRemap(request.Target, request.InjectionMethod);
        captureBinding = LocalResolver.Bind(request, locals, captureBinding);
        if (captureBinding is not null) {
            foreach (int captured in captureBinding.Keys) {
                argRemap.Remove(captured);
            }
        }

        LocalHandleLowering? localHandles = LocalHandleLowering.Plan(
            injectionBody, request.InjectionMethod, request.Destination.Body, locals, request.Target);
        if (localHandles is not null) {
            foreach (int handle in LocalHandleLowering.BoundArgIndices(request.InjectionMethod)) {
                argRemap.Remove(handle);
            }
        }

        Dictionary<int, object?> boundValues = BoundConstants.Resolve(request.InjectionMethod, request.BoundArguments);
        foreach (int bound in boundValues.Keys) {
            argRemap.Remove(bound);
        }

        int controlHandleArgIndex = ControlHandleLowering.FindControlHandleArgIndex(request.InjectionMethod);
        int operationArgIndex = ControlHandleLowering.FindOperationArgIndex(request.InjectionMethod);

        Dictionary<VariableDefinition, VariableDefinition> variableMap = CopyInjectionLocals(injectionBody, request.Destination.Body, module);
        LoweringContext ctx = new LoweringContext(
            module, variableMap, injectionBody.Variables, request.InjectedMembers, argRemap, request.Destination.Body.Variables,
            request.InjectionMethod.DeclaringType!, request.InjectionMethod, LocalOnly(captureBinding, request.InjectionMethod));

        List<Instruction> boundPrologue = [];
        Dictionary<int, VariableDefinition> boundConstants =
            BoundConstants.SpillToLocals(boundValues, request.InjectionMethod, request.Destination.Body, module, boundPrologue);

        InjectionLoweringSite site = new InjectionLoweringSite(
            controlHandleArgIndex,
            operationArgIndex,
            locals,
            returnBranchTarget,
            request.InjectionMethod,
            request.Target,
            spineTemplate,
            request.Destination,
            spineCopies,
            insideAround,
            captureBinding,
            boundConstants,
            localHandles);

        List<(Instruction Source, List<Instruction> Emitted)> entries =
            new List<(Instruction Source, List<Instruction> Emitted)>(injectionBody.Instructions.Count);

        foreach (Instruction source_instruction in injectionBody.Instructions) {
            List<Instruction> emitted = LowerInstruction(source_instruction, ctx, site);
            entries.Add((source_instruction, emitted));
        }

        Dictionary<Instruction, Instruction> instructionMap = BuildInstructionMap(entries);

        List<Instruction> result = new List<Instruction>(injectionBody.Instructions.Count + boundPrologue.Count);
        result.AddRange(boundPrologue);
        foreach ((Instruction _, List<Instruction> emitted) in entries) {
            foreach (Instruction copy in emitted) {
                RemapBranchTargets(copy, instructionMap);
                result.Add(copy);
            }
        }

        CopyInjectionHandlers(injectionBody, request.Destination.Body, instructionMap, module);

        return result;
    }

    /// <summary>
    ///     Copies a <c>T M(T original)</c> value-injection body: the leading parameter's loads read from
    ///     <paramref name="valueLocal" />, and every <c>ret</c> lowers to storing the returned value into a fresh
    ///     result local and branching to a trailing splice-end block that reloads it, leaving exactly the
    ///     replacement value on the evaluation stack.
    /// </summary>
    /// <param name="injectionDefinition">The Cecil definition of the decompiled injection method body.</param>
    /// <param name="destination">The wrapper method the lowered instructions are copied into.</param>
    /// <param name="target">The original target member being patched.</param>
    /// <param name="injectionMethod">The reflection handle for the injection method.</param>
    /// <param name="injectedMembers">Maps injected member declarations to the resolved target members.</param>
    /// <param name="valueLocal">The local holding the value the injection reads and replaces.</param>
    /// <param name="localBinding">
    ///     Maps an injection argument index to the wrapper local its <see cref="LocalAttribute" /> selected.
    ///     Only <see cref="InjectAt.Local" /> supplies one; every other value position leaves it null, and a
    ///     <see cref="LocalAttribute" /> parameter is then rejected rather than silently bound to nothing.
    /// </param>
    /// <param name="localHandles">
    ///     Pairs <see cref="LocalHandle{T}" /> receiver loads with the Value calls that consume them, or null
    ///     when the injection declares no handle.
    /// </param>
    public static List<Instruction> CopyValueInjection(
        MethodDefinition injectionDefinition,
        MethodDefinition destination,
        MethodBase target,
        MethodBase injectionMethod,
        InjectedMemberMap injectedMembers,
        VariableDefinition valueLocal,
        IReadOnlyDictionary<int, VariableDefinition>? localBinding = null,
        LocalHandleLowering? localHandles = null) {
        BoundConstants.RejectDeclarations(injectionMethod, "a value injection");
        if (localBinding is null) {
            RejectLocalParameters(injectionMethod, "a value injection");
        }

        MethodBody injectionBody = injectionDefinition.Body;
        RejectConstructedLocalHandle(injectionBody, injectionMethod);
        ModuleDefinition module = destination.Module;

        Dictionary<int, int> argRemap = BuildArgRemap(target, injectionMethod);
        int valueArgIndex = ValueParameterIndex(injectionMethod);
        argRemap.Remove(valueArgIndex);
        if (localBinding is not null) {
            foreach (int bound in localBinding.Keys) {
                argRemap.Remove(bound);
            }
        }

        if (localHandles is not null) {
            foreach (int handle in LocalHandleLowering.BoundArgIndices(injectionMethod)) {
                argRemap.Remove(handle);
            }
        }

        VariableDefinition resultLocal = new VariableDefinition(valueLocal.VariableType);
        destination.Body.Variables.Add(resultLocal);
        destination.Body.InitLocals = true;

        Instruction spliceEnd = Instruction.Create(OpCodes.Nop);

        Dictionary<VariableDefinition, VariableDefinition> variableMap = CopyInjectionLocals(injectionBody, destination.Body, module);
        LoweringContext ctx = new LoweringContext(
            module, variableMap, injectionBody.Variables, injectedMembers, argRemap, destination.Body.Variables,
            injectionMethod.DeclaringType!, injectionMethod, localBinding);

        List<(Instruction Source, List<Instruction> Emitted)> entries =
            new List<(Instruction Source, List<Instruction> Emitted)>(injectionBody.Instructions.Count);

        ValueLoweringSite valueSite = new ValueLoweringSite(valueArgIndex, valueLocal, resultLocal, spliceEnd, localBinding, localHandles);

        foreach (Instruction source_instruction in injectionBody.Instructions) {
            List<Instruction> emitted = LowerValueInstruction(source_instruction, ctx, valueSite);
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
    /// <param name="request">The injection identity and target being copied.</param>
    /// <param name="wrapEnd">The instruction to branch to when the wrap injection method returns.</param>
    /// <param name="originalCall">The original call-site method reference.</param>
    /// <param name="receiverLocal">The spilled receiver local, or <see langword="null" /> for a static call site.</param>
    /// <param name="argLocals">The spilled argument locals, in call-site parameter order.</param>
    /// <param name="originalOpCode">The original call site's opcode (<c>call</c> or <c>callvirt</c>).</param>
    /// <param name="shape">The matched call site's resolved shape.</param>
    /// <returns>The copied and lowered instruction sequence.</returns>
    public static List<Instruction> CopyWrapInjection(
        InjectionCopyRequest request,
        Instruction wrapEnd,
        MethodReference originalCall,
        VariableDefinition? receiverLocal,
        IReadOnlyList<VariableDefinition> argLocals,
        OpCode originalOpCode,
        CallSiteShape shape) {
        BoundConstants.RejectDeclarations(request.InjectionMethod, "an invoke-wrap injection");
        RejectLocalParameters(request.InjectionMethod, "an invoke-wrap injection");

        MethodBody injectionBody = request.InjectionDefinition.Body;
        RejectConstructedLocalHandle(injectionBody, request.InjectionMethod);
        ModuleDefinition module = request.Destination.Module;

        Dictionary<int, VariableDefinition> wrapArgBinding = BuildWrapArgBinding(request.InjectionMethod, argLocals, shape);
        Dictionary<int, int> thisRemap = request.InjectionMethod.IsStatic ? new Dictionary<int, int>() : new Dictionary<int, int> { [0] = 0 };
        int operationArgIndex = ControlHandleLowering.FindOperationArgIndex(request.InjectionMethod);

        MethodBase resolvedOriginal = originalCall.ResolveReflection();
        MethodReference importedOriginal = resolvedOriginal is ConstructorInfo originalConstructor
            ? module.ImportReference(originalConstructor)
            : module.ImportReference((MethodInfo)resolvedOriginal);

        ParameterInfo[] originalParameters = resolvedOriginal.GetParameters();
        Type[] invokeParameterTypes = new Type[originalParameters.Length];
        for (int i = 0; i < originalParameters.Length; i++) {
            invokeParameterTypes[i] = originalParameters[i].ParameterType;
        }

        Dictionary<VariableDefinition, VariableDefinition> variableMap = CopyInjectionLocals(injectionBody, request.Destination.Body, module);
        LoweringContext ctx = new LoweringContext(
            module, variableMap, injectionBody.Variables, request.InjectedMembers, thisRemap, request.Destination.Body.Variables,
            request.InjectionMethod.DeclaringType!, request.InjectionMethod);

        WrapLoweringSite site = new WrapLoweringSite(
            operationArgIndex,
            wrapEnd,
            importedOriginal,
            receiverLocal,
            invokeParameterTypes,
            originalOpCode,
            wrapArgBinding);

        List<(Instruction Source, List<Instruction> Emitted)> entries =
            new List<(Instruction Source, List<Instruction> Emitted)>(injectionBody.Instructions.Count);

        foreach (Instruction source_instruction in injectionBody.Instructions) {
            List<Instruction> emitted = LowerWrapInstruction(source_instruction, ctx, site);
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

        CopyInjectionHandlers(injectionBody, request.Destination.Body, instructionMap, module);

        return result;
    }

    /// <summary>
    ///     Rewrites <c>ldarg</c>/<c>ldarga</c>/<c>starg</c> instructions that target a rebound parameter to
    ///     load/store the given per-copy argument local instead, so a body spliced into a <see cref="SpineCopy" />
    ///     observes that copy's (possibly Invoke-changed) argument values.
    /// </summary>
    /// <param name="spliceBody">The instruction list to rewrite in place.</param>
    /// <param name="argLocals">Maps a wrapper-relative argument index to its per-copy local.</param>
    internal static void RewriteSpliceArgs(List<Instruction> spliceBody, Dictionary<int, VariableDefinition> argLocals) {
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

            instruction.OpCode = (isAddress, isStore) switch
            {
                (true, _) => OpCodes.Ldloca,
                (false, true) => OpCodes.Stloc,
                _ => OpCodes.Ldloc,
            };
            instruction.Operand = local;
        }
    }

    /// <summary>
    ///     Rewrites local operands that name a spine template slot to name the running
    ///     <see cref="SpineCopy" />'s clone instead, so a body spliced into that copy reads the slot the
    ///     copy actually wrote. The opcode never changes.
    /// </summary>
    /// <param name="spliceBody">The instruction list to rewrite in place.</param>
    /// <param name="localMap">Maps a template local to this copy's clone of it.</param>
    internal static void RewriteSpliceLocals(
        List<Instruction> spliceBody,
        IReadOnlyDictionary<VariableDefinition, VariableDefinition> localMap) {
        foreach (Instruction instruction in spliceBody) {
            if (instruction.Operand is VariableDefinition slot && localMap.TryGetValue(slot, out VariableDefinition? clone)) {
                instruction.Operand = clone;
            }
        }
    }

    // C# evaluates an assigned value AFTER loading the receiver, so `ch.ReturnValue = Build()`
    // puts a whole expression between the handle load and the setter that consumes it. Looking only
    // at the next instruction rejects every such assignment, so walk the stack forward to find the
    // instruction that actually pops the slot this load pushed. startDepth lets a caller resume the
    // walk partway through, once an earlier call has already accounted for the slots above it.
    internal static Instruction? FindStackConsumer(Instruction load, int startDepth = 1) {
        return FindStackConsumer(load, startDepth, out _);
    }

    /// <summary>
    ///     <see cref="FindStackConsumer(Instruction, int)" />, also reporting whether control flow is what
    ///     ended the walk, so a caller can tell "the consumer is past a branch" from "there is no consumer".
    /// </summary>
    /// <param name="load">The instruction whose pushed value is being traced.</param>
    /// <param name="startDepth">How many stack slots above the traced value are already in flight.</param>
    /// <param name="blockedByFlow">Set when a branch, return or throw stopped the walk.</param>
    /// <returns>The instruction that pops the value, or null.</returns>
    internal static Instruction? FindStackConsumer(Instruction load, int startDepth, out bool blockedByFlow) {
        blockedByFlow = false;
        int depth = startDepth;
        for (Instruction? current = load.Next; current is not null; current = current.Next) {
            // Control flow means the slot may be consumed on a path this linear walk cannot follow.
            // Give up rather than accuse code the walk does not actually understand.
            if (current.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch
                    or FlowControl.Return or FlowControl.Throw
                || current.OpCode == OpCodes.Dup) {
                blockedByFlow = current.OpCode != OpCodes.Dup;
                return null;
            }

            int pops = IlDump.PopCount(current);
            if (pops >= depth) {
                return current;
            }

            depth = depth - pops + IlDump.PushCount(current);
        }

        return null;
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

    private static List<Instruction> LowerWrapInstruction(Instruction source, LoweringContext ctx, WrapLoweringSite site) {
        if (ControlHandleLowering.IsOperationReceiverLoad(source, site.OperationArgIndex)) {
            return new List<Instruction>(0);
        }

        if (ControlHandleLowering.IsOperationInvoke(source)) {
            return LowerOperationInvoke(ctx, site.ImportedOriginal, site.ReceiverLocal, site.InvokeParameterTypes, site.OriginalOpCode);
        }

        if (source.OpCode == OpCodes.Ret) {
            return new List<Instruction> { Instruction.Create(OpCodes.Br, site.WrapEnd) };
        }

        if (TryLowerArgBinding(source, site.WrapArgBinding, out Instruction? bound)) {
            return new List<Instruction> { bound! };
        }

        if (TryLowerGetExecutingAssembly(source, ctx, out List<Instruction>? executingAssembly)) {
            return executingAssembly;
        }

        if (TryLowerProjectedMethodCall(source, ctx, out List<Instruction>? projectedCall)) {
            return projectedCall;
        }

        if (TryNormalizeLocal(source, ctx.VariableMap, ctx.InjectionMethodLocals, out Instruction? local)) {
            return new List<Instruction> { local! };
        }

        if (TryLowerObjectTypedInjectedField(source, ctx, out List<Instruction>? boxedField)) {
            return boxedField!;
        }

        if (TryLowerAttachedField(source, ctx, out List<Instruction>? attachedField)) {
            return attachedField!;
        }

        Instruction copy = CloneInstruction(source, ctx.Module, ctx.VariableMap, ctx.InjectedMembers);
        RemapArgInstruction(copy, ctx);
        return new List<Instruction> { copy };
    }

    private static bool TryLowerArgBinding(Instruction source, IReadOnlyDictionary<int, VariableDefinition> argBinding, out Instruction? lowered) {
        lowered = null;

        bool isAddress = source.OpCode == OpCodes.Ldarga || source.OpCode == OpCodes.Ldarga_S;
        bool isLoad = IsLoadArgOpCode(source.OpCode);
        if (!isAddress && !isLoad) {
            return false;
        }

        int argIndex = GetArgIndex(source);
        if (argIndex < 0 || !argBinding.TryGetValue(argIndex, out VariableDefinition? local)) {
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

    private static List<Instruction> LowerInstruction(Instruction source, LoweringContext ctx, InjectionLoweringSite site) {
        if (site.LocalHandles is not null && site.LocalHandles.TryLower(source, out List<Instruction> handleAccess)) {
            return handleAccess;
        }

        List<Instruction>? strayHandleUse = TryLowerStrayHandleUse(source, site);
        if (strayHandleUse is not null) {
            return strayHandleUse;
        }

        List<Instruction>? spliceLowering = TryLowerOriginalBodySplice(source, ctx, site);
        if (spliceLowering is not null) {
            return spliceLowering;
        }

        List<Instruction>? controlCallLowering = TryLowerControlCall(source, site);
        if (controlCallLowering is not null) {
            return controlCallLowering;
        }

        if (source.OpCode == OpCodes.Ret) {
            return LowerReturn(site);
        }

        List<Instruction>? bindingLowering = TryLowerBinding(source, ctx, site);
        if (bindingLowering is not null) {
            return bindingLowering;
        }

        Instruction copy = CloneInstruction(source, ctx.Module, ctx.VariableMap, ctx.InjectedMembers);
        RemapArgInstruction(copy, ctx);
        return new List<Instruction> { copy };
    }

    private static List<Instruction>? TryLowerBinding(Instruction source, LoweringContext ctx, InjectionLoweringSite site) {
        List<Instruction>? localAddress = TryLowerLocalAddress(source, ctx);
        if (localAddress is not null) {
            return localAddress;
        }

        if (site.CaptureBinding is not null && TryLowerArgBinding(source, site.CaptureBinding, out Instruction? captured)) {
            return new List<Instruction> { captured! };
        }

        if (site.BoundConstants is not null && TryLowerArgBinding(source, site.BoundConstants, out Instruction? boundLocal)) {
            return new List<Instruction> { boundLocal! };
        }

        return TryLowerCommon(source, ctx);
    }

    private static List<Instruction>? TryLowerCommon(Instruction source, LoweringContext ctx) {
        if (TryLowerGetExecutingAssembly(source, ctx, out List<Instruction>? executingAssembly)) {
            return executingAssembly;
        }

        if (TryLowerProjectedMethodCall(source, ctx, out List<Instruction>? projectedCall)) {
            return projectedCall;
        }

        if (TryNormalizeLocal(source, ctx.VariableMap, ctx.InjectionMethodLocals, out Instruction? local)) {
            return new List<Instruction> { local! };
        }

        if (TryLowerObjectTypedInjectedField(source, ctx, out List<Instruction>? boxedField)) {
            return boxedField!;
        }

        if (TryLowerAttachedField(source, ctx, out List<Instruction>? attachedField)) {
            return attachedField!;
        }

        return null;
    }

    private static List<Instruction>? TryLowerStrayHandleUse(Instruction source, InjectionLoweringSite site) {
        if (ControlHandleLowering.IsControlHandleReceiverLoad(source, site.ControlHandleArgIndex)) {
            EnsureNotStrayControlHandleUse(source, site.ControlHandleArgIndex, site.Target, site.InjectionMethod);
            return new List<Instruction>(0);
        }

        if (IsControlHandleDup(source, site.ControlHandleArgIndex)) {
            EnsureNotStrayControlHandleUse(source, site.ControlHandleArgIndex, site.Target, site.InjectionMethod);
            return new List<Instruction>(0);
        }

        if (site.SpineTemplate is not null && ControlHandleLowering.IsOperationReceiverLoad(source, site.OperationArgIndex)) {
            EnsureNotStrayOperationUse(source, site.OperationArgIndex, site.InjectionMethod);
            return new List<Instruction>(0);
        }

        if (site.SpineTemplate is not null && IsOperationDup(source, site.OperationArgIndex)) {
            EnsureNotStrayOperationUse(source, site.OperationArgIndex, site.InjectionMethod);
            return new List<Instruction>(0);
        }

        return null;
    }

    private static List<Instruction>? TryLowerOriginalBodySplice(Instruction source, LoweringContext ctx, InjectionLoweringSite site) {
        if (site.Target is not null && site.SpineTemplate is not null && ControlHandleLowering.IsOperationInvoke(source)) {
            SpineCopy spineCopy = SpineCopy.Create(site.SpineTemplate, site.Destination!);
            site.SpineCopies!.Add(spineCopy);

            List<Instruction> spliced = new List<Instruction>(site.Target.GetParameters().Length + spineCopy.Instructions.Count);
            List<VariableDefinition> argLocals = SpillInvokeArgs(ctx, site.Target, spliced);

            int targetOffset = site.Target.IsStatic ? 0 : 1;
            for (int i = 0; i < argLocals.Count; i++) {
                spineCopy.ArgLocals[i + targetOffset] = argLocals[i];
            }

            RewriteSpliceArgs(spineCopy.Instructions, spineCopy.ArgLocals);

            spliced.AddRange(spineCopy.Instructions);
            return spliced;
        }

        if (site.Target is not null && site.SpineTemplate is not null && ControlHandleLowering.IsOriginalBodySpliceCall(source, site.Target)) {
            int consumed = site.Target.GetParameters().Length + (site.Target.IsStatic ? 0 : 1);
            EnsureVerbatimArgForwards(source, consumed, site.InjectionMethod);

            SpineCopy spineCopy = SpineCopy.Create(site.SpineTemplate, site.Destination!);
            site.SpineCopies!.Add(spineCopy);

            List<Instruction> spliced = new List<Instruction>(consumed + spineCopy.Instructions.Count);
            for (int p = 0; p < consumed; p++) {
                spliced.Add(Instruction.Create(OpCodes.Pop));
            }

            spliced.AddRange(spineCopy.Instructions);
            return spliced;
        }

        return null;
    }

    private static List<Instruction>? TryLowerControlCall(Instruction source, InjectionLoweringSite site) {
        ControlHandleLowering.ControlCallKind controlCall = ControlHandleLowering.ClassifyCall(source);
        if (controlCall == ControlHandleLowering.ControlCallKind.Cancel) {
            return new List<Instruction> { Instruction.Create(OpCodes.Ldc_I4_1), Instruction.Create(OpCodes.Stloc, site.Locals.Cancel) };
        }

        if (controlCall == ControlHandleLowering.ControlCallKind.GetReturnValue) {
            VariableDefinition returnValueSource = site.InsideAround && site.Locals.SpliceValue is not null ? site.Locals.SpliceValue : site.Locals.ReturnValue!;
            return new List<Instruction> { Instruction.Create(OpCodes.Ldloc, returnValueSource) };
        }

        if (controlCall == ControlHandleLowering.ControlCallKind.SetReturnValue) {
            if (site.InsideAround && site.Locals.SpliceValue is not null) {
                return new List<Instruction> { Instruction.Create(OpCodes.Stloc, site.Locals.SpliceValue) };
            }

            return new List<Instruction> {
                Instruction.Create(OpCodes.Stloc, site.Locals.ReturnValue!),
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Stloc, site.Locals.HasReturn!),
            };
        }

        if (controlCall == ControlHandleLowering.ControlCallKind.SetState) {
            return new List<Instruction> { Instruction.Create(OpCodes.Stloc, ResolveStateLocal(site)) };
        }

        if (controlCall == ControlHandleLowering.ControlCallKind.GetState) {
            return new List<Instruction> { Instruction.Create(OpCodes.Ldloc, ResolveStateLocal(site)) };
        }

        return null;
    }

    // The slot is keyed on the patch declaration, so every injection copied from the same declaring
    // type reads and writes the one local allocated for it during composition.
    private static VariableDefinition ResolveStateLocal(InjectionLoweringSite site) {
        Type? owner = site.InjectionMethod.DeclaringType;
        if (owner is not null && site.Locals.State is not null && site.Locals.State.TryGetValue(owner, out VariableDefinition? local)) {
            return local;
        }

        throw new ConcordEmitException(
            "CONC142",
            "Concord bug: no state slot exists for injection '" + site.InjectionMethod.DeclaringType?.Name + "." + site.InjectionMethod.Name +
            "' even though it calls SetState/GetState. Nothing in the patch declaration causes this. Report it with the declaration.");
    }

    private static List<Instruction> LowerReturn(InjectionLoweringSite site) {
        if (site.SpineTemplate is not null && site.Locals.ReturnValue is not null) {
            return new List<Instruction> {
                Instruction.Create(OpCodes.Stloc, site.Locals.ReturnValue), Instruction.Create(OpCodes.Br, site.ReturnBranchTarget),
            };
        }

        if (ControlHandleLowering.ReturnsControl(site.InjectionMethod)) {
            return new List<Instruction> {
                Instruction.Create(OpCodes.Ldloc, site.Locals.Cancel),
                Instruction.Create(OpCodes.Or),
                Instruction.Create(OpCodes.Stloc, site.Locals.Cancel),
                Instruction.Create(OpCodes.Br, site.ReturnBranchTarget),
            };
        }

        return new List<Instruction> { Instruction.Create(OpCodes.Br, site.ReturnBranchTarget) };
    }

    private static List<Instruction> LowerValueInstruction(Instruction source, LoweringContext ctx, ValueLoweringSite site) {
        if (site.LocalHandles is not null && site.LocalHandles.TryLower(source, out List<Instruction> handleAccess)) {
            return handleAccess;
        }

        if (IsLoadArgOpCode(source.OpCode) && GetArgIndex(source) == site.ValueArgIndex) {
            return new List<Instruction> { Instruction.Create(OpCodes.Ldloc, site.ValueLocal) };
        }

        RejectValueParameterWrite(source, site.ValueArgIndex);

        List<Instruction>? localAddress = TryLowerLocalAddress(source, ctx);
        if (localAddress is not null) {
            return localAddress;
        }

        if (site.LocalBinding is not null && TryLowerArgBinding(source, site.LocalBinding, out Instruction? boundLocal)) {
            return new List<Instruction> { boundLocal! };
        }

        if (source.OpCode == OpCodes.Ret) {
            return new List<Instruction> {
                Instruction.Create(OpCodes.Stloc, site.ResultLocal), Instruction.Create(OpCodes.Br, site.SpliceEnd),
            };
        }

        List<Instruction>? common = TryLowerCommon(source, ctx);
        if (common is not null) {
            return common;
        }

        Instruction copy = CloneInstruction(source, ctx.Module, ctx.VariableMap, ctx.InjectedMembers);
        RemapArgInstruction(copy, ctx);
        return new List<Instruction> { copy };
    }

    private static void RejectValueParameterWrite(Instruction source, int valueArgIndex) {
        int argIndex = GetArgIndex(source);
        bool isAddressOfValue = (source.OpCode == OpCodes.Ldarga || source.OpCode == OpCodes.Ldarga_S) && argIndex == valueArgIndex;
        bool isReassignValue = (source.OpCode == OpCodes.Starg || source.OpCode == OpCodes.Starg_S) && argIndex == valueArgIndex;

        if (isAddressOfValue || isReassignValue) {
            throw new ConcordEmitException(
                "CONC039",
                $"Value injection cannot take the address of or reassign its 'original' parameter. Only by-value reads are supported.");
        }
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

        // Another load or a dup puts a second copy of the handle in flight; that copy is checked
        // from its own load, so follow it rather than treating it as this one's consumer.
        if (ControlHandleLowering.IsControlHandleReceiverLoad(next, controlHandleArgIndex) || next.OpCode == OpCodes.Dup) {
            EnsureNotStrayControlHandleUse(next, controlHandleArgIndex, target, injectionMethod);
            return;
        }

        Instruction? consumer = FindStackConsumer(receiverLoad);
        if (consumer is null) {
            return;
        }

        bool isStore = IsStoreOpCode(consumer.OpCode);
        bool isUnrelatedCall = IsCallOpCode(consumer.OpCode)
            && ControlHandleLowering.ClassifyCall(consumer) == ControlHandleLowering.ControlCallKind.None
            && !(target is not null && ControlHandleLowering.IsOriginalBodySpliceCall(consumer, target));

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
                    "' must forward its arguments verbatim (plain parameter loads). Computed or modified arguments are not supported.");
            }

            cursor = cursor.Previous;
        }
    }

    private static bool IsGetExecutingAssemblyCall(Instruction source) {
        return source.OpCode == OpCodes.Call
            && source.Operand is MethodReference method
            && method.Name == "GetExecutingAssembly"
            && method.DeclaringType?.FullName == "System.Reflection.Assembly";
    }

    private static bool TryLowerGetExecutingAssembly(Instruction source, LoweringContext ctx, out List<Instruction> lowered) {
        lowered = [];

        if (!IsGetExecutingAssemblyCall(source)) {
            return false;
        }

        lowered.Add(Instruction.Create(OpCodes.Ldtoken, ctx.Module.ImportReference(ctx.InjectionDeclaringType)));
        lowered.Add(Instruction.Create(OpCodes.Call, ctx.Module.ImportReference(GetTypeFromHandle)));
        lowered.Add(Instruction.Create(OpCodes.Callvirt, ctx.Module.ImportReference(TypeAssemblyGetter)));
        return true;
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

    // The value parameter is whichever one is not a local sibling, so a signature that declares them
    // the other way round still reads and replaces the right thing. A bare LocalHandle<T> carries no
    // [Local], so count it the same way WrapperComposer.ValidateValueInjectionShape does.
    private static int ValueParameterIndex(MethodBase injectionMethod) {
        int offset = injectionMethod.IsStatic ? 0 : 1;
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].GetCustomAttribute<LocalAttribute>() is null
                && !LocalHandleLowering.IsLocalHandleType(parameters[i].ParameterType)) {
                return i + offset;
            }
        }

        return offset;
    }

    // A handle only ever exists as a lowered-away parameter, so a `new LocalHandle<T>()` anywhere in
    // an injection body copies through verbatim and puts a real allocation on the patched path.
    private static void RejectConstructedLocalHandle(MethodBody injectionBody, MethodBase injectionMethod) {
        foreach (Instruction instruction in injectionBody.Instructions) {
            if (instruction.OpCode != OpCodes.Newobj || instruction.Operand is not MethodReference reference) {
                continue;
            }

            TypeReference? declaringType = reference.DeclaringType;
            if (declaringType?.Namespace == "Concord" && declaringType.Name == "LocalHandle`1") {
                throw new ConcordEmitException(
                    "CONC161",
                    $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' constructs a " +
                    "LocalHandle<T>. Concord lowers every handle away while copying IL, so no instance exists at " +
                    "runtime; a handle can only arrive as an injection parameter.");
            }
        }
    }

    // Backstop behind WrapperComposer.RejectMisplacedLocals. That gate is a hand-maintained list of
    // positions; re-routing a position through a copier that builds no binding map would otherwise
    // read a default forever instead of failing.
    private static void RejectLocalParameters(MethodBase injectionMethod, string what) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].GetCustomAttribute<LocalAttribute>() is not null
                || LocalHandleLowering.IsLocalHandleType(parameters[i].ParameterType)) {
                throw new ConcordEmitException(
                    "CONC158",
                    $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' declares a [Local] or " +
                    $"LocalHandle<T> parameter on {what}, which binds no local.");
            }
        }
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

    // A parameter Concord binds - [Local], [Capture], [Bound], a control handle - has no argument slot
    // in the wrapper, so it is absent from ArgRemap and its loads lower to the local it was bound to. A
    // store has nothing to lower to, so it would copy through as a starg against the wrapper's own
    // argument of that index, which is the target's parameter N: the wrong variable, silently, or
    // invalid IL the runtime only rejects at JIT when the two types differ.
    private static void RejectBoundParameterStore(Instruction instruction, int injectionArgIndex, LoweringContext ctx) {
        if (instruction.OpCode != OpCodes.Starg && instruction.OpCode != OpCodes.Starg_S) {
            return;
        }

        throw new ConcordEmitException(
            "CONC164",
            $"Injection '{ctx.InjectionMethod.DeclaringType?.Name}.{ctx.InjectionMethod.Name}' assigns to " +
            $"{ParameterName(ctx.InjectionMethod, injectionArgIndex)}, which Concord binds for it. A bound parameter " +
            "is read-only: it has no argument slot to store into. Change what the parameter stands for instead - " +
            "a local through a LocalHandle<T>, a captured argument through the Around shift's original.Invoke.");
    }

    private static string ParameterName(MethodBase injectionMethod, int argIndex) {
        int offset = injectionMethod.IsStatic ? 0 : 1;
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int index = argIndex - offset;
        return index >= 0 && index < parameters.Length ? $"parameter '{parameters[index].Name}'" : $"argument {argIndex}";
    }

    // ldarga on a bound local would hand out the address of the target's own slot, so `out x` or
    // `ref x` on a read-only [Local] parameter would write the target's variable. A by-value parameter
    // owns its own storage, so copy the slot and give out the copy's address instead: a struct local's
    // instance calls still work, and a write lands where C# would have put it.
    private static List<Instruction>? TryLowerLocalAddress(Instruction source, LoweringContext ctx) {
        if (source.OpCode != OpCodes.Ldarga && source.OpCode != OpCodes.Ldarga_S) {
            return null;
        }

        int argIndex = GetArgIndex(source);
        if (argIndex < 0 || ctx.LocalArgBinding is null || !ctx.LocalArgBinding.TryGetValue(argIndex, out VariableDefinition? slot)) {
            return null;
        }

        VariableDefinition copy = new VariableDefinition(slot.VariableType);
        ctx.DestinationVariables.Add(copy);

        return new List<Instruction> {
            Instruction.Create(OpCodes.Ldloc, slot),
            Instruction.Create(OpCodes.Stloc, copy),
            Instruction.Create(OpCodes.Ldloca, copy),
        };
    }

    // Filters the merged capture-and-local binding down to the entries that name a real target slot.
    private static Dictionary<int, VariableDefinition>? LocalOnly(
        IReadOnlyDictionary<int, VariableDefinition>? merged, MethodBase injectionMethod) {
        if (merged is null) {
            return null;
        }

        HashSet<int> bound = LocalResolver.BoundArgIndices(injectionMethod);
        if (bound.Count == 0) {
            return null;
        }

        Dictionary<int, VariableDefinition> locals = new Dictionary<int, VariableDefinition>(bound.Count);
        foreach (KeyValuePair<int, VariableDefinition> entry in merged) {
            if (bound.Contains(entry.Key)) {
                locals[entry.Key] = entry.Value;
            }
        }

        return locals;
    }

    private static void RemapArgInstruction(Instruction instruction, LoweringContext ctx) {
        int injectionArgIndex = GetArgIndex(instruction);
        if (injectionArgIndex < 0) {
            return;
        }

        if (!ctx.ArgRemap.TryGetValue(injectionArgIndex, out int wrapperArgIndex)) {
            RejectBoundParameterStore(instruction, injectionArgIndex, ctx);
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
            // ResolveReflection drops the pin, and an unpinned copy lets the GC move the buffer a
            // fixed statement is holding. Put the marker back.
            TypeReference copiedType = module.ImportReference(variable.VariableType.ResolveReflection());
            VariableDefinition copy = new VariableDefinition(variable.IsPinned ? new PinnedType(copiedType) : copiedType);
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

    private static bool TryLowerAttachedField(Instruction source, LoweringContext ctx, out List<Instruction>? lowered) {
        lowered = null;
        if (source.Operand is not FieldReference reference ||
            !ctx.InjectedMembers.Owns(reference.DeclaringType?.FullName) ||
            !ctx.InjectedMembers.TryGetAttached(reference.Name, out AttachedFieldSlot slot)) {
            return false;
        }

        MethodInfo definition;
        if (source.OpCode == OpCodes.Ldfld) {
            definition = AttachedGet;
        } else if (source.OpCode == OpCodes.Stfld) {
            definition = AttachedSet;
        } else if (source.OpCode == OpCodes.Ldflda) {
            definition = AttachedRef;
        } else {
            throw new ConcordEmitException(
                "CONC004",
                $"Attached field '{reference.Name}' was accessed with '{source.OpCode}'. Attached fields are per-instance, so only instance access is supported.");
        }

        MethodReference call = ctx.Module.ImportReference(definition.MakeGenericMethod(slot.ValueType));
        lowered = new List<Instruction> {
            Instruction.Create(OpCodes.Ldc_I4, slot.Slot),
            Instruction.Create(OpCodes.Call, call),
        };
        return true;
    }

    // An [InjectField] declared as object reaches a target whose type cannot be named from the
    // patch class - a private nested enum, say. The access still has to be emitted against the
    // real field, so the value is boxed on the way out and unboxed on the way back in.
    private static bool TryLowerObjectTypedInjectedField(Instruction source, LoweringContext ctx, out List<Instruction>? lowered) {
        lowered = null;
        if (source.Operand is not FieldReference reference ||
            reference.FieldType.FullName != "System.Object" ||
            !ctx.InjectedMembers.Owns(reference.DeclaringType?.FullName) ||
            !ctx.InjectedMembers.TryGetField(reference.Name, out FieldInfo? target) ||
            target.FieldType == typeof(object)) {
            return false;
        }

        FieldReference importedField = ctx.Module.ImportReference(target);
        TypeReference importedType = ctx.Module.ImportReference(target.FieldType);

        if (source.OpCode == OpCodes.Ldfld || source.OpCode == OpCodes.Ldsfld) {
            lowered = new List<Instruction> { Instruction.Create(source.OpCode, importedField) };
            if (target.FieldType.IsValueType) {
                lowered.Add(Instruction.Create(OpCodes.Box, importedType));
            }

            return true;
        }

        if (source.OpCode == OpCodes.Stfld || source.OpCode == OpCodes.Stsfld) {
            lowered = new List<Instruction> {
                Instruction.Create(target.FieldType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, importedType),
                Instruction.Create(source.OpCode, importedField),
            };
            return true;
        }

        if (source.OpCode == OpCodes.Ldflda || source.OpCode == OpCodes.Ldsflda) {
            throw new ConcordEmitException(
                "CONC126",
                $"Injected field '{reference.Name}' is declared as object against target type '{target.FieldType.Name}', " +
                "so its value is boxed on access and has no stable address. Taking its address is not supported; " +
                "read it into a local instead.");
        }

        return false;
    }

    private static Instruction CloneFieldInstruction(
        OpCode opCode,
        FieldReference field,
        ModuleDefinition module,
        InjectedMemberMap injectedMembers) {
        if (injectedMembers.Owns(field.DeclaringType?.FullName) && injectedMembers.TryGetField(field.Name, out FieldInfo? realField)) {
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
