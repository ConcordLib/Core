using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

/// <summary>
///     Builds composed wrapper methods from an original target and ordered Concord injections.
/// </summary>
public static class WrapperComposer {
    private const string CodeCONC039 = "CONC039";

    /// <summary>
    ///     Creates a wrapper method for a target and a copy of the original body.
    /// </summary>
    /// <param name="target">
    ///     The method to patch. Async and iterator methods are resolved to their generated
    ///     <c>MoveNext</c> method before composition.
    /// </param>
    /// <param name="ordered">The injections to compose, ordered by their caller.</param>
    /// <returns>The generated wrapper method and original body copy.</returns>
    public static ComposeResult Compose(MethodBase target, IReadOnlyList<Injection> ordered) {
        if (HasWholeMethodAround(ordered)) {
            ValidateWholeMethodAroundEligible(target);
            RejectCallSiteInjectionsWithWholeMethodAround(ordered, target);
        }

        MethodBase resolved = ResolveStateMachineTarget(target);

        MethodInfo originalBody = OriginalBody.Clone(resolved);

        using DynamicMethodDefinition source = new DynamicMethodDefinition(resolved);
        Type returnType = ResolveReturnType(resolved);
        Type[] parameterTypes = ResolveParameterTypes(resolved);

        using DynamicMethodDefinition wrapper = new DynamicMethodDefinition(WrapperName(resolved), returnType, parameterTypes);
        BodyCopier.CopySpine(source.Definition, wrapper.Definition);

        Assemble(wrapper.Definition, resolved, ordered, returnType);
        MethodInfo wrapperMethod = wrapper.Generate();
        return new ComposeResult(wrapperMethod, originalBody);
    }

    /// <summary>
    ///     Diagnostic variant of <see cref="Compose" />: runs the full spine-copy and assembly
    ///     pipeline but returns a textual dump of the composed wrapper body BEFORE JIT generation,
    ///     for inspecting cross-module operands, the locals table, and per-instruction stack depth.
    /// </summary>
    /// <param name="target">The method to patch.</param>
    /// <param name="ordered">The injections to compose.</param>
    /// <returns>A human-readable IL dump of the composed wrapper.</returns>
    public static string ComposeDump(MethodBase target, IReadOnlyList<Injection> ordered) {
        MethodBase resolved = ResolveStateMachineTarget(target);

        using DynamicMethodDefinition source = new DynamicMethodDefinition(resolved);
        Type returnType = ResolveReturnType(resolved);
        Type[] parameterTypes = ResolveParameterTypes(resolved);

        using DynamicMethodDefinition wrapper = new DynamicMethodDefinition(WrapperName(resolved), returnType, parameterTypes);
        BodyCopier.CopySpine(source.Definition, wrapper.Definition);

        Assemble(wrapper.Definition, resolved, ordered, returnType);

        return IlDump.Format(wrapper.Definition);
    }

    /// <summary>
    ///     Resolves async and iterator entry methods to their generated state-machine <c>MoveNext</c> method.
    /// </summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>The state-machine <c>MoveNext</c> method when present; otherwise <paramref name="target" />.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011", Justification = "Concord reaches the private state-machine MoveNext by design; validated at resolve time.")]
    public static MethodBase ResolveStateMachineTarget(MethodBase target) {
        Type? stateMachineType = ReadStateMachineType(target);
        if (stateMachineType is null) {
            return target;
        }

        MethodInfo? moveNext = stateMachineType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
        if (moveNext is null) {
            throw new ConcordEmitException(
                "CONC060",
                $"State machine type '{stateMachineType.Name}' for '{target.DeclaringType?.Name}.{target.Name}' has no MoveNext method.");
        }

        return moveNext;
    }

    /// <summary>
    ///     Rejects a target that is a reference-type generic instantiation. The runtime shares one
    ///     compiled body across all reference-type instantiations of a generic method, so a detour
    ///     installed for one instantiation runs for every other one. Value-type instantiations each
    ///     get their own body and are safe.
    /// </summary>
    /// <param name="target">The method a detour is about to be installed for.</param>
    /// <exception cref="ConcordEmitException">Thrown with <c>CONC061</c> when the target is a reference-type instantiation.</exception>
    public static void RejectSharedGenericInstantiation(MethodBase target) {
        foreach (Type argument in EnumerateGenericArguments(target)) {
            if (argument.IsGenericParameter || argument.IsValueType) {
                continue;
            }

            throw new ConcordEmitException(
                "CONC061",
                $"'{target.DeclaringType?.Name}.{target.Name}' is a generic instantiation with reference-type argument '{argument.Name}'. " +
                "The runtime shares one compiled body across all reference-type instantiations, so a detour would leak to every other one. " +
                "Patch generic targets only at value-type instantiations.");
        }
    }

    internal static void ValidateOperationShape(MethodBase injectionMethod, CallSiteShape shape, MethodBase target, bool allowValueReceiver = false) {
        if (!allowValueReceiver && shape.HasThis && shape.ReceiverType is { IsValueType: true }) {
            throw new ConcordEmitException(
                CodeCONC039,
                $"Around-invoke on '{target.DeclaringType?.Name}.{target.Name}' matches a call on value type '{shape.ReceiverType.Name}'. Value-type receivers are not supported.");
        }

        int operationArgIndex = ControlHandleLowering.FindOperationArgIndex(injectionMethod);
        if (operationArgIndex < 0) {
            throw new ConcordEmitException(
                CodeCONC039,
                $"Around-invoke injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' declares no Operation parameter.");
        }

        Type expected = shape.ExpectedOperationType();
        int offset = injectionMethod.IsStatic ? 0 : 1;
        Type declared = injectionMethod.GetParameters()[operationArgIndex - offset].ParameterType;
        if (declared != expected) {
            throw new ConcordEmitException(
                CodeCONC039,
                $"Around-invoke injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' declares '{declared.Name}' but the matched call requires '{expected.Name}'.");
        }
    }

    internal static bool IsInsideProtectedRegion(Instruction instruction, IList<ExceptionHandler> handlers) {
        return handlers.Any(handler =>
            SpansInstruction(handler.TryStart, handler.TryEnd, instruction) ||
            SpansInstruction(handler.HandlerStart, handler.HandlerEnd, instruction) ||
            SpansInstruction(handler.FilterStart, handler.HandlerStart, instruction));
    }

