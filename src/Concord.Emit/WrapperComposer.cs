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
    public static MethodBase ResolveStateMachineTarget(MethodBase target) {
        Type? stateMachineType = ReadStateMachineType(target);
        if (stateMachineType is null) {
            return target;
        }

        MethodInfo? moveNext = stateMachineType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance); // NOSONAR concord reaches private target members by design; validated at resolve time
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

    internal static void ValidateOperationShape(MethodBase injectionMethod, CallSiteShape shape, MethodBase target) {
        if (shape.HasThis && shape.ReceiverType is { IsValueType: true }) {
            throw new ConcordEmitException(
                "CONC039",
                $"Around-invoke on '{target.DeclaringType?.Name}.{target.Name}' matches a call on value type '{shape.ReceiverType.Name}'. Value-type receivers are not supported.");
        }

        int operationArgIndex = ControlHandleLowering.FindOperationArgIndex(injectionMethod);
        if (operationArgIndex < 0) {
            throw new ConcordEmitException(
                "CONC039",
                $"Around-invoke injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' declares no Operation parameter.");
        }

        Type expected = shape.ExpectedOperationType();
        int offset = injectionMethod.IsStatic ? 0 : 1;
        Type declared = injectionMethod.GetParameters()[operationArgIndex - offset].ParameterType;
        if (declared != expected) {
            throw new ConcordEmitException(
                "CONC039",
                $"Around-invoke injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' declares '{declared.Name}' but the matched call requires '{expected.Name}'.");
        }
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
        for (int i = 0; i < ordered.Count; i++) {
            Injection injection = ordered[i];
            if (injection.At is not InjectAt.Head && ControlHandleLowering.ReturnsControl(injection.InjectionMethod)) {
                throw new ConcordEmitException(
                    "CONC015",
                    $"Injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' returns Control; a Control return is only valid on a head injection.");
            }
        }

        MethodBody body = wrapperDefinition.Body;
        ModuleDefinition module = wrapperDefinition.Module;
        bool isVoid = returnType == typeof(void);

        bool hasAround = false;
        for (int i = 0; i < ordered.Count; i++) {
            if (ordered[i].At is InjectAt.Around) {
                hasAround = true;
                break;
            }
        }

        ProtocolLocals locals = DeclareLocals(body, module, returnType, isVoid, hasAround && !isVoid);

        List<Instruction> spine = new List<Instruction>(body.Instructions);
        Instruction afterSpine = Instruction.Create(OpCodes.Nop);

        List<Instruction> epilogue = BuildEpilogue(locals, isVoid);
        Instruction epilogueStart = epilogue[0];

        RewriteSpineReturns(spine, locals, afterSpine, isVoid, hasAround, body.ExceptionHandlers);

        if (!isVoid && !hasAround) {
            bool hasReturnSite = false;
            for (int i = 0; i < ordered.Count; i++) {
                if (ordered[i].At is InjectAt.Return) {
                    hasReturnSite = true;
                    break;
                }
            }

            if (hasReturnSite) {
                NormalizeReturnSites(spine, locals, afterSpine);
            }
        }

        Instruction guardStart = Instruction.Create(OpCodes.Ldloc, locals.Cancel);
        Instruction guardBranch = Instruction.Create(OpCodes.Brtrue, afterSpine);

        bool hasHead = false;
        List<List<Instruction>> headBodies = new List<List<Instruction>>();
        List<List<Instruction>> returnBodies = new List<List<Instruction>>();
        List<Instruction>? aroundBody = null;

        for (int i = ordered.Count - 1; i >= 0; i--) {
            Injection injection = ordered[i];

            if (injection.At is InjectAt.Head) {
                ProcessHeadInjection(injection, wrapperDefinition, target, locals, guardStart, isVoid, ref hasHead, headBodies);
                continue;
            }

            if (injection.At is InjectAt.Tail) {
                ProcessTailInjection(injection, wrapperDefinition, target, locals, epilogueStart, returnBodies);
                continue;
            }

            if (injection.At is InjectAt.Return returnSite) {
                ProcessReturnInjection(injection, returnSite, wrapperDefinition, target, locals, afterSpine, spine);
                continue;
            }

            if (injection.At is InjectAt.Invoke invoke) {
                ProcessInvokeInjection(injection, invoke, wrapperDefinition, target, locals, spine);
                continue;
            }

            if (injection.At is InjectAt.Constant constant) {
                ProcessConstantInjection(injection, constant, wrapperDefinition, target, spine);
                continue;
            }

            if (injection.At is InjectAt.Around) {
                ProcessAroundInjection(injection, wrapperDefinition, target, locals, epilogueStart, afterSpine, spine, ref aroundBody);
            }
        }

        List<Instruction> heads = ChainBodies(headBodies, guardStart);
        List<Instruction> returns = ChainBodies(returnBodies, epilogueStart);

        List<Instruction> assembled = new List<Instruction>();

        if (aroundBody is not null) {
            assembled.AddRange(aroundBody);
            assembled.AddRange(epilogue);
        } else {
            assembled.AddRange(heads);
            if (hasHead) {
                assembled.Add(guardStart);
                assembled.Add(guardBranch);
            }

            assembled.AddRange(spine);
            assembled.Add(afterSpine);
            assembled.AddRange(returns);
            assembled.AddRange(epilogue);
        }

        body.Instructions.Clear();
        foreach (Instruction instruction in assembled) {
            body.Instructions.Add(instruction);
        }
    }

    private static List<Instruction> ChainBodies(List<List<Instruction>> bodies, Instruction sharedTarget) {
        if (bodies.Count == 0) {
            return [];
        }

        for (int i = 0; i < bodies.Count - 1; i++) {
            List<Instruction> body = bodies[i];
            Instruction nextStart = bodies[i + 1][0];
            foreach (Instruction instruction in body) {
                if (ReferenceEquals(instruction.Operand, sharedTarget)) {
                    instruction.Operand = nextStart;
                }
            }
        }

        List<Instruction> chained = new List<Instruction>();
        foreach (List<Instruction> body in bodies) {
            chained.AddRange(body);
        }

        return chained;
    }

    private static void ProcessHeadInjection(
        Injection injection,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        Instruction guardStart,
        bool isVoid,
        ref bool hasHead,
        List<List<Instruction>> heads) {
        hasHead = true;
        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
        GuardCancelWithoutReturn(
            injectionMethodDefinition.Definition.Body,
            target,
            isVoid,
            ControlHandleLowering.ReturnsControl(injection.InjectionMethod));
        heads.Add(
            BodyCopier.CopyInjection(
                injectionMethodDefinition.Definition,
                wrapperDefinition,
                target,
                injection.InjectionMethod,
                injectedMembers,
                locals,
                guardStart));
    }

    private static void ProcessTailInjection(
        Injection injection,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        Instruction epilogueStart,
        List<List<Instruction>> returns) {
        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
        returns.Add(
            BodyCopier.CopyInjection(
                injectionMethodDefinition.Definition,
                wrapperDefinition,
                target,
                injection.InjectionMethod,
                injectedMembers,
                locals,
                epilogueStart));
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
                injectionMethodDefinition.Definition,
                wrapperDefinition,
                target,
                injection.InjectionMethod,
                injectedMembers,
                locals,
                exit);
            int exitIndex = spine.IndexOf(exit);
            spine.InsertRange(exitIndex, siteBody);
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

        List<Instruction> allSites = ControlHandleLowering.FindInvokeCallSites(spine, invoke.DeclaringType, effectiveName, invoke.ParameterTypes);
        if (allSites.Count == 0) {
            throw new ConcordEmitException(
                "CONC031",
                $"Injection on '{target.DeclaringType?.Name}.{target.Name}' targets call site '{invoke.DeclaringType.Name}.{invoke.Method}' which does not occur in the method body.");
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
            List<Instruction> invokeBody = BodyCopier.CopyInjection(
                injectionMethodDefinition.Definition,
                wrapperDefinition,
                target,
                injection.InjectionMethod,
                injectedMembers,
                locals,
                site);
            int siteIndex = spine.IndexOf(site);
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
                    "CONC039",
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
                    "CONC039",
                    $"Argument injection on '{target.DeclaringType?.Name}.{target.Name}' matches more than one '{valueType.Name}' argument; pass arg: to select one.");
            }

            found = i;
        }

        if (found < 0) {
            throw new ConcordEmitException(
                "CONC039",
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
                "CONC039",
                $"Value injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{target.DeclaringType?.Name}.{target.Name}' must declare exactly one parameter, got {parameters.Length}.");
        }

        Type returnType = injectionMethod is MethodInfo methodInfo ? methodInfo.ReturnType : typeof(void);
        if (parameters[0].ParameterType != valueType || returnType != valueType) {
            throw new ConcordEmitException(
                "CONC039",
                $"Value injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{target.DeclaringType?.Name}.{target.Name}' must be shaped '{valueType.Name} M({valueType.Name} original)'.");
        }
    }

    private static void ProcessAroundInjection(
        Injection injection,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        Instruction epilogueStart,
        Instruction afterSpine,
        List<Instruction> spine,
        ref List<Instruction>? aroundBody) {
        if (aroundBody is not null) {
            throw new ConcordEmitException(
                "CONC051",
                $"Multiple Around injections on '{target.DeclaringType?.Name}.{target.Name}' are not supported; only one Around injection per target is allowed.");
        }

        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
        aroundBody = BodyCopier.CopyInjection(
            injectionMethodDefinition.Definition,
            wrapperDefinition,
            target,
            injection.InjectionMethod,
            injectedMembers,
            locals,
            epilogueStart,
            spine);
        RetargetAroundSpineBranches(aroundBody, spine, afterSpine, epilogueStart, locals);
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
            injectionMethodDefinition,
            wrapperDefinition,
            target,
            injectionMethod,
            injectedMembers,
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
                $"Injection on '{target.DeclaringType?.Name}.{target.Name}' targets occurrence {by} of call site '{invoke.DeclaringType.Name}.{invoke.Method}', but only {allSites.Count} occurrence(s) exist in the method body.");
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
        bool needsSpliceValue) {
        VariableDefinition cancel = new VariableDefinition(module.ImportReference(typeof(bool)));
        body.Variables.Add(cancel);
        body.InitLocals = true;

        if (isVoid) {
            return new ProtocolLocals(cancel, null, null);
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

        return new ProtocolLocals(cancel, hasReturn, returnValue, spliceValue);
    }

    private static List<Instruction> BuildEpilogue(ProtocolLocals locals, bool isVoid) {
        if (isVoid) {
            return new List<Instruction> { Instruction.Create(OpCodes.Ret) };
        }

        return new List<Instruction> { Instruction.Create(OpCodes.Ldloc, locals.ReturnValue!), Instruction.Create(OpCodes.Ret) };
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

    private static void NormalizeReturnSites(List<Instruction> spine, ProtocolLocals locals, Instruction afterSpine) {
        Instruction? exitStore = FindExitStore(spine, locals, afterSpine);
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
            if ((instruction.OpCode == OpCodes.Br || instruction.OpCode == OpCodes.Br_S)
                && ReferenceEquals(instruction.Operand, sharedLoad)) {
                branchSites.Add(instruction);
            }
        }

        if (branchSites.Count == 0) {
            return;
        }

        foreach (Instruction branch in branchSites) {
            int branchIndex = spine.IndexOf(branch);
            spine[branchIndex] = CloneLoadLocal(sharedLoad);
            spine.Insert(branchIndex + 1, Instruction.Create(OpCodes.Stloc, locals.ReturnValue!));
            spine.Insert(branchIndex + 2, Instruction.Create(OpCodes.Br, afterSpine));
        }

        int deadTailIndex = spine.IndexOf(sharedLoad);
        spine.RemoveAt(deadTailIndex + 2);
        spine.RemoveAt(deadTailIndex + 1);
        spine.RemoveAt(deadTailIndex);
    }

    private static Instruction? FindExitStore(List<Instruction> spine, ProtocolLocals locals, Instruction afterSpine) {
        for (int i = 0; i < spine.Count - 1; i++) {
            Instruction store = spine[i];
            Instruction branch = spine[i + 1];
            if (store.OpCode == OpCodes.Stloc && ReferenceEquals(store.Operand, locals.ReturnValue)
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
            || opCode == OpCodes.Leave_S;
    }

    private static bool IsInsideProtectedRegion(Instruction instruction, IList<ExceptionHandler> handlers) {
        foreach (ExceptionHandler handler in handlers) { // NOSONAR project forbids LINQ in loops (perf/determinism); for-loop is intentional
            if (SpansInstruction(handler.TryStart, handler.TryEnd, instruction) ||
                SpansInstruction(handler.HandlerStart, handler.HandlerEnd, instruction) ||
                SpansInstruction(handler.FilterStart, handler.HandlerStart, instruction)) {
                return true;
            }
        }

        return false;
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
        List<Instruction> spine,
        Instruction afterSpine,
        Instruction epilogueStart,
        ProtocolLocals locals) {
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

        foreach (Instruction instruction in aroundBody) {
            if (instruction.Operand == afterSpine) {
                instruction.Operand = spliceJoin;
            }
        }
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