    private static bool HasWholeMethodAround(IReadOnlyList<Injection> ordered) {
        for (int i = 0; i < ordered.Count; i++) {
            if (ordered[i].At is InjectAt.Around) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Rejects call-site injection positions (<see cref="InjectAt.Invoke" /> in any shift, including its
    ///     <c>At.Argument</c> variant, and <see cref="InjectAt.Constant" />) when combined with a whole-method
    ///     <see cref="InjectAt.Around" /> on the same target.
    /// </summary>
    /// <param name="ordered">The full injection list being composed for <paramref name="target" />.</param>
    /// <param name="target">The original method being patched, used for the diagnostic message.</param>
    private static void RejectCallSiteInjectionsWithWholeMethodAround(IReadOnlyList<Injection> ordered, MethodBase target) {
        for (int i = 0; i < ordered.Count; i++) {
            InjectAt at = ordered[i].At;
            if (at is InjectAt.Invoke or InjectAt.Constant) {
                throw new ConcordEmitException(
                    "CONC115",
                    $"Whole-method Around on '{target.DeclaringType?.Name}.{target.Name}' cannot be combined with call-site " +
                    "(Invoke/Argument/Constant) injections on the same target. Call-site positions mutate the pre-Around spine, " +
                    "which does not compose with the per-copy splicing a whole-method Around performs.");
            }
        }
    }

    private static void ValidateWholeMethodAroundEligible(MethodBase originalTarget) {
        if (originalTarget is ConstructorInfo && originalTarget.IsStatic) {
            throw new ConcordEmitException(
                "CONC114",
                $"Whole-method Around on '{originalTarget.DeclaringType?.Name}.{originalTarget.Name}' targets a static type initializer. " +
                "Type initializers have no coherent Around contract and are not supported.");
        }

        ParameterInfo[] parameters = originalTarget.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            Type parameterType = parameters[i].ParameterType;
            if (parameterType.IsByRef) {
                throw new ConcordEmitException(
                    "CONC108",
                    $"Whole-method Around on '{originalTarget.DeclaringType?.Name}.{originalTarget.Name}' targets a byref parameter '{parameters[i].Name}'. " +
                    "Byref parameters are not supported by the Operation handle.");
            }

            if (IsUnsupportedByValueShape(parameterType)) {
                throw new ConcordEmitException(
                    "CONC109",
                    $"Whole-method Around on '{originalTarget.DeclaringType?.Name}.{originalTarget.Name}' targets parameter '{parameters[i].Name}' of type '{parameterType.Name}', " +
                    "which is a pointer, function pointer, or byref-like type. These are not supported by the Operation handle.");
            }
        }

        Type returnType = ResolveReturnType(originalTarget);
        if (returnType.IsByRef) {
            throw new ConcordEmitException(
                "CONC109",
                $"Whole-method Around on '{originalTarget.DeclaringType?.Name}.{originalTarget.Name}' returns by reference. " +
                "Ref returns are not supported by the Operation handle.");
        }

        if (IsUnsupportedByValueShape(returnType)) {
            throw new ConcordEmitException(
                "CONC109",
                $"Whole-method Around on '{originalTarget.DeclaringType?.Name}.{originalTarget.Name}' returns type '{returnType.Name}', " +
                "which is a pointer, function pointer, or byref-like type. These are not supported by the Operation handle.");
        }

        if (ReadStateMachineType(originalTarget) is not null) {
            throw new ConcordEmitException(
                "CONC110",
                $"Whole-method Around on '{originalTarget.DeclaringType?.Name}.{originalTarget.Name}' targets an async or iterator method. " +
                "State-machine methods are not supported by the Operation handle; patch at Head instead.");
        }
    }

    private static bool IsUnsupportedByValueShape(Type type) {
        if (type.IsPointer) {
            return true;
        }

        if (IsFunctionPointer(type)) {
            return true;
        }

        return IsByRefLike(type);
    }

    private static bool IsFunctionPointer(Type type) {
#if NET
        return type.IsFunctionPointer || type.IsUnmanagedFunctionPointer;
#else
        return type.Name.IndexOf("(fnptr)", StringComparison.Ordinal) >= 0;
#endif
    }

    private static bool IsByRefLike(Type type) {
#if NET
        return type.IsByRefLike;
#else
        foreach (CustomAttributeData attribute in type.GetCustomAttributesData()) { // NOSONAR project forbids LINQ in loops (perf/determinism); for-loop is intentional
            if (attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") {
                return true;
            }
        }

        return false;
#endif
    }

    private static IEnumerable<Type> EnumerateGenericArguments(MethodBase target) {
        Type? declaringType = target.DeclaringType;
        if (declaringType is { IsConstructedGenericType: true }) {
            foreach (Type argument in declaringType.GetGenericArguments()) {
                yield return argument;
            }
        }

        if (target is MethodInfo { IsGenericMethod: true } method) {
            foreach (Type argument in method.GetGenericArguments()) {
                yield return argument;
            }
        }
    }

    private static Type? ReadStateMachineType(MethodBase target) {
        AsyncStateMachineAttribute? asyncAttr = target.GetCustomAttribute<AsyncStateMachineAttribute>();
        if (asyncAttr is not null) {
            return asyncAttr.StateMachineType;
        }

        IteratorStateMachineAttribute? iterator = target.GetCustomAttribute<IteratorStateMachineAttribute>();
        return iterator?.StateMachineType;
    }

    private static void Assemble(MethodDefinition wrapperDefinition, MethodBase target, IReadOnlyList<Injection> ordered, Type returnType) {
        ValidateNonHeadInjectionsDoNotReturnControl(ordered);

        MethodBody body = wrapperDefinition.Body;
        ModuleDefinition module = wrapperDefinition.Module;
        bool isVoid = returnType == typeof(void);

        bool hasAround = HasWholeMethodAround(ordered);

        bool needsCtorGuard = hasAround && target.IsConstructor;
        ProtocolLocals locals = DeclareLocals(body, module, returnType, isVoid, hasAround && !isVoid, needsCtorGuard);

        List<Instruction> spine = new List<Instruction>(body.Instructions);
        Instruction afterSpine = Instruction.Create(OpCodes.Nop);

        List<Instruction> epilogue = BuildEpilogue(locals, isVoid);
        if (needsCtorGuard) {
            epilogue.InsertRange(0, BuildCtorExactlyOnceCheck(locals, module));
        }

        Instruction epilogueStart = epilogue[0];

        RewriteSpineReturns(spine, locals, afterSpine, isVoid, hasAround, body.ExceptionHandlers);

        NormalizeReturnSitesIfNeeded(ordered, spine, locals, afterSpine, isVoid, hasAround, body.ExceptionHandlers);

        Instruction guardStart = Instruction.Create(OpCodes.Ldloc, locals.Cancel);
        Instruction guardBranch = Instruction.Create(OpCodes.Brtrue, hasAround ? epilogueStart : afterSpine);

        WrapperAssembly context = new WrapperAssembly(wrapperDefinition, target, ordered, locals, isVoid, hasAround);
        AssemblyAnchors anchors = new AssemblyAnchors(spine, afterSpine, guardStart, epilogueStart);
        InjectionBuffers buffers = new InjectionBuffers(
            new List<List<Instruction>>(),
            new List<List<Instruction>>(),
            new List<(Injection, InjectAt.Return)>(),
            new List<Injection>());

        bool hasHead = DispatchInjections(context, anchors, buffers, out Injection? aroundInjection, out Instruction? lastExit);

        if (lastExit is not null) {
            List<Instruction> tails = ChainBodies(buffers.TailBodies, lastExit);
            RedirectProtectedRegionExits(spine, lastExit, tails[0]);
            RetargetHandlerBoundaries(body.ExceptionHandlers, lastExit, tails[0]);
            int exitIndex = spine.IndexOf(lastExit);
            spine.InsertRange(exitIndex, tails);
        }

        List<Instruction>? aroundBody = null;
        if (aroundInjection is not null) {
            buffers.AroundReturnInjections.Reverse();
            buffers.AroundTailInjections.Reverse();
            ProcessAroundInjection(new InjectionSiteContext(aroundInjection, wrapperDefinition, target, locals), epilogueStart, afterSpine, spine, buffers.AroundReturnInjections, buffers.AroundTailInjections, ref aroundBody);
        }

        List<Instruction> heads = ChainBodies(buffers.HeadBodies, guardStart);
        List<Instruction> returns = ChainBodies(new List<List<Instruction>>(), epilogueStart);

        List<Instruction> assembled = AssembleFinalBody(new AssembledBodyParts(heads, hasHead, guardStart, guardBranch, aroundBody, spine, afterSpine, returns, epilogue));

        body.Instructions.Clear();
        foreach (Instruction instruction in assembled) {
            body.Instructions.Add(instruction);
        }
    }

    private static void ValidateNonHeadInjectionsDoNotReturnControl(IReadOnlyList<Injection> ordered) {
        for (int i = 0; i < ordered.Count; i++) {
            Injection injection = ordered[i];
            if (injection.At is not InjectAt.Head && ControlHandleLowering.ReturnsControl(injection.InjectionMethod)) {
                throw new ConcordEmitException(
                    "CONC015",
                    $"Injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' returns Control; a Control return is only valid on a head injection.");
            }
        }
    }

    private static void NormalizeReturnSitesIfNeeded(
        IReadOnlyList<Injection> ordered,
        List<Instruction> spine,
        ProtocolLocals locals,
        Instruction afterSpine,
        bool isVoid,
        bool hasAround,
        IList<ExceptionHandler> exceptionHandlers) {
        if (isVoid) {
            return;
        }

        bool hasReturnSite = false;
        bool hasTailSite = false;
        for (int i = 0; i < ordered.Count; i++) {
            if (ordered[i].At is InjectAt.Return) {
                hasReturnSite = true;
            } else if (ordered[i].At is InjectAt.Tail) {
                hasTailSite = true;
            }
        }

        if (!hasAround && (hasReturnSite || hasTailSite)) {
            NormalizeReturnSites(spine, locals.ReturnValue!, afterSpine, exceptionHandlers);
        } else if (hasAround && hasTailSite) {
            NormalizeReturnSites(spine, locals.SpliceValue!, afterSpine, exceptionHandlers);
        }
    }

    private static bool DispatchInjections(
        WrapperAssembly context,
        AssemblyAnchors anchors,
        InjectionBuffers buffers,
        out Injection? aroundInjection,
        out Instruction? lastExit) {
        bool hasHead = false;
        aroundInjection = null;
        lastExit = null;

        IReadOnlyList<Injection> ordered = context.Ordered;
        for (int i = ordered.Count - 1; i >= 0; i--) {
            Injection injection = ordered[i];

            if (injection.At is InjectAt.Head) {
                ProcessHeadInjection(new InjectionSiteContext(injection, context.WrapperDefinition, context.Target, context.Locals), anchors.GuardStart, context.IsVoid, ref hasHead, buffers.HeadBodies);
                continue;
            }

            if (injection.At is InjectAt.Tail) {
                lastExit = DispatchTailInjection(context, anchors, buffers, injection, lastExit);
                continue;
            }

            if (injection.At is InjectAt.Return returnSite) {
                DispatchReturnInjection(context, anchors, buffers, injection, returnSite);
                continue;
            }

            if (injection.At is InjectAt.Invoke invoke) {
                ProcessInvokeInjection(injection, invoke, context.WrapperDefinition, context.Target, context.Locals, anchors.Spine);
                continue;
            }

            if (injection.At is InjectAt.Constant constant) {
                ProcessConstantInjection(injection, constant, context.WrapperDefinition, context.Target, anchors.Spine);
                continue;
            }

            if (injection.At is InjectAt.Around) {
                aroundInjection = RegisterAroundInjection(injection, aroundInjection, context.Target);
            }
        }

        return hasHead;
    }

    private static Instruction? DispatchTailInjection(
        WrapperAssembly context,
        AssemblyAnchors anchors,
        InjectionBuffers buffers,
        Injection injection,
        Instruction? lastExit) {
        if (context.HasAround) {
            buffers.AroundTailInjections.Add(injection);
            return lastExit;
        }

        return ProcessTailInjection(injection, context.WrapperDefinition, context.Target, context.Locals, anchors.AfterSpine, anchors.Spine, buffers.TailBodies);
    }

    private static void DispatchReturnInjection(
        WrapperAssembly context,
        AssemblyAnchors anchors,
        InjectionBuffers buffers,
        Injection injection,
        InjectAt.Return returnSite) {
        if (context.HasAround) {
            buffers.AroundReturnInjections.Add((injection, returnSite));
            return;
        }

        ProcessReturnInjection(injection, returnSite, context.WrapperDefinition, context.Target, context.Locals, anchors.AfterSpine, anchors.Spine);
    }

    private static Injection RegisterAroundInjection(Injection injection, Injection? aroundInjection, MethodBase target) {
        if (aroundInjection is not null) {
            throw new ConcordEmitException(
                "CONC051",
                $"Multiple Around injections on '{target.DeclaringType?.Name}.{target.Name}' are not supported; only one Around injection per target is allowed.");
        }

        return injection;
    }

    private static List<Instruction> AssembleFinalBody(AssembledBodyParts parts) {
        List<Instruction> assembled = new List<Instruction>();

        if (parts.AroundBody is not null) {
            assembled.AddRange(parts.Heads);
            if (parts.HasHead) {
                assembled.Add(parts.GuardStart);
                assembled.Add(parts.GuardBranch);
            }

            assembled.AddRange(parts.AroundBody);
            assembled.AddRange(parts.Epilogue);
        } else {
            assembled.AddRange(parts.Heads);
            if (parts.HasHead) {
                assembled.Add(parts.GuardStart);
                assembled.Add(parts.GuardBranch);
            }

            assembled.AddRange(parts.Spine);
            assembled.Add(parts.AfterSpine);
            assembled.AddRange(parts.Returns);
            assembled.AddRange(parts.Epilogue);
        }

        return assembled;
    }

    private static List<Instruction> ChainBodies(List<List<Instruction>> bodies, Instruction sharedTarget) {
        if (bodies.Count == 0) {
            return [];
        }

        for (int i = 0; i < bodies.Count - 1; i++) {
            List<Instruction> body = bodies[i];
            Instruction nextStart = bodies[i + 1][0];
            foreach (Instruction instruction in body.Where(instruction => ReferenceEquals(instruction.Operand, sharedTarget))) {
                instruction.Operand = nextStart;
            }
        }

        List<Instruction> chained = new List<Instruction>();
        foreach (List<Instruction> body in bodies) {
            chained.AddRange(body);
        }

        return chained;
    }

    private static void ProcessHeadInjection(
        InjectionSiteContext site,
        Instruction guardStart,
        bool isVoid,
        ref bool hasHead,
        List<List<Instruction>> heads) {
        hasHead = true;
        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(site.Injection.InjectionMethod.DeclaringType!, site.Target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(site.Injection.InjectionMethod);
        GuardCancelWithoutReturn(
            injectionMethodDefinition.Definition.Body,
            site.Target,
            isVoid,
            ControlHandleLowering.ReturnsControl(site.Injection.InjectionMethod));
        heads.Add(
            BodyCopier.CopyInjection(
                new InjectionCopyRequest(injectionMethodDefinition.Definition, site.WrapperDefinition, site.Target, site.Injection.InjectionMethod, injectedMembers),
                site.Locals,
                guardStart));
    }

    private static Instruction ProcessTailInjection(
        Injection injection,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        Instruction afterSpine,
        List<Instruction> spine,
        List<List<Instruction>> tailBodies) {
        List<Instruction> allExits = FindReturnExits(spine, afterSpine);
        if (allExits.Count == 0) {
            throw new ConcordEmitException(
                "CONC106",
                $"Tail injection on '{target.DeclaringType?.Name}.{target.Name}' found no return in the target body.");
        }

        Instruction lastExit = allExits[allExits.Count - 1];
        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
        List<Instruction> siteBody = BodyCopier.CopyInjection(
            new InjectionCopyRequest(injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers),
            locals,
            lastExit);
        tailBodies.Add(siteBody);
        return lastExit;
    }

    private static void ProcessReturnInjection(
        Injection injection,
        InjectAt.Return returnSite,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        Instruction afterSpine,
        List<Instruction> spine) {
        List<Instruction> allExits = FindReturnExits(spine, afterSpine);
        if (allExits.Count == 0) {
            throw new ConcordEmitException(
                "CONC034",
                $"Return injection on '{target.DeclaringType?.Name}.{target.Name}' found no return in the target body.");
        }

        List<Instruction> exits = SelectReturnExits(allExits, returnSite.By, target);

        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
        foreach (Instruction exit in exits) {
            List<Instruction> siteBody = BodyCopier.CopyInjection(
                new InjectionCopyRequest(injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers),
                locals,
                exit);
            RedirectIntermediateBranches(spine, exit, siteBody[0]);
            RetargetHandlerBoundaries(wrapperDefinition.Body.ExceptionHandlers, exit, siteBody[0]);
            int exitIndex = spine.IndexOf(exit);
            spine.InsertRange(exitIndex, siteBody);
        }
    }

    private static void SpliceReturnInjectionsIntoSpineCopy(
        SpineCopy spineCopy,
        List<Instruction> aroundBody,
        List<(Injection Injection, InjectAt.Return ReturnSite)> returnInjections,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        Instruction afterSpine) {
        foreach ((Injection injection, InjectAt.Return returnSite) in returnInjections) {
            List<Instruction> allExits = FindReturnExits(spineCopy.Instructions, afterSpine);
            if (allExits.Count == 0) {
                throw new ConcordEmitException(
                    "CONC034",
                    $"Return injection on '{target.DeclaringType?.Name}.{target.Name}' found no return in the target body.");
            }

            List<Instruction> exits = SelectReturnExits(allExits, returnSite.By, target);

            InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
            using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
            foreach (Instruction exit in exits) {
                List<Instruction> siteBody = BodyCopier.CopyInjection(
                    new InjectionCopyRequest(injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers),
                    locals,
                    exit,
                    insideAround: true);
                BodyCopier.RewriteSpliceArgs(siteBody, spineCopy.ArgLocals);

                RedirectIntermediateBranches(spineCopy.Instructions, exit, siteBody[0]);
                RedirectIntermediateBranches(aroundBody, exit, siteBody[0]);
                RetargetHandlerBoundaries(spineCopy.Handlers, exit, siteBody[0]);

                int spineCopyExitIndex = spineCopy.Instructions.IndexOf(exit);
                spineCopy.Instructions.InsertRange(spineCopyExitIndex, siteBody);

                int aroundBodyExitIndex = aroundBody.IndexOf(exit);
                aroundBody.InsertRange(aroundBodyExitIndex, siteBody);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3267", Justification = "Loop body copies IL, redirects branches, and splices into two collections; projecting to Select would obscure it.")]
    private static void SpliceTailInjectionsIntoSpineCopy(
        SpineCopy spineCopy,
        List<Instruction> aroundBody,
        List<Injection> tailInjections,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        Instruction afterSpine) {
        foreach (Injection injection in tailInjections) {
            List<Instruction> allExits = FindReturnExits(spineCopy.Instructions, afterSpine);
            if (allExits.Count == 0) {
                throw new ConcordEmitException(
                    "CONC106",
                    $"Tail injection on '{target.DeclaringType?.Name}.{target.Name}' found no return in the target body.");
            }

            Instruction lastExit = allExits[allExits.Count - 1];

            InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
            using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
            List<Instruction> siteBody = BodyCopier.CopyInjection(
                new InjectionCopyRequest(injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers),
                locals,
                lastExit,
                insideAround: true);
            BodyCopier.RewriteSpliceArgs(siteBody, spineCopy.ArgLocals);

            RedirectIntermediateBranches(spineCopy.Instructions, lastExit, siteBody[0]);
            RedirectIntermediateBranches(aroundBody, lastExit, siteBody[0]);
            RetargetHandlerBoundaries(spineCopy.Handlers, lastExit, siteBody[0]);

            int spineCopyExitIndex = spineCopy.Instructions.IndexOf(lastExit);
            spineCopy.Instructions.InsertRange(spineCopyExitIndex, siteBody);

            int aroundBodyExitIndex = aroundBody.IndexOf(lastExit);
            aroundBody.InsertRange(aroundBodyExitIndex, siteBody);
        }
    }

    /// <summary>
    ///     Redirects every branch in <paramref name="instructions" /> that currently targets
    ///     <paramref name="originalTarget" /> to instead target <paramref name="newTarget" />, so code about to be
    ///     spliced immediately before <paramref name="originalTarget" /> is not skipped by an earlier injection's
    ///     own trailing branch to that same exit.
    /// </summary>
    /// <param name="instructions">The instruction list to scan for branches targeting <paramref name="originalTarget" />.</param>
    /// <param name="originalTarget">The exit instruction that previously-spliced code may branch to directly.</param>
    /// <param name="newTarget">The first instruction of the newly-spliced body, which now sits before <paramref name="originalTarget" />.</param>
    private static void RedirectIntermediateBranches(List<Instruction> instructions, Instruction originalTarget, Instruction newTarget) {
        foreach (Instruction instruction in instructions.Where(instruction => !ReferenceEquals(instruction, originalTarget) && ReferenceEquals(instruction.Operand, originalTarget))) {
            instruction.Operand = newTarget;
        }
    }

    private static void RedirectProtectedRegionExits(List<Instruction> instructions, Instruction originalTarget, Instruction newTarget) {
        foreach (Instruction instruction in instructions) {
            if ((instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S)
                && ReferenceEquals(instruction.Operand, originalTarget)) {
                instruction.Operand = newTarget;
            }
        }
    }

    private static void ProcessInvokeInjection(
        Injection injection,
        InjectAt.Invoke invoke,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        List<Instruction> spine) {
        if (invoke.Shift is At.Argument) {
            ProcessArgumentInjection(injection, invoke, wrapperDefinition, target, spine);
            return;
        }

        string effectiveName = AccessorNameResolver.ResolveAccessorName(
            invoke.DeclaringType,
            invoke.Method,
            injection.InjectionMethod,
            invoke.Shift is At.Around);

        bool includeFieldReads = invoke.Shift is At.Head or At.Tail;
        List<Instruction> allSites = ControlHandleLowering.FindInvokeCallSites(
            spine,
            invoke.DeclaringType,
            effectiveName,
            invoke.ParameterTypes,
            includeFieldReads);
        if (allSites.Count == 0) {
            throw new ConcordEmitException(
                "CONC031",
                $"Injection on '{target.DeclaringType?.Name}.{target.Name}' targets invoke site '{invoke.DeclaringType.Name}.{invoke.Method}' which does not occur in the method body.");
        }

        List<Instruction> sites = SelectInvokeSites(allSites, invoke.By, target, invoke);

        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);

        if (invoke.Shift is At.Around) {
            foreach (Instruction site in sites) {
                WrapCallSite(spine, site, injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers);
            }

            return;
        }

        bool after = invoke.Shift is At.Tail;
        foreach (Instruction site in sites) {
            int siteIndex = spine.IndexOf(site);
            Instruction continuation = after ? spine[siteIndex + 1] : site;
            List<Instruction> invokeBody = BodyCopier.CopyInjection(
                new InjectionCopyRequest(injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers),
                locals,
                continuation);
            spine.InsertRange(after ? siteIndex + 1 : siteIndex, invokeBody);
        }
    }

    private static void ProcessArgumentInjection(
        Injection injection,
        InjectAt.Invoke invoke,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        List<Instruction> spine) {
        string effectiveName = AccessorNameResolver.ResolveAccessorName(invoke.DeclaringType, invoke.Method, injection.InjectionMethod, false);
        List<Instruction> allSites = ControlHandleLowering.FindInvokeCallSites(spine, invoke.DeclaringType, effectiveName, invoke.ParameterTypes);
        if (allSites.Count == 0) {
            throw new ConcordEmitException(
                "CONC031",
                $"Injection on '{target.DeclaringType?.Name}.{target.Name}' targets call site '{invoke.DeclaringType.Name}.{invoke.Method}' which does not occur in the method body.");
        }

        List<Instruction> sites = SelectInvokeSites(allSites, invoke.By, target, invoke);
        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);

        ModuleDefinition module = wrapperDefinition.Module;
        MethodBody body = wrapperDefinition.Body;

        foreach (Instruction site in sites) {
            MethodBase resolvedOriginal = ((MethodReference)site.Operand).ResolveReflection();
            CallSiteShape shape = CallSiteShape.Resolve(resolvedOriginal);
            int argIndex = ResolveArgumentIndex(invoke, injection.InjectionMethod, shape, target);
            ValidateValueInjectionShape(injection.InjectionMethod, shape.ParameterTypes[argIndex], target);

            List<VariableDefinition> argLocals = new List<VariableDefinition>(shape.ParameterTypes.Length);
            for (int i = 0; i < shape.ParameterTypes.Length; i++) {
                VariableDefinition local = new VariableDefinition(module.ImportReference(shape.ParameterTypes[i]));
                body.Variables.Add(local);
                argLocals.Add(local);
            }

            VariableDefinition? receiverLocal = null;
            if (shape.HasThis) {
                receiverLocal = new VariableDefinition(module.ImportReference(shape.ReceiverType!));
                body.Variables.Add(receiverLocal);
            }

            List<Instruction> block = new List<Instruction>();
            for (int i = argLocals.Count - 1; i >= 0; i--) {
                block.Add(Instruction.Create(OpCodes.Stloc, argLocals[i]));
            }

            if (receiverLocal is not null) {
                block.Add(Instruction.Create(OpCodes.Stloc, receiverLocal));
            }

            block.AddRange(BodyCopier.CopyValueInjection(
                injectionMethodDefinition.Definition,
                wrapperDefinition,
                target,
                injection.InjectionMethod,
                injectedMembers,
                argLocals[argIndex]));
            block.Add(Instruction.Create(OpCodes.Stloc, argLocals[argIndex]));

            if (receiverLocal is not null) {
                block.Add(Instruction.Create(OpCodes.Ldloc, receiverLocal));
            }

            for (int i = 0; i < argLocals.Count; i++) {
                block.Add(Instruction.Create(OpCodes.Ldloc, argLocals[i]));
            }

            int siteIndex = spine.IndexOf(site);
            spine.InsertRange(siteIndex, block);
        }
    }

    private static int ResolveArgumentIndex(InjectAt.Invoke invoke, MethodBase injectionMethod, CallSiteShape shape, MethodBase target) {
        if (invoke.Arg > 0) {
            if (invoke.Arg > shape.ParameterTypes.Length) {
                throw new ConcordEmitException(
                    CodeCONC039,
                    $"Argument injection on '{target.DeclaringType?.Name}.{target.Name}' selects arg {invoke.Arg}, but the call has {shape.ParameterTypes.Length} argument(s).");
            }

            return (int)(invoke.Arg - 1);
        }

        ParameterInfo[] parameters = injectionMethod.GetParameters();
        Type valueType = parameters[0].ParameterType;
        int found = -1;
        for (int i = 0; i < shape.ParameterTypes.Length; i++) {
            if (shape.ParameterTypes[i] != valueType) {
                continue;
            }

            if (found >= 0) {
                throw new ConcordEmitException(
                    CodeCONC039,
                    $"Argument injection on '{target.DeclaringType?.Name}.{target.Name}' matches more than one '{valueType.Name}' argument; pass arg: to select one.");
            }

            found = i;
        }

        if (found < 0) {
            throw new ConcordEmitException(
                CodeCONC039,
                $"Argument injection on '{target.DeclaringType?.Name}.{target.Name}' matches no '{valueType.Name}' argument.");
        }

        return found;
    }

    private static void ProcessConstantInjection(
        Injection injection,
        InjectAt.Constant constant,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        List<Instruction> spine) {
        List<Instruction> allMatches = ConstantMatcher.FindMatches(spine, constant.Value);
        if (allMatches.Count == 0) {
            throw new ConcordEmitException(
                "CONC037",
                $"Constant injection on '{target.DeclaringType?.Name}.{target.Name}' matched no '{constant.Value}' literal in the target body.");
        }

        if (constant.By > allMatches.Count) {
            throw new ConcordEmitException(
                "CONC038",
                $"Constant injection on '{target.DeclaringType?.Name}.{target.Name}' targets occurrence {constant.By} of '{constant.Value}', but only {allMatches.Count} occurrence(s) exist.");
        }

        List<Instruction> matches = constant.By == 0 ? allMatches : [allMatches[(int)(constant.By - 1)]];

        ValidateValueInjectionShape(injection.InjectionMethod, constant.Value.GetType(), target);

        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);

        ModuleDefinition module = wrapperDefinition.Module;
        foreach (Instruction match in matches) {
            VariableDefinition valueLocal = new VariableDefinition(module.ImportReference(constant.Value.GetType()));
            wrapperDefinition.Body.Variables.Add(valueLocal);

            List<Instruction> splice = new List<Instruction> { Instruction.Create(OpCodes.Stloc, valueLocal) };
            splice.AddRange(BodyCopier.CopyValueInjection(
                injectionMethodDefinition.Definition,
                wrapperDefinition,
                target,
                injection.InjectionMethod,
                injectedMembers,
                valueLocal));

            int matchIndex = spine.IndexOf(match);
            spine.InsertRange(matchIndex + 1, splice);
        }
    }

    private static void ValidateValueInjectionShape(MethodBase injectionMethod, Type valueType, MethodBase target) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        if (parameters.Length != 1) {
            throw new ConcordEmitException(
                CodeCONC039,
                $"Value injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{target.DeclaringType?.Name}.{target.Name}' must declare exactly one parameter, got {parameters.Length}.");
        }

        Type returnType = injectionMethod is MethodInfo methodInfo ? methodInfo.ReturnType : typeof(void);
        if (parameters[0].ParameterType != valueType || returnType != valueType) {
            throw new ConcordEmitException(
                CodeCONC039,
                $"Value injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{target.DeclaringType?.Name}.{target.Name}' must be shaped '{valueType.Name} M({valueType.Name} original)'.");
        }
    }

    private static void ProcessAroundInjection(
        InjectionSiteContext site,
        Instruction epilogueStart,
        Instruction afterSpine,
        List<Instruction> spine,
        List<(Injection Injection, InjectAt.Return ReturnSite)> returnInjections,
        List<Injection> tailInjections,
        ref List<Instruction>? aroundBody) {
        if (aroundBody is not null) {
            throw new ConcordEmitException(
                "CONC051",
                $"Multiple Around injections on '{site.Target.DeclaringType?.Name}.{site.Target.Name}' are not supported; only one Around injection per target is allowed.");
        }

        ValidateWholeMethodOperationOnly(site.Injection.InjectionMethod, site.Target);
        CallSiteShape shape = CallSiteShape.Resolve(site.Target);
        ValidateOperationShape(site.Injection.InjectionMethod, shape, site.Target, allowValueReceiver: true);

        HashSet<VariableDefinition> protocolLocals = CollectProtocolLocals(site.Locals);

        SpineTemplate template = SpineTemplate.Capture(spine, site.WrapperDefinition.Body.ExceptionHandlers, protocolLocals);

        foreach (ExceptionHandler handler in template.Handlers) {
            site.WrapperDefinition.Body.ExceptionHandlers.Remove(handler);
        }

        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(site.Injection.InjectionMethod.DeclaringType!, site.Target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(site.Injection.InjectionMethod);
        EnsureOperationInvokeNotInLoop(injectionMethodDefinition.Definition.Body, site.Injection.InjectionMethod);
        EnsureAroundInvokePlacement(injectionMethodDefinition.Definition, template.Handlers.Count > 0, site.Injection.InjectionMethod);

        if (site.Target.IsConstructor) {
            EnsureConstructorHasInvokeSite(injectionMethodDefinition.Definition.Body, site.Injection.InjectionMethod, site.Target);
        }

        List<SpineCopy> spineCopies = [];
        aroundBody = BodyCopier.CopyInjection(
            new InjectionCopyRequest(injectionMethodDefinition.Definition, site.WrapperDefinition, site.Target, site.Injection.InjectionMethod, injectedMembers),
            site.Locals,
            epilogueStart,
            template,
            spineCopies);

        foreach (SpineCopy spineCopy in spineCopies) {
            foreach (ExceptionHandler handler in spineCopy.Handlers) {
                site.WrapperDefinition.Body.ExceptionHandlers.Add(handler);
            }
        }

        SpliceCallSiteInjectionsIntoSpineCopies(site, aroundBody, spineCopies, returnInjections, tailInjections, afterSpine);

        RetargetAroundSpineBranches(aroundBody, spineCopies, afterSpine, epilogueStart, site.Locals);

        if (site.Target.IsConstructor) {
            GuardCtorSpineCopiesAgainstReentry(aroundBody, spineCopies, site.Locals);
        }
    }

    private static HashSet<VariableDefinition> CollectProtocolLocals(ProtocolLocals locals) {
        HashSet<VariableDefinition> protocolLocals = [locals.Cancel];
        if (locals.HasReturn is not null) {
            protocolLocals.Add(locals.HasReturn);
        }

        if (locals.ReturnValue is not null) {
            protocolLocals.Add(locals.ReturnValue);
        }

        if (locals.SpliceValue is not null) {
            protocolLocals.Add(locals.SpliceValue);
        }

        return protocolLocals;
    }

    private static void SpliceCallSiteInjectionsIntoSpineCopies(
        InjectionSiteContext site,
        List<Instruction> aroundBody,
        List<SpineCopy> spineCopies,
        List<(Injection Injection, InjectAt.Return ReturnSite)> returnInjections,
        List<Injection> tailInjections,
        Instruction afterSpine) {
        if (returnInjections.Count > 0) {
            foreach (SpineCopy spineCopy in spineCopies) {
                SpliceReturnInjectionsIntoSpineCopy(spineCopy, aroundBody, returnInjections, site.WrapperDefinition, site.Target, site.Locals, afterSpine);
            }
        }

        if (tailInjections.Count > 0) {
            foreach (SpineCopy spineCopy in spineCopies) {
                SpliceTailInjectionsIntoSpineCopy(spineCopy, aroundBody, tailInjections, site.WrapperDefinition, site.Target, site.Locals, afterSpine);
            }
        }
    }

    private static void ValidateWholeMethodOperationOnly(MethodBase injectionMethod, MethodBase target) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int operationCount = 0;
        int controlHandleCount = 0;
        for (int i = 0; i < parameters.Length; i++) {
            if (ControlHandleLowering.IsOperationType(parameters[i].ParameterType)) {
                operationCount++;
            } else if (ControlHandleLowering.IsControlHandleType(parameters[i].ParameterType)) {
                controlHandleCount++;
            }
        }

        if (operationCount == 1 && controlHandleCount == 0) {
            return;
        }

        throw new ConcordEmitException(
            "CONC111",
            $"Whole-method Around injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{target.DeclaringType?.Name}.{target.Name}' " +
            "must declare exactly one Operation parameter and no ControlHandle parameters.");
    }

    private static void EnsureOperationInvokeNotInLoop(MethodBody injectionBody, MethodBase injectionMethod) {
        List<Instruction> instructions = new List<Instruction>(injectionBody.Instructions);
        for (int i = 0; i < instructions.Count; i++) {
            if (!ControlHandleLowering.IsOperationInvoke(instructions[i])) {
                continue;
            }

            for (int b = i + 1; b < instructions.Count; b++) {
                if (!BranchTargetsAtOrBefore(instructions[b], instructions, i)) {
                    continue;
                }

                throw new ConcordEmitException(
                    "CONC113",
                    "The Operation handle Invoke(...) call in injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name +
                    "' is inside a loop; the original body can only be spliced once.");
            }
        }
    }

    private static void EnsureConstructorHasInvokeSite(MethodBody injectionBody, MethodBase injectionMethod, MethodBase target) {
        if (injectionBody.Instructions.Any(ControlHandleLowering.IsOperationInvoke)) {
            return;
        }

        throw new ConcordEmitException(
            "CONC112",
            $"Whole-method Around injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on constructor '{target.DeclaringType?.Name}.{target.Name}' " +
            "never calls Invoke(...); a constructor Around must invoke the original constructor exactly once.");
    }

    private static bool BranchTargetsAtOrBefore(Instruction branch, List<Instruction> instructions, int index) {
        if (branch.Operand is Instruction singleTarget) {
            int targetIndex = instructions.IndexOf(singleTarget);
            return targetIndex >= 0 && targetIndex <= index;
        }

        if (branch.Operand is Instruction[] switchTargets) {
            for (int t = 0; t < switchTargets.Length; t++) {
                int targetIndex = instructions.IndexOf(switchTargets[t]);
                if (targetIndex >= 0 && targetIndex <= index) {
                    return true;
                }
            }
        }

        return false;
    }

    private static void EnsureAroundInvokePlacement(MethodDefinition injectionDefinition, bool bodyHasHandlers, MethodBase injectionMethod) {
        if (!bodyHasHandlers) {
            return;
        }

        MethodBody injectionBody = injectionDefinition.Body;
        List<Instruction> instructions = new List<Instruction>(injectionBody.Instructions);
        if (instructions.Count == 0) {
            return;
        }

        int[] entryDepth = ComputeEntryDepths(instructions, injectionDefinition);

        for (int i = 0; i < instructions.Count; i++) {
            Instruction instruction = instructions[i];
            if (!ControlHandleLowering.IsOperationInvoke(instruction)) {
                continue;
            }

            int ambientDepth = entryDepth[i] - IlDump.PopCount(instruction, injectionDefinition);
            if (ambientDepth > 0) {
                throw new ConcordEmitException(
                    "CONC107",
                    "The Operation handle Invoke(...) call in injection '" + injectionMethod.DeclaringType?.Name + "." + injectionMethod.Name +
                    "' is used mid-expression on a target with exception handlers; splicing the original body clears the evaluation stack on any protected-region exit. " +
                    "Use Invoke(...) only as a statement, a direct assignment, or a direct return.");
            }
        }
    }

    private static int[] ComputeEntryDepths(List<Instruction> instructions, MethodDefinition injectionDefinition) {
        Dictionary<Instruction, int> indexOf = new Dictionary<Instruction, int>(instructions.Count);
        for (int i = 0; i < instructions.Count; i++) {
            indexOf[instructions[i]] = i;
        }

        int[] entryDepth = new int[instructions.Count];
        bool[] seen = new bool[instructions.Count];
        Queue<int> work = new Queue<int>();

        seen[0] = true;
        entryDepth[0] = 0;
        work.Enqueue(0);

        foreach (ExceptionHandler handler in injectionDefinition.Body.ExceptionHandlers) { // NOSONAR project forbids LINQ in loops (perf/determinism); for-loop is intentional
            SeedDepth(handler.HandlerStart, handler.HandlerType == ExceptionHandlerType.Finally ? 0 : 1, indexOf, entryDepth, seen, work);
            SeedDepth(handler.FilterStart, 1, indexOf, entryDepth, seen, work);
        }

        while (work.Count > 0) {
            PropagateEntryDepth(work.Dequeue(), instructions, injectionDefinition, indexOf, entryDepth, seen, work);
        }

        return entryDepth;
    }

    private static void PropagateEntryDepth(
        int idx,
        List<Instruction> instructions,
        MethodDefinition injectionDefinition,
        Dictionary<Instruction, int> indexOf,
        int[] entryDepth,
        bool[] seen,
        Queue<int> work) {
        Instruction instruction = instructions[idx];
        int depth = entryDepth[idx];

        int after = depth - IlDump.PopCount(instruction, injectionDefinition) + IlDump.PushCount(instruction);
        if (after < 0) {
            after = 0;
        }

        if (instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S) {
            SeedDepth(instruction.Operand as Instruction, 0, indexOf, entryDepth, seen, work);
            return;
        }

        if (instruction.Operand is Instruction branchTarget) {
            SeedDepth(branchTarget, after, indexOf, entryDepth, seen, work);
        } else if (instruction.Operand is Instruction[] switchTargets) {
            for (int t = 0; t < switchTargets.Length; t++) {
                SeedDepth(switchTargets[t], after, indexOf, entryDepth, seen, work);
            }
        }

        FlowControl flow = instruction.OpCode.FlowControl;
        if (flow is FlowControl.Branch or FlowControl.Return or FlowControl.Throw) {
            return;
        }

        if (idx + 1 < instructions.Count) {
            SeedDepth(instructions[idx + 1], after, indexOf, entryDepth, seen, work);
        }
    }

    private static void SeedDepth(
        Instruction? target,
        int depth,
        Dictionary<Instruction, int> indexOf,
        int[] entryDepth,
        bool[] seen,
        Queue<int> work) {
        if (target is null || !indexOf.TryGetValue(target, out int idx) || seen[idx]) {
            return;
        }

        seen[idx] = true;
        entryDepth[idx] = depth;
        work.Enqueue(idx);
    }

    private static void WrapCallSite(
        List<Instruction> spine,
        Instruction site,
        MethodDefinition injectionMethodDefinition,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        MethodBase injectionMethod,
        InjectedMemberMap injectedMembers) {
        MethodReference originalCall = (MethodReference)site.Operand;
        MethodBase resolvedOriginal = originalCall.ResolveReflection();
        CallSiteShape shape = CallSiteShape.Resolve(resolvedOriginal);
        ValidateOperationShape(injectionMethod, shape, target);

        ModuleDefinition module = wrapperDefinition.Module;
        MethodBody body = wrapperDefinition.Body;

        List<VariableDefinition> argLocals = new List<VariableDefinition>(shape.ParameterTypes.Length);
        for (int i = 0; i < shape.ParameterTypes.Length; i++) {
            VariableDefinition local = new VariableDefinition(module.ImportReference(shape.ParameterTypes[i]));
            body.Variables.Add(local);
            argLocals.Add(local);
        }

        VariableDefinition? receiverLocal = null;
        if (shape.HasThis) {
            receiverLocal = new VariableDefinition(module.ImportReference(shape.ReceiverType!));
            body.Variables.Add(receiverLocal);
        }

        List<Instruction> spill = new List<Instruction>(argLocals.Count + 1);
        for (int i = argLocals.Count - 1; i >= 0; i--) {
            spill.Add(Instruction.Create(OpCodes.Stloc, argLocals[i]));
        }

        if (receiverLocal is not null) {
            spill.Add(Instruction.Create(OpCodes.Stloc, receiverLocal));
        }

        Instruction wrapEnd = Instruction.Create(OpCodes.Nop);
        List<Instruction> wrapBody = BodyCopier.CopyWrapInjection(
            new InjectionCopyRequest(injectionMethodDefinition, wrapperDefinition, target, injectionMethod, injectedMembers),
            wrapEnd,
            originalCall,
            receiverLocal,
            argLocals,
            site.OpCode,
            shape);

        int siteIndex = spine.IndexOf(site);
        spine.RemoveAt(siteIndex);

        List<Instruction> replacement = new List<Instruction>(spill.Count + wrapBody.Count + 1);
        replacement.AddRange(spill);
        replacement.AddRange(wrapBody);
        replacement.Add(wrapEnd);
        spine.InsertRange(siteIndex, replacement);
    }

    private static List<Instruction> SelectInvokeSites(List<Instruction> allSites, uint by, MethodBase target, InjectAt.Invoke invoke) {
        if (by == 0) {
            return allSites;
        }

        if (by > allSites.Count) {
            throw new ConcordEmitException(
                "CONC033",
                $"Injection on '{target.DeclaringType?.Name}.{target.Name}' targets occurrence {by} of invoke site '{invoke.DeclaringType.Name}.{invoke.Method}', but only {allSites.Count} occurrence(s) exist in the method body.");
        }

        return [allSites[(int)(by - 1)]];
    }

    private static List<Instruction> FindReturnExits(List<Instruction> spine, Instruction afterSpine) {
        List<Instruction> exits = new List<Instruction>();
        foreach (Instruction instruction in spine) {
            if ((instruction.OpCode == OpCodes.Br || instruction.OpCode == OpCodes.Leave)
                && ReferenceEquals(instruction.Operand, afterSpine)) {
                exits.Add(instruction);
            }
        }

        return exits;
    }

    private static List<Instruction> SelectReturnExits(List<Instruction> allExits, uint by, MethodBase target) {
        if (by == 0) {
            return allExits;
        }

        if (by > allExits.Count) {
            throw new ConcordEmitException(
                "CONC035",
                $"Return injection on '{target.DeclaringType?.Name}.{target.Name}' targets occurrence {by}, but only {allExits.Count} return(s) exist in the method body.");
        }

        return [allExits[(int)(by - 1)]];
    }

    private static ProtocolLocals DeclareLocals(
        MethodBody body,
        ModuleDefinition module,
        Type returnType,
        bool isVoid,
        bool needsSpliceValue,
        bool needsCtorGuard) {
        VariableDefinition cancel = new VariableDefinition(module.ImportReference(typeof(bool)));
        body.Variables.Add(cancel);
        body.InitLocals = true;

        VariableDefinition? ctorBodyRan = null;
        VariableDefinition? ctorBodyRanTwice = null;
        if (needsCtorGuard) {
            ctorBodyRan = new VariableDefinition(module.ImportReference(typeof(bool)));
            ctorBodyRanTwice = new VariableDefinition(module.ImportReference(typeof(bool)));
            body.Variables.Add(ctorBodyRan);
            body.Variables.Add(ctorBodyRanTwice);
        }

        if (isVoid) {
            return new ProtocolLocals(cancel, null, null, null, ctorBodyRan, ctorBodyRanTwice);
        }

        VariableDefinition hasReturn = new VariableDefinition(module.ImportReference(typeof(bool)));
        VariableDefinition returnValue = new VariableDefinition(module.ImportReference(returnType));
        body.Variables.Add(hasReturn);
        body.Variables.Add(returnValue);

        VariableDefinition? spliceValue = null;
        if (needsSpliceValue) {
            spliceValue = new VariableDefinition(module.ImportReference(returnType));
            body.Variables.Add(spliceValue);
        }

        return new ProtocolLocals(cancel, hasReturn, returnValue, spliceValue, ctorBodyRan, ctorBodyRanTwice);
    }

    private static List<Instruction> BuildEpilogue(ProtocolLocals locals, bool isVoid) {
        if (isVoid) {
            return new List<Instruction> { Instruction.Create(OpCodes.Ret) };
        }

        return new List<Instruction> { Instruction.Create(OpCodes.Ldloc, locals.ReturnValue!), Instruction.Create(OpCodes.Ret) };
    }

    private static List<Instruction> BuildCtorExactlyOnceCheck(ProtocolLocals locals, ModuleDefinition module) {
        MethodReference exceptionCtor = module.ImportReference(typeof(InvalidOperationException).GetConstructor([typeof(string)]));

        Instruction afterTwiceCheck = Instruction.Create(OpCodes.Nop);
        Instruction afterZeroCheck = Instruction.Create(OpCodes.Nop);

        List<Instruction> instructions = new List<Instruction> {
            Instruction.Create(OpCodes.Ldloc, locals.CtorBodyRanTwice!),
            Instruction.Create(OpCodes.Brfalse, afterTwiceCheck),
            Instruction.Create(OpCodes.Ldstr, "Constructor Around invoked the original constructor body more than once; the pre-entry guard blocked the second attempt."),
            Instruction.Create(OpCodes.Newobj, exceptionCtor),
            Instruction.Create(OpCodes.Throw),
            afterTwiceCheck,
            Instruction.Create(OpCodes.Ldloc, locals.CtorBodyRan!),
            Instruction.Create(OpCodes.Brtrue, afterZeroCheck),
            Instruction.Create(OpCodes.Ldstr, "Constructor Around never invoked the original constructor body; the object was not fully constructed."),
            Instruction.Create(OpCodes.Newobj, exceptionCtor),
            Instruction.Create(OpCodes.Throw),
            afterZeroCheck,
        };

        return instructions;
    }

    private static void RewriteSpineReturns(
        List<Instruction> spine,
        ProtocolLocals locals,
        Instruction afterSpine,
        bool isVoid,
        bool isAroundSplice,
        IList<ExceptionHandler> handlers) {
        int i = 0;
        while (i < spine.Count) {
            Instruction instruction = spine[i];
            if (instruction.OpCode != OpCodes.Ret) {
                i++;
                continue;
            }

            if (isAroundSplice) {
                OpCode exit = IsInsideProtectedRegion(instruction, handlers) ? OpCodes.Leave : OpCodes.Br;

                if (isVoid) {
                    instruction.OpCode = exit;
                    instruction.Operand = afterSpine;
                    i++;
                    continue;
                }

                instruction.OpCode = OpCodes.Stloc;
                instruction.Operand = locals.SpliceValue!;
                spine.Insert(i + 1, Instruction.Create(exit, afterSpine));
                i += 2;
                continue;
            }

            if (isVoid) {
                instruction.OpCode = OpCodes.Br;
                instruction.Operand = afterSpine;
                i++;
                continue;
            }

            instruction.OpCode = OpCodes.Stloc;
            instruction.Operand = locals.ReturnValue!;
            spine.Insert(i + 1, Instruction.Create(OpCodes.Br, afterSpine));
            i += 2;
        }
    }

    private static void NormalizeReturnSites(List<Instruction> spine, VariableDefinition exitLocal, Instruction afterSpine, IList<ExceptionHandler> handlers) {
        Instruction? exitStore = FindExitStore(spine, exitLocal, afterSpine);
        if (exitStore is null) {
            return;
        }

        int storeIndex = spine.IndexOf(exitStore);
        if (storeIndex == 0) {
            return;
        }

        Instruction sharedLoad = spine[storeIndex - 1];
        if (!IsLoadLocal(sharedLoad.OpCode)) {
            return;
        }

        int sharedLoadIndex = storeIndex - 1;
        if (sharedLoadIndex == 0 || !IsUnconditionalExit(spine[sharedLoadIndex - 1].OpCode)) {
            return;
        }

        List<Instruction> branchSites = new List<Instruction>();
        foreach (Instruction instruction in spine) {
            if ((instruction.OpCode == OpCodes.Br || instruction.OpCode == OpCodes.Br_S
                || instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S)
                && ReferenceEquals(instruction.Operand, sharedLoad)) {
                branchSites.Add(instruction);
            }
        }

        if (branchSites.Count == 0) {
            return;
        }

        foreach (Instruction branch in branchSites) {
            int branchIndex = spine.IndexOf(branch);
            OpCode exit = IsInsideProtectedRegion(branch, handlers) ? OpCodes.Leave : OpCodes.Br;
            spine[branchIndex] = CloneLoadLocal(sharedLoad);
            spine.Insert(branchIndex + 1, Instruction.Create(OpCodes.Stloc, exitLocal));
            spine.Insert(branchIndex + 2, Instruction.Create(exit, afterSpine));
        }

        RetargetHandlerBoundaries(handlers, sharedLoad, afterSpine);

        int deadTailIndex = spine.IndexOf(sharedLoad);
        spine.RemoveAt(deadTailIndex + 2);
        spine.RemoveAt(deadTailIndex + 1);
        spine.RemoveAt(deadTailIndex);
    }

    private static Instruction? FindExitStore(List<Instruction> spine, VariableDefinition exitLocal, Instruction afterSpine) {
        for (int i = 0; i < spine.Count - 1; i++) {
            Instruction store = spine[i];
            Instruction branch = spine[i + 1];
            if (store.OpCode == OpCodes.Stloc && ReferenceEquals(store.Operand, exitLocal)
                && branch.OpCode == OpCodes.Br && ReferenceEquals(branch.Operand, afterSpine)) {
                return store;
            }
        }

        return null;
    }

    private static bool IsLoadLocal(OpCode opCode) {
        return opCode == OpCodes.Ldloc
            || opCode == OpCodes.Ldloc_S
            || opCode == OpCodes.Ldloc_0
            || opCode == OpCodes.Ldloc_1
            || opCode == OpCodes.Ldloc_2
            || opCode == OpCodes.Ldloc_3;
    }

    private static Instruction CloneLoadLocal(Instruction load) {
        if (load.Operand is VariableDefinition variable) {
            return Instruction.Create(load.OpCode, variable);
        }

        return Instruction.Create(load.OpCode);
    }

    private static bool IsUnconditionalExit(OpCode opCode) {
        return opCode == OpCodes.Br
            || opCode == OpCodes.Br_S
            || opCode == OpCodes.Ret
            || opCode == OpCodes.Throw
            || opCode == OpCodes.Rethrow
            || opCode == OpCodes.Leave
            || opCode == OpCodes.Leave_S
            || opCode == OpCodes.Endfinally
            || opCode == OpCodes.Endfilter;
    }

    private static void RetargetHandlerBoundaries(IList<ExceptionHandler> handlers, Instruction from, Instruction to) {
        foreach (ExceptionHandler handler in handlers) { // NOSONAR project forbids LINQ in loops (perf/determinism); for-loop is intentional
            if (ReferenceEquals(handler.TryEnd, from)) {
                handler.TryEnd = to;
            }

            if (ReferenceEquals(handler.HandlerEnd, from)) {
                handler.HandlerEnd = to;
            }

            if (ReferenceEquals(handler.FilterStart, from)) {
                handler.FilterStart = to;
            }
        }
    }

    private static bool SpansInstruction(Instruction? start, Instruction? end, Instruction instruction) {
        if (start is null) {
            return false;
        }

        for (Instruction? cursor = start; cursor is not null && cursor != end; cursor = cursor.Next) {
            if (cursor == instruction) {
                return true;
            }
        }

        return false;
    }

    private static void GuardCancelWithoutReturn(MethodBody injectionMethodBody, MethodBase target, bool isVoid, bool returnsControl) {
        if (isVoid) {
            return;
        }

        bool cancels = returnsControl || ControlHandleLowering.InjectionMethodCancels(injectionMethodBody);
        if (cancels && !ControlHandleLowering.InjectionMethodSetsReturnValue(injectionMethodBody)) {
            throw new ConcordEmitException(
                "CONC012",
                $"Injection on non-void target '{target.DeclaringType?.Name}.{target.Name}' cancels without setting ReturnValue; a return value is required when the original method is skipped.");
        }
    }

    private static void RetargetAroundSpineBranches(
        List<Instruction> aroundBody,
        List<SpineCopy> spineCopies,
        Instruction afterSpine,
        Instruction epilogueStart,
        ProtocolLocals locals) {
        foreach (SpineCopy spineCopy in spineCopies) {
            RetargetSpineCopyBranches(aroundBody, spineCopy, afterSpine, epilogueStart, locals);
        }
    }

    private static void RetargetSpineCopyBranches(
        List<Instruction> aroundBody,
        SpineCopy spineCopy,
        Instruction afterSpine,
        Instruction epilogueStart,
        ProtocolLocals locals) {
        List<Instruction> spine = spineCopy.Instructions;
        if (spine.Count == 0) {
            return;
        }

        Instruction lastSpineInstruction = spine[spine.Count - 1];
        int lastSpineIndex = aroundBody.LastIndexOf(lastSpineInstruction);
        Instruction postStart = lastSpineIndex + 1 < aroundBody.Count
            ? aroundBody[lastSpineIndex + 1]
            : epilogueStart;

        Instruction spliceJoin = postStart;
        if (locals.SpliceValue is not null) {
            spliceJoin = Instruction.Create(OpCodes.Ldloc, locals.SpliceValue);
            int insertAt = lastSpineIndex + 1;
            if (insertAt < aroundBody.Count) {
                aroundBody.Insert(insertAt, spliceJoin);
            } else {
                aroundBody.Add(spliceJoin);
            }
        }

        foreach (Instruction instruction in spine.Where(instruction => ReferenceEquals(instruction.Operand, afterSpine))) {
            instruction.Operand = spliceJoin;
        }
    }

    private static void GuardCtorSpineCopiesAgainstReentry(List<Instruction> aroundBody, List<SpineCopy> spineCopies, ProtocolLocals locals) {
        foreach (SpineCopy spineCopy in spineCopies) {
            GuardCtorSpineCopyAgainstReentry(aroundBody, spineCopy, locals);
        }
    }

    private static void GuardCtorSpineCopyAgainstReentry(List<Instruction> aroundBody, SpineCopy spineCopy, ProtocolLocals locals) {
        List<Instruction> spine = spineCopy.Instructions;
        if (spine.Count == 0) {
            return;
        }

        Instruction firstSpineInstruction = spine[0];
        Instruction lastSpineInstruction = spine[spine.Count - 1];
        int firstSpineIndex = aroundBody.IndexOf(firstSpineInstruction);
        int lastSpineIndex = aroundBody.LastIndexOf(lastSpineInstruction);

        Instruction skipTarget = Instruction.Create(OpCodes.Nop);
        int skipInsertAt = lastSpineIndex + 1;
        if (skipInsertAt < aroundBody.Count) {
            aroundBody.Insert(skipInsertAt, skipTarget);
        } else {
            aroundBody.Add(skipTarget);
        }

        Instruction markRan = Instruction.Create(OpCodes.Ldc_I4_1);
        List<Instruction> guardEntry = new List<Instruction> {
            Instruction.Create(OpCodes.Ldloc, locals.CtorBodyRan!),
            Instruction.Create(OpCodes.Brfalse, markRan),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Stloc, locals.CtorBodyRanTwice!),
            Instruction.Create(OpCodes.Br, skipTarget),
            markRan,
            Instruction.Create(OpCodes.Stloc, locals.CtorBodyRan!),
        };

        aroundBody.InsertRange(firstSpineIndex, guardEntry);
    }

    private static Type ResolveReturnType(MethodBase target) {
        return target is MethodInfo method ? method.ReturnType : typeof(void);
    }

    private static Type[] ResolveParameterTypes(MethodBase target) {
        ParameterInfo[] parameters = target.GetParameters();
        bool hasThis = !target.IsStatic;
        Type[] result = new Type[parameters.Length + (hasThis ? 1 : 0)];

        int offset = 0;
        if (hasThis) {
            result[0] = ThisParameterType(target);
            offset = 1;
        }

        for (int i = 0; i < parameters.Length; i++) {
            result[offset + i] = parameters[i].ParameterType;
        }

        return result;
    }

    private static Type ThisParameterType(MethodBase target) {
        Type declaring = target.DeclaringType!;
        return declaring.IsValueType ? declaring.MakeByRefType() : declaring;
    }

    private static string WrapperName(MethodBase target) {
        return $"{target.DeclaringType?.Name}.{target.Name}\u2039concord\u203A";
    }
}
