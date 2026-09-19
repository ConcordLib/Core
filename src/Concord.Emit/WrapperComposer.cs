using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    // Mirrors InjectedMemberAnalyzer.LocalPositionHelp. The two lists are deliberate twins: the
    // analyzer cannot reference Concord.Emit, so changing one means changing the other.
    // PublicSurfaceTests.PositionHelpMatchesTheAnalyzersTwin reads both by reflection and fails on drift.
    private const string LocalPositionHelp = "[Local] is supported at At.Return, At.Tail, At.Finally, At.Local, and the At.Head and " +
                                             "At.Tail shifts of At.Invoke and At.NewObj.";

    // Mirrors InjectedMemberAnalyzer.LocalWriteHelp. The two lists are deliberate twins: the
    // analyzer cannot reference Concord.Emit, so changing one means changing the other.
    private const string LocalWriteHelp = "LocalHandle<T> is supported at At.Local and the At.Head and At.Tail shifts of " +
                                          "At.Invoke and At.NewObj. Use a plain [Local] parameter to read a local at the other positions.";

    private static readonly Dictionary<MethodBase, bool> SharedBodyCache = new Dictionary<MethodBase, bool>();
    private static readonly object SharedBodyGate = new object();

    /// <summary>
    ///     Whether this runtime can host the shared generic receiver guard. .NET Framework refuses to
    ///     prepare the canonical instantiation, so shared reference-type instantiations stay rejected there.
    /// </summary>
    public static bool SharedGenericGuardSupported { get; } = !RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal);

    /// <summary>
    ///     Creates a wrapper method for a target and a copy of the original body.
    /// </summary>
    /// <param name="target">
    ///     The exact method to compose onto. Callers that mean to patch an async or iterator target's
    ///     generated <c>MoveNext</c> resolve it with <see cref="ResolveBodyTarget" /> first.
    /// </param>
    /// <param name="ordered">The injections to compose, ordered by their caller.</param>
    /// <returns>The generated wrapper method and original body copy.</returns>
    public static ComposeResult Compose(MethodBase target, IReadOnlyList<Injection> ordered) {
        List<RejectedInjection> rejected = new List<RejectedInjection>();
        IReadOnlyList<Injection> live = ordered;
        while (true) {
            try {
                return ComposeOnce(target, live, rejected);
            } catch (ConcordEmitException failure) when (CanEvict(failure, live)) {
                live = Evict(live, failure, rejected);
            } catch (ConcordEmitException failure) when (rejected.Count > 0) {
                throw WithEarlierEvictions(failure, rejected);
            }
        }
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
        List<RejectedInjection> rejected = new List<RejectedInjection>();
        IReadOnlyList<Injection> live = ordered;
        while (true) {
            try {
                return ComposeDumpOnce(target, live);
            } catch (ConcordEmitException failure) when (CanEvict(failure, live)) {
                live = Evict(live, failure, rejected);
            } catch (ConcordEmitException failure) when (rejected.Count > 0) {
                throw WithEarlierEvictions(failure, rejected);
            }
        }
    }

    /// <summary>
    ///     Creates a transpiler context for use with <see cref="TransformStream(MethodBase, IReadOnlyList{CodeInstruction}, IReadOnlyList{Injection}, ITranspilerContext)" />, in the virgin
    ///     local-numbering state a supplied stream expects: no locals declared yet, so a caller's
    ///     first <see cref="ITranspilerContext.DeclareLocal" /> call returns index 0, the second
    ///     returns index 1, and so on.
    /// </summary>
    /// <param name="target">
    ///     The method the supplied stream's shape describes, already resolved by the caller through
    ///     <see cref="ResolveBodyTarget" /> when the stream is a state machine's <c>MoveNext</c>.
    /// </param>
    /// <returns>A fresh transpiler context, not yet used to read or write any body.</returns>
    public static ITranspilerContext CreateStreamContext(MethodBase target) {
        return new TranspilerContext(target);
    }

    /// <summary>
    ///     Composes ordered injections onto a caller-supplied instruction stream instead of IL read from
    ///     <paramref name="target" /> itself. This is the seam a coexistence bridge - one that hands
    ///     Concord another patching library's own transpiler stream - uses to compose Concord's
    ///     injections onto it and hand the composed stream back.
    /// </summary>
    /// <param name="target">
    ///     The method that defines the composed body's shape (return type and parameters), already
    ///     resolved by the caller through <see cref="ResolveBodyTarget" /> where that applies.
    /// </param>
    /// <param name="source">The instruction stream to compose the injections onto.</param>
    /// <param name="ordered">The injections to compose, ordered by their caller.</param>
    /// <param name="context">
    ///     The context <paramref name="source" /> was produced against, obtained from
    ///     <see cref="CreateStreamContext" />. Locals a caller declared on it before this call land at
    ///     the indices they were declared in.
    /// </param>
    /// <returns>The composed instruction stream.</returns>
    /// <remarks>
    ///     This overload discards the eviction report. A bridge that wants to tell an author why its
    ///     <c>[Local]</c> stopped resolving must call the overload that hands back
    ///     <see cref="RejectedInjection" />s, because nothing else reports them.
    /// </remarks>
    /// <exception cref="ConcordEmitException">
    ///     Thrown with code <c>CONC116</c> when <paramref name="context" /> was not obtained from
    ///     <see cref="CreateStreamContext" />.
    /// </exception>
    public static List<CodeInstruction> TransformStream(
        MethodBase target,
        IReadOnlyList<CodeInstruction> source,
        IReadOnlyList<Injection> ordered,
        ITranspilerContext context) {
        return TransformStream(target, source, ordered, context, out _);
    }

    /// <summary>
    ///     Composes ordered injections onto a caller-supplied instruction stream, and reports every
    ///     injection that was evicted to get there. A bridge should log <paramref name="rejected" />:
    ///     it is the only place the owning mod learns that its <c>[Local]</c> stopped resolving.
    /// </summary>
    /// <param name="target">The method that defines the composed body's shape.</param>
    /// <param name="source">The instruction stream to compose the injections onto.</param>
    /// <param name="ordered">The injections to compose, ordered by their caller.</param>
    /// <param name="context">The context <paramref name="source" /> was produced against.</param>
    /// <param name="rejected">The injections dropped during composition. Empty on a clean compose.</param>
    /// <returns>The composed instruction stream.</returns>
    public static List<CodeInstruction> TransformStream(
        MethodBase target,
        IReadOnlyList<CodeInstruction> source,
        IReadOnlyList<Injection> ordered,
        ITranspilerContext context,
        out IReadOnlyList<RejectedInjection> rejected) {
        List<RejectedInjection> dropped = new List<RejectedInjection>();
        IReadOnlyList<Injection> live = ordered;
        while (true) {
            try {
                List<CodeInstruction> composed = TransformStreamOnce(target, source, live, context);
                rejected = dropped;
                return composed;
            } catch (ConcordEmitException failure) when (CanEvict(failure, live)) {
                live = Evict(live, failure, dropped);
            } catch (ConcordEmitException failure) when (dropped.Count > 0) {
                throw WithEarlierEvictions(failure, dropped);
            }
        }
    }

    /// <summary>
    ///     Resolves async and iterator entry methods to their generated state-machine <c>MoveNext</c> method.
    /// </summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>The state-machine <c>MoveNext</c> method when present. Otherwise <paramref name="target" />.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011", Justification = "Concord reaches the private state-machine MoveNext by design. Validated at resolve time.")]
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
    ///     Picks the method an injection actually composes onto, given the body it asked for.
    /// </summary>
    /// <param name="target">The method named by the declaration.</param>
    /// <param name="body">The body the injection selected.</param>
    /// <returns>
    ///     The state-machine <c>MoveNext</c> when <paramref name="body" /> is
    ///     <see cref="PatchBody.StateMachine" /> and <paramref name="target" /> is an async or iterator
    ///     method. Otherwise <paramref name="target" /> unchanged.
    /// </returns>
    public static MethodBase ResolveBodyTarget(MethodBase target, PatchBody body) {
        return body == PatchBody.StateMachine ? ResolveStateMachineTarget(target) : target;
    }

    /// <summary>
    ///     Rejects an injection whose declared shape cannot work against the body it selected.
    /// </summary>
    /// <param name="declared">The method named by the declaration.</param>
    /// <param name="resolved">The method composition will actually run against.</param>
    /// <param name="injection">The injection to check.</param>
    /// <exception cref="ConcordEmitException">
    ///     Thrown with <c>CONC122</c> when a <c>ControlHandle&lt;T&gt;</c> is shaped against the declared
    ///     return type but the injection selected <see cref="PatchBody.StateMachine" />, and with
    ///     <c>CONC123</c> when a position that needs the body as written selected
    ///     <see cref="PatchBody.Declared" /> on an async or iterator target, where only the
    ///     compiler-generated stub exists.
    /// </exception>
    public static void ValidateBodySelection(MethodBase declared, MethodBase resolved, Injection injection) {
        if (ReadStateMachineType(declared) is null) {
            return;
        }

        if (injection.Body == PatchBody.Declared) {
            RejectStubOnlyPosition(declared, injection);
            return;
        }

        if (injection.At is not (InjectAt.Head or InjectAt.Return or InjectAt.Tail)) {
            return;
        }

        int handleIndex = ControlHandleLowering.FindControlHandleArgIndex(injection.InjectionMethod);
        if (handleIndex < 0) {
            return;
        }

        ParameterInfo[] parameters = injection.InjectionMethod.GetParameters();
        int offset = injection.InjectionMethod.IsStatic ? 0 : 1;
        Type handleType = parameters[handleIndex - offset].ParameterType;
        Type handled = handleType.IsGenericType ? handleType.GetGenericArguments()[0] : typeof(void);

        Type resolvedReturn = ResolveReturnType(resolved);
        if (handled == resolvedReturn) {
            return;
        }

        throw new ConcordEmitException(
            "CONC122",
            $"Injection '{injection.InjectionMethod.Name}' selected PatchBody.StateMachine on async or iterator method " +
            $"'{declared.DeclaringType?.Name}.{declared.Name}', so it composes onto " +
            $"'{resolved.DeclaringType?.Name}.{resolved.Name}' which returns '{resolvedReturn.Name}'. The injection declares a " +
            $"handle over '{handled.Name}', which cannot be spliced into that body. Shape the handle against '{resolvedReturn.Name}', " +
            $"or drop to PatchBody.Declared to read the '{ResolveReturnType(declared).Name}' the target hands back.");
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
        RejectSharedGenericInstantiation(target, null);
    }

    /// <summary>
    ///     Rejects a target that is a reference-type generic instantiation, unless the composition
    ///     qualifies for a receiver guard. A guarded composition emits a runtime
    ///     <c>isinst</c> check against the requested instantiation in front of every injection body, so
    ///     calls that arrive on a different reference-type instantiation of the same shared body run the
    ///     copied original instead of the injections.
    /// </summary>
    /// <param name="target">The method a detour is about to be installed for.</param>
    /// <param name="ordered">The injections being composed, or <see langword="null" /> when unknown.</param>
    /// <exception cref="ConcordEmitException">Thrown with <c>CONC061</c> when the target is an unguardable reference-type instantiation.</exception>
    public static void RejectSharedGenericInstantiation(MethodBase target, IReadOnlyList<Injection>? ordered) {
        if (CanGuardSharedInstantiation(target, ordered)) {
            return;
        }

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

    /// <summary>
    ///     Reports whether a target's compiled body is shared with the other reference-type instantiations
    ///     of its declaring type, and a copy of that body taken at this instantiation is valid for all of
    ///     them. Detour registries key every such target to one canonical entry, because the runtime gives
    ///     them one native body between them.
    /// </summary>
    /// <param name="target">The method a detour is about to be installed for.</param>
    /// <returns><see langword="true" /> when the target shares a guardable body.</returns>
    public static bool SharesGenericBody(MethodBase target) {
        if (!SharedGenericGuardSupported) {
            return false;
        }

        if (target.IsStatic || target.IsConstructor) {
            return false;
        }

        if (target is MethodInfo { IsGenericMethod: true }) {
            return false;
        }

        if (target.DeclaringType is not { IsConstructedGenericType: true, IsValueType: false }) {
            return false;
        }

        bool anyReferenceArgument = false;
        foreach (Type argument in EnumerateGenericArguments(target)) {
            if (!argument.IsGenericParameter && !argument.IsValueType) {
                anyReferenceArgument = true;
                break;
            }
        }

        if (!anyReferenceArgument) {
            return false;
        }

        lock (SharedBodyGate) {
            if (SharedBodyCache.TryGetValue(target, out bool cached)) {
                return cached;
            }
        }

        bool contextFree = IsSharedBodyGenericContextFree(target);
        lock (SharedBodyGate) {
            SharedBodyCache[target] = contextFree;
        }

        return contextFree;
    }

    /// <summary>
    ///     Records which instantiation each injection was requested for, so composition can guard its body
    ///     with a receiver check. Injections that already carry a tag keep it, and a target whose body is not
    ///     shared is returned untouched.
    /// </summary>
    /// <param name="target">The instantiation the injections were requested for.</param>
    /// <param name="added">The injections to tag.</param>
    /// <returns>The tagged injections, or <paramref name="added" /> when no tag applies.</returns>
    public static IReadOnlyList<Injection> TagRequestedInstantiation(MethodBase target, IReadOnlyList<Injection> added) {
        if (!SharesGenericBody(target)) {
            return added;
        }

        Injection[] tagged = new Injection[added.Count];
        for (int i = 0; i < added.Count; i++) {
            tagged[i] = added[i].RequestedInstantiation is null
                ? added[i] with { RequestedInstantiation = target.DeclaringType }
                : added[i];
        }

        return tagged;
    }

    internal static bool CanGuardSharedInstantiation(MethodBase target, IReadOnlyList<Injection>? ordered) {
        if (ordered is null || ordered.Count == 0) {
            return false;
        }

        foreach (Injection injection in ordered) {
            if (injection.At is not (InjectAt.Head or InjectAt.Tail)) {
                return false;
            }
        }

        return SharesGenericBody(target);
    }

    internal static bool IsSharedBodyGenericContextFree(MethodBase target) {
        MethodBase? probe = BuildProbeInstantiation(target);
        if (probe is null) {
            return false;
        }

        try {
            using DynamicMethodDefinition requested = new DynamicMethodDefinition(target);
            using DynamicMethodDefinition other = new DynamicMethodDefinition(probe);
            return SameInstructions(requested.Definition, other.Definition);
        } catch (Exception) {
            return false;
        }
    }

    internal static void ValidateComposition(MethodBase target, IReadOnlyList<Injection> ordered) {
        RejectMisplacedCaptures(ordered, target);
        RejectMisplacedSlices(ordered, target);
        RejectMisplacedLocals(ordered, target);
        RejectMisplacedLocalWrites(ordered, target);

        if (HasWholeMethodAround(ordered)) {
            ValidateWholeMethodAroundEligible(target);
            RejectCallSiteInjectionsWithWholeMethodAround(ordered, target);
        }
    }

    internal static void AssembleInto(MethodDefinition wrapperDefinition, MethodBase target, IReadOnlyList<Injection> ordered, Type returnType, int rawLocalCount) {
        ValidateComposition(target, ordered);
        Assemble(wrapperDefinition, target, ordered, returnType, rawLocalCount);
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
        for (int i = 0; i < handlers.Count; i++) {
            ExceptionHandler handler = handlers[i];
            if (SpansInstruction(handler.TryStart, handler.TryEnd, instruction) ||
                SpansInstruction(handler.HandlerStart, handler.HandlerEnd, instruction) ||
                SpansInstruction(handler.FilterStart, handler.HandlerStart, instruction)) {
                return true;
            }
        }

        return false;
    }

    internal static Type ResolveReturnType(MethodBase target) {
        return target is MethodInfo method ? method.ReturnType : typeof(void);
    }

    internal static Type[] ResolveParameterTypes(MethodBase target) {
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

    internal static Type ThisParameterType(MethodBase target) {
        Type declaring = target.DeclaringType!;
        return declaring.IsValueType ? declaring.MakeByRefType() : declaring;
    }

    internal static string WrapperName(MethodBase target) {
        return $"{target.DeclaringType?.Name}.{target.Name}‹concord›";
    }

    internal static void PartitionTranspilers(
        IReadOnlyList<Injection> ordered,
        out List<Injection> preTranspilers,
        out List<Injection> finalTranspilers,
        out List<Injection> declarative) {
        preTranspilers = new List<Injection>();
        finalTranspilers = new List<Injection>();
        declarative = new List<Injection>();

        for (int i = 0; i < ordered.Count; i++) {
            Injection injection = ordered[i];
            if (injection.At is InjectAt.Transpiler { Final: false }) {
                preTranspilers.Add(injection);
            } else if (injection.At is InjectAt.Transpiler { Final: true }) {
                finalTranspilers.Add(injection);
            } else {
                declarative.Add(injection);
            }
        }
    }

    internal static void RunTranspilers(MethodDefinition wrapperDefinition, MethodBase resolved, IReadOnlyList<Injection> transpilers) {
        if (transpilers.Count == 0) {
            return;
        }

        TranspilerContext context = new TranspilerContext(resolved);
        List<CodeInstruction> instructions = CecilCodeConverter.ToInstructions(wrapperDefinition, context);

        int baseline = CountGetExecutingAssemblyCalls(instructions);

        for (int i = 0; i < transpilers.Count; i++) {
            IEnumerable<CodeInstruction> produced = TranspilerInvoker.Invoke(transpilers[i].InjectionMethod, instructions, context);
            instructions = produced as List<CodeInstruction> ?? new List<CodeInstruction>(produced);

            int emitted = CountGetExecutingAssemblyCalls(instructions);
            if (emitted > baseline) {
                MethodBase offender = transpilers[i].InjectionMethod;
                throw new ConcordEmitException(
                    "CONC143",
                    $"Transpiler '{offender.DeclaringType?.Name}.{offender.Name}' on '{resolved.DeclaringType?.Name}.{resolved.Name}' emitted a call to Assembly.GetExecutingAssembly. " +
                    "Concord cannot tell which assembly that call is meant to observe, and under a Harmony bridge Harmony rewrites it to the target's assembly. " +
                    "Emit ldtoken of the type you mean followed by Type.GetTypeFromHandle and Type.get_Assembly instead.");
            }

            baseline = emitted;
        }

        try {
            CecilCodeConverter.WriteBack(wrapperDefinition, instructions, context);
        } catch (ConcordEmitException ex) when (ex.Code is "CONC118" or "CONC119") {
            string names = string.Join(", ", transpilers.Select(t => $"{t.InjectionMethod.DeclaringType?.Name}.{t.InjectionMethod.Name}"));
            throw new ConcordEmitException(
                ex.Code,
                $"Transpiler(s) '{names}' on '{resolved.DeclaringType?.Name}.{resolved.Name}' produced instructions Concord cannot write back: {ex.Message}");
        }
    }

    private static int CountGetExecutingAssemblyCalls(List<CodeInstruction> instructions) {
        int count = 0;
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.opcode == System.Reflection.Emit.OpCodes.Call
                && instruction.operand is MethodBase method
                && method.Name == "GetExecutingAssembly"
                && method.DeclaringType == typeof(System.Reflection.Assembly)) {
                count++;
            }
        }

        return count;
    }

    private static TranspilerContext RequireConcreteContext(ITranspilerContext context) {
        if (context is TranspilerContext concrete) {
            return concrete;
        }

        throw new ConcordEmitException(
            "CONC116",
            "The supplied ITranspilerContext was not obtained from WrapperComposer.CreateStreamContext.");
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
    ///     Rejects <see cref="CaptureAttribute" /> anywhere there is no matched call to capture an argument
    ///     from: any position other than <see cref="InjectAt.Invoke" /> and <see cref="InjectAt.NewObj" />, and
    ///     any shift of those other than <see cref="At.Head" /> and <see cref="At.Tail" />.
    /// </summary>
    /// <param name="ordered">The full injection list being composed for <paramref name="target" />.</param>
    /// <param name="target">The original method being patched, used for the diagnostic message.</param>
    private static void RejectMisplacedCaptures(IReadOnlyList<Injection> ordered, MethodBase target) {
        for (int i = 0; i < ordered.Count; i++) {
            Injection injection = ordered[i];
            if (!DeclaresCapture(injection.InjectionMethod)) {
                continue;
            }

            At? shift = injection.At switch {
                InjectAt.Invoke invoke => invoke.Shift,
                InjectAt.NewObj newObj => newObj.Shift,
                _ => null,
            };

            if (shift is At.Head or At.Tail) {
                continue;
            }

            string reason = shift is null
                ? $"is a whole-method position ({PositionName(injection.At)}), which matches no call"
                : $"uses the shift At.{shift}, which does not name a point where the call's arguments are still on the stack";

            throw new ConcordEmitException(
                "CONC128",
                $"Injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' on " +
                $"'{target.DeclaringType?.Name}.{target.Name}' declares a [Capture] parameter, but its position {reason}. " +
                "[Capture] binds an argument of a call matched inside the target body, so it needs an Invoke or NewObj " +
                "injection shifted to At.Head or At.Tail. At.Around and At.Argument already receive the call's arguments.");
        }
    }

    /// <summary>
    ///     Rejects <see cref="SliceAttribute" /> on any position that matches no call site, since a range
    ///     only bounds the search that <see cref="InjectAt.Invoke" />, <see cref="InjectAt.NewObj" /> and
    ///     <see cref="InjectAt.Local" /> perform.
    /// </summary>
    /// <param name="ordered">The full injection list being composed for <paramref name="target" />.</param>
    /// <param name="target">The original method being patched, used for the diagnostic message.</param>
    private static void RejectMisplacedSlices(IReadOnlyList<Injection> ordered, MethodBase target) {
        for (int i = 0; i < ordered.Count; i++) {
            Injection injection = ordered[i];
            if (injection.At is InjectAt.Invoke or InjectAt.NewObj or InjectAt.Local) {
                continue;
            }

            if (injection.InjectionMethod.GetCustomAttribute<SliceAttribute>() is null) {
                continue;
            }

            throw new ConcordEmitException(
                "CONC134",
                $"Injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' on " +
                $"'{target.DeclaringType?.Name}.{target.Name}' carries [Slice] at position '{PositionName(injection.At)}'. " +
                "[Slice] applies to invoke and construction positions only.");
        }
    }

    /// <summary>
    ///     Rejects <see cref="LocalAttribute" /> on any position that carries no local binding.
    ///     <see cref="InjectAt.Head" /> runs before the target assigns anything, and a whole-method
    ///     <see cref="InjectAt.Around" /> never executes the target's slots at all, so each gets its own code.
    ///     Every other unsupported position lowers through a copier that never builds a binding map, which
    ///     would read the wrong slot or a default instead of failing.
    /// </summary>
    /// <param name="ordered">The full injection list being composed for <paramref name="target" />.</param>
    /// <param name="target">The original method being patched, used for the diagnostic message.</param>
    private static void RejectMisplacedLocals(IReadOnlyList<Injection> ordered, MethodBase target) {
        for (int i = 0; i < ordered.Count; i++) {
            Injection injection = ordered[i];
            if (!DeclaresLocal(injection.InjectionMethod) || SupportsLocalBinding(injection.At)) {
                continue;
            }

            string who =
                $"Injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' on " +
                $"'{target.DeclaringType?.Name}.{target.Name}' declares a [Local] or LocalHandle<T> parameter";

            // Mirrors InjectedMemberAnalyzer.LocalOnWholeMethodAroundRule's message and description.
            // The two are deliberate twins: the analyzer is netstandard2.0 and reads At values from
            // the user's compilation, this switches an InjectAt graph, so neither can call the other.
            if (injection.At is InjectAt.Around) {
                throw new ConcordEmitException(
                    "CONC160",
                    $"{who} at At.Around. The target's locals only exist inside the body copies this Around splices in at each " +
                    "original.Invoke, so the Around method's own instructions never see one, and with more than one Invoke site " +
                    "there is no single copy to bind. Move the parameter to an At.Return or At.Tail injection on the same target.");
            }

            throw injection.At is InjectAt.Head
                ? new ConcordEmitException(
                    "CONC146",
                    $"{who} at At.Head, which runs before the target body assigns any local. {LocalPositionHelp}")
                : new ConcordEmitException(
                    "CONC158",
                    $"{who} at '{PositionName(injection.At)}', which binds no local. {LocalPositionHelp}");
        }
    }

    /// <summary>
    ///     Rejects <see cref="LocalHandle{T}" /> at a position that can read a local but not usefully write
    ///     one. Write-legal positions are a strict subset of <see cref="SupportsLocalBinding" />: by
    ///     <see cref="InjectAt.Return" /> and <see cref="InjectAt.Tail" /> the target body has finished with
    ///     its locals, so the write is dead, and <see cref="InjectAt.Finally" /> is the same plus unreachable
    ///     on the throwing path. Every other unsupported position is already rejected by
    ///     <see cref="RejectMisplacedLocals" />.
    /// </summary>
    /// <param name="ordered">The full injection list being composed for <paramref name="target" />.</param>
    /// <param name="target">The original method being patched, used for the diagnostic message.</param>
    private static void RejectMisplacedLocalWrites(IReadOnlyList<Injection> ordered, MethodBase target) {
        for (int i = 0; i < ordered.Count; i++) {
            Injection injection = ordered[i];
            if (!LocalHandleLowering.DeclaresLocalHandle(injection.InjectionMethod) || SupportsLocalWrite(injection.At)) {
                continue;
            }

            throw new ConcordEmitException(
                "CONC156",
                $"Injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' on " +
                $"'{target.DeclaringType?.Name}.{target.Name}' declares a LocalHandle<T> parameter at " +
                $"'{PositionName(injection.At)}', where the target body is done with its locals, so the write would " +
                "never be read back. " + LocalWriteHelp);
        }
    }

    // Twin of InjectedMemberAnalyzer.SupportsLocalWrite. Keep both in step; the analyzer targets
    // netstandard2.0 with no reference to this assembly, so it cannot call this one.
    private static bool SupportsLocalWrite(InjectAt at) {
        return at switch {
            InjectAt.Local => true,
            InjectAt.Invoke invoke => invoke.Shift is At.Head or At.Tail,
            InjectAt.NewObj newObj => newObj.Shift is At.Head or At.Tail,
            _ => false,
        };
    }

    // Twin of InjectedMemberAnalyzer.SupportsLocalBinding, which compares raw At ordinals because
    // the analyzer cannot reference this assembly. Keep both in step.
    private static bool SupportsLocalBinding(InjectAt at) {
        return at switch {
            InjectAt.Return or InjectAt.Tail or InjectAt.Finally or InjectAt.Local => true,
            InjectAt.Invoke invoke => invoke.Shift is At.Head or At.Tail,
            InjectAt.NewObj newObj => newObj.Shift is At.Head or At.Tail,
            _ => false,
        };
    }

    private static bool DeclaresLocal(MethodBase injectionMethod) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].GetCustomAttribute<LocalAttribute>() is not null
                || LocalHandleLowering.IsLocalHandleType(parameters[i].ParameterType)) {
                return true;
            }
        }

        return false;
    }

    private static bool DeclaresCapture(MethodBase injectionMethod) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            if (parameters[i].GetCustomAttribute<CaptureAttribute>() is not null) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Rejects call-site injection positions (<see cref="InjectAt.Invoke" /> and
    ///     <see cref="InjectAt.NewObj" /> in any shift, including their <c>At.Argument</c> variant, and
    ///     <see cref="InjectAt.Constant" />) when combined with a whole-method <see cref="InjectAt.Around" />
    ///     on the same target.
    /// </summary>
    /// <param name="ordered">The full injection list being composed for <paramref name="target" />.</param>
    /// <param name="target">The original method being patched, used for the diagnostic message.</param>
    private static void RejectCallSiteInjectionsWithWholeMethodAround(IReadOnlyList<Injection> ordered, MethodBase target) {
        for (int i = 0; i < ordered.Count; i++) {
            InjectAt at = ordered[i].At;
            if (at is InjectAt.Invoke or InjectAt.NewObj or InjectAt.Constant or InjectAt.Local) {
                throw new ConcordEmitException(
                    "CONC115",
                    $"Whole-method Around on '{target.DeclaringType?.Name}.{target.Name}' cannot be combined with call-site " +
                    "(Invoke/NewObj/Argument/Constant/Local) injections on the same target. Call-site positions mutate the pre-Around spine, " +
                    "which does not compose with the per-copy splicing a whole-method Around performs.");
            }
        }
    }

    // The stub the compiler leaves behind for an async or iterator method builds the state machine
    // and returns it. Head, Return and Tail still mean something there - they run once per call, and
    // Return hands over the Task or IEnumerable. Every other position needs statements, branches or
    // call sites that only exist in MoveNext, so selecting the stub for one of those silently
    // matches nothing.
    private static void RejectStubOnlyPosition(MethodBase declared, Injection injection) {
        if (injection.At is InjectAt.Head or InjectAt.Return or InjectAt.Tail) {
            return;
        }

        throw new ConcordEmitException(
            "CONC123",
            $"Injection '{injection.InjectionMethod.Name}' targets '{declared.DeclaringType?.Name}.{declared.Name}' at {PositionName(injection.At)}, " +
            "but that method is async or an iterator, so the body it declares is compiled into a generated MoveNext. The method itself only " +
            "builds and returns the state machine. Set Body = PatchBody.StateMachine on the injection to reach the body as written.");
    }

    // An Invoke or NewObj shift is spelled with its owning position in front of it, because a bare
    // "At.Around" would read the same for a call-site Around shift and a whole-method Around, and
    // several diagnostics name both.
    private static string PositionName(InjectAt at) {
        return at switch {
            InjectAt.Head => "At.Head",
            InjectAt.Return => "At.Return",
            InjectAt.Tail => "At.Tail",
            InjectAt.Around => "At.Around",
            InjectAt.Finally => "At.Finally",
            InjectAt.Constant => "At.Constant",
            InjectAt.Invoke invoke => $"At.Invoke/At.{invoke.Shift}",
            InjectAt.NewObj newObj => $"At.NewObj/At.{newObj.Shift}",
            InjectAt.Local => "At.Local",
            InjectAt.Transpiler { Final: true } => "At.TranspilerFinal",
            InjectAt.Transpiler => "At.Transpiler",
            _ => at.GetType().Name,
        };
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
                "State-machine methods are not supported by the Operation handle. Patch at Head instead.");
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

    private static MethodBase? BuildProbeInstantiation(MethodBase target) {
        Type? declaringType = target.DeclaringType;
        if (declaringType is not { IsConstructedGenericType: true }) {
            return null;
        }

        Type[] arguments = declaringType.GetGenericArguments();
        Type[] substituted = new Type[arguments.Length];
        for (int i = 0; i < arguments.Length; i++) {
            Type argument = arguments[i];
            if (argument.IsValueType) {
                substituted[i] = argument;
                continue;
            }

            substituted[i] = argument == typeof(object) ? typeof(string) : typeof(object);
        }

        try {
            Type probeType = declaringType.GetGenericTypeDefinition().MakeGenericType(substituted);
            return MethodBase.GetMethodFromHandle(target.MethodHandle, probeType.TypeHandle);
        } catch (Exception) {
            return null;
        }
    }

    private static bool SameInstructions(MethodDefinition left, MethodDefinition right) {
        if (left.Body.Instructions.Count != right.Body.Instructions.Count) {
            return false;
        }

        for (int i = 0; i < left.Body.Instructions.Count; i++) {
            Instruction a = left.Body.Instructions[i];
            Instruction b = right.Body.Instructions[i];
            if (a.OpCode != b.OpCode) {
                return false;
            }

            if (a.Operand is Instruction || a.Operand is Instruction[] || a.Operand is VariableDefinition || a.Operand is ParameterDefinition) {
                continue;
            }

            if (a.Operand?.ToString() != b.Operand?.ToString()) {
                return false;
            }
        }

        for (int i = 0; i < left.Body.Variables.Count; i++) {
            if (i >= right.Body.Variables.Count) {
                return false;
            }

            if (left.Body.Variables[i].VariableType.FullName != right.Body.Variables[i].VariableType.FullName) {
                return false;
            }
        }

        return left.Body.Variables.Count == right.Body.Variables.Count;
    }

    private static void PrefixSharedGenericGuard(
        MethodDefinition wrapperDefinition,
        Injection injection,
        List<List<Instruction>> bodies,
        int firstNewBody,
        Instruction skipTo) {
        if (injection.RequestedInstantiation is null) {
            return;
        }

        for (int i = firstNewBody; i < bodies.Count; i++) {
            bodies[i].InsertRange(0, BuildSharedGenericGuard(wrapperDefinition.Module, injection.RequestedInstantiation, skipTo));
        }
    }

    private static List<Instruction> BuildSharedGenericGuard(ModuleDefinition module, Type requested, Instruction skipTo) {
        MethodInfo matches = typeof(SharedGenericGuard).GetMethod(nameof(SharedGenericGuard.Matches))!;
        MethodInfo fromHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
        return [
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldtoken, module.ImportReference(requested)),
            Instruction.Create(OpCodes.Call, module.ImportReference(fromHandle)),
            Instruction.Create(OpCodes.Call, module.ImportReference(matches)),
            Instruction.Create(OpCodes.Brfalse, skipTo),
        ];
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

    // A [Local] that another mod's transpiler broke evicts its own injection and nothing else, so
    // the mod that caused the break still applies and an undo of an unrelated handle still works.
    // Composing is not incremental, so dropping one means starting over; only a failure pays that.
    // Evicting the last injection would leave the owner with no error at all, so that one throws.
    // Every other local-binding failure throws too: a selector that would have missed against the
    // bare target body is the author's own typo, and dropping it turns an error into an injection
    // that silently never runs.
    private static bool CanEvict(ConcordEmitException failure, IReadOnlyList<Injection> live) {
        if (failure.LocalBindingMethod is null || !failure.BrokenByAForeignEdit) {
            return false;
        }

        int matched = 0;
        foreach (Injection injection in live) {
            if (Owns(failure, injection)) {
                matched++;
            }
        }

        return matched > 0 && matched < live.Count;
    }

    // Dispatch stamps the exact injection, so two selectors on one shared helper method do not fall
    // together. Only a failure raised outside the dispatch loop falls back to the method.
    private static bool Owns(ConcordEmitException failure, Injection injection) {
        return failure.LocalBindingInjection is null
            ? injection.InjectionMethod == failure.LocalBindingMethod
            : ReferenceEquals(failure.LocalBindingInjection, injection);
    }

    private static List<Injection> Evict(
        IReadOnlyList<Injection> live, ConcordEmitException failure, List<RejectedInjection> rejected) {
        List<Injection> kept = new List<Injection>(live.Count);
        foreach (Injection injection in live) {
            if (Owns(failure, injection)) {
                rejected.Add(new RejectedInjection(injection.Owner, failure.Code, failure.Message));
            } else {
                kept.Add(injection);
            }
        }

        return kept;
    }

    // The last failure is the one that empties the set and throws, so an injection evicted earlier in
    // the same compose would otherwise vanish. Carry those diagnostics along in the thrown message.
    private static ConcordEmitException WithEarlierEvictions(ConcordEmitException failure, List<RejectedInjection> rejected) {
        System.Text.StringBuilder message = new System.Text.StringBuilder(failure.Detail);
        foreach (RejectedInjection earlier in rejected) {
            message.Append("\nAlso evicted, owner '").Append(earlier.Owner).Append("': ").Append(earlier.Message);
        }

        return new ConcordEmitException(failure.Code, message.ToString());
    }

    private static ComposeResult ComposeOnce(MethodBase target, IReadOnlyList<Injection> ordered, List<RejectedInjection> rejected) {
        ValidateComposition(target, ordered);

        MethodBase resolved = target;

        using DynamicMethodDefinition source = new DynamicMethodDefinition(resolved);
        Type returnType = ResolveReturnType(resolved);
        Type[] parameterTypes = ResolveParameterTypes(resolved);

        using DynamicMethodDefinition wrapper = new DynamicMethodDefinition(WrapperName(resolved), returnType, parameterTypes);
        BodyCopier.CopySpine(source.Definition, wrapper.Definition, resolved.DeclaringType!);

        PartitionTranspilers(ordered, out List<Injection> preTranspilers, out List<Injection> finalTranspilers, out List<Injection> declarative);

        int rawLocalCount = wrapper.Definition.Body.Variables.Count;
        RunTranspilers(wrapper.Definition, resolved, preTranspilers);

        AssembleInto(wrapper.Definition, resolved, declarative, returnType, rawLocalCount);
        RunTranspilers(wrapper.Definition, resolved, finalTranspilers);
        MethodInfo wrapperMethod = wrapper.Generate();
        return new ComposeResult(wrapperMethod, () => OriginalBody.Clone(resolved), rejected);
    }

    private static string ComposeDumpOnce(MethodBase target, IReadOnlyList<Injection> ordered) {
        MethodBase resolved = target;

        using DynamicMethodDefinition source = new DynamicMethodDefinition(resolved);
        Type returnType = ResolveReturnType(resolved);
        Type[] parameterTypes = ResolveParameterTypes(resolved);

        using DynamicMethodDefinition wrapper = new DynamicMethodDefinition(WrapperName(resolved), returnType, parameterTypes);
        BodyCopier.CopySpine(source.Definition, wrapper.Definition, resolved.DeclaringType!);

        PartitionTranspilers(ordered, out List<Injection> preTranspilers, out List<Injection> finalTranspilers, out List<Injection> declarative);

        int rawLocalCount = wrapper.Definition.Body.Variables.Count;
        RunTranspilers(wrapper.Definition, resolved, preTranspilers);

        Assemble(wrapper.Definition, resolved, declarative, returnType, rawLocalCount);
        RunTranspilers(wrapper.Definition, resolved, finalTranspilers);

        return IlDump.Format(wrapper.Definition);
    }

    private static List<CodeInstruction> TransformStreamOnce(
        MethodBase target,
        IReadOnlyList<CodeInstruction> source,
        IReadOnlyList<Injection> ordered,
        ITranspilerContext context) {
        TranspilerContext writeContext = RequireConcreteContext(context);
        MethodBase resolved = target;
        Type returnType = ResolveReturnType(resolved);
        Type[] parameterTypes = ResolveParameterTypes(resolved);

        using DynamicMethodDefinition wrapper = new DynamicMethodDefinition(WrapperName(resolved), returnType, parameterTypes);

        Dictionary<Instruction, List<int>> provenance = CecilCodeConverter.WriteBack(wrapper.Definition, source, writeContext);
        int firstFreshLabelId = writeContext.NextLabelId;

        PartitionTranspilers(ordered, out List<Injection> preTranspilers, out List<Injection> finalTranspilers, out List<Injection> declarative);

        int rawLocalCount = StreamRawLocalCount(resolved, wrapper.Definition.Body.Variables.Count);
        RunTranspilers(wrapper.Definition, resolved, preTranspilers);
        AssembleInto(wrapper.Definition, resolved, declarative, returnType, rawLocalCount);
        RunTranspilers(wrapper.Definition, resolved, finalTranspilers);

        TranspilerContext readContext = new TranspilerContext(resolved);
        return CecilCodeConverter.ToInstructions(wrapper.Definition, readContext, provenance, firstFreshLabelId);
    }

    // On the stream route every local arrives declared through the context, the target's own included,
    // so the written-back body cannot say which came from another mod's transpiler. The target's own
    // IL can: anything past its slot count was added on the way in.
    private static int StreamRawLocalCount(MethodBase target, int written) {
        // No body to read means no baseline. Counting every slot as the target's own closes the
        // eviction gate; counting none would open it for every failure on the route.
        System.Reflection.MethodBody? own = target.GetMethodBody();
        if (own is null) {
            return written;
        }

        int count = own.LocalVariables.Count;
        return count < written ? count : written;
    }

    private static void Assemble(MethodDefinition wrapperDefinition, MethodBase target, IReadOnlyList<Injection> ordered, Type returnType, int rawLocalCount) {
        ValidateNonHeadInjectionsDoNotReturnControl(ordered);

        MethodBody body = wrapperDefinition.Body;
        ModuleDefinition module = wrapperDefinition.Module;
        bool isVoid = returnType == typeof(void);

        bool hasAround = HasWholeMethodAround(ordered);

        bool needsCtorGuard = hasAround && target.IsConstructor;

        // Snapshot before Concord declares any local of its own, so [Local] only ever sees the
        // target's own slots plus whatever a pre-transpiler added.
        int searchLocalCount = body.Variables.Count;

        HashSet<int> referencedSlots = SpineTemplate.ReferencedLocalIndices(body.Instructions, body.Variables);

        Dictionary<Type, VariableDefinition> stateLocals = AllocateStateLocals(ordered, wrapperDefinition, target);
        ProtocolLocals locals = DeclareLocals(body, module, returnType, isVoid, hasAround && !isVoid, needsCtorGuard, stateLocals) with {
            RawLocalCount = rawLocalCount,
            SearchLocalCount = searchLocalCount,
            ReferencedSlots = referencedSlots,
        };

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
        Instruction finallyEnd = Instruction.Create(OpCodes.Endfinally);
        Instruction leaveEpilogue = Instruction.Create(OpCodes.Leave, epilogueStart);

        WrapperAssembly context = new WrapperAssembly(wrapperDefinition, target, ordered, locals, isVoid, hasAround);
        AssemblyAnchors anchors = new AssemblyAnchors(spine, afterSpine, guardStart, epilogueStart, finallyEnd);
        InjectionBuffers buffers = new InjectionBuffers(
            new List<List<Instruction>>(),
            new List<List<Instruction>>(),
            new List<(Injection, InjectAt.Return)>(),
            new List<Injection>(),
            new List<List<Instruction>>());

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

        List<Instruction> finallyBody = ChainBodies(buffers.FinallyBodies, finallyEnd);
        if (finallyBody.Count > 0 && hasAround) {
            throw new ConcordEmitException(
                "CONC138",
                $"Target '{target.DeclaringType?.Name}.{target.Name}' has both an At.Finally injection and a whole-method Around. An Around already owns the whole body. Write the finally inside it.");
        }

        List<Instruction> assembled = AssembleFinalBody(
            new AssembledBodyParts(heads, hasHead, guardStart, guardBranch, aroundBody, spine, afterSpine, returns, epilogue, finallyBody, finallyEnd, leaveEpilogue));

        body.Instructions.Clear();
        foreach (Instruction instruction in assembled) {
            body.Instructions.Add(instruction);
        }

        if (finallyBody.Count > 0) {
            body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
                TryStart = assembled[0],
                TryEnd = finallyBody[0],
                HandlerStart = finallyBody[0],
                HandlerEnd = epilogueStart,
            });
        }
    }

    private static void ValidateNonHeadInjectionsDoNotReturnControl(IReadOnlyList<Injection> ordered) {
        for (int i = 0; i < ordered.Count; i++) {
            Injection injection = ordered[i];
            if (injection.At is not InjectAt.Head && ControlHandleLowering.ReturnsControl(injection.InjectionMethod)) {
                throw new ConcordEmitException(
                    "CONC015",
                    $"Injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' returns Control. A Control return is only valid on a head injection.");
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

        // At.Local, At.Constant, At.Invoke and At.NewObj all count By against this, never the live
        // spine. A store splice ends in 'stloc slot' and a value injection opens with 'ldloc slot',
        // so At.Local's splices always add an instruction of the kind it matches. The other three
        // only do it when the author's body holds a matching literal, call or allocation, which is
        // the ordinary case. Counting live lets one injection renumber the next one's occurrences.
        // A match only reaches a splice site if the live spine still holds it, which At.Around's
        // replacement does not guarantee; see SearchList. SpliceIndexOf makes that a throw rather
        // than a silent splice at index 0.
        List<Instruction> preSpliceSpine = new List<Instruction>(anchors.Spine);

        IReadOnlyList<Injection> ordered = context.Ordered;
        for (int i = ordered.Count - 1; i >= 0; i--) {
            Injection injection = ordered[i];
            try {
                if (injection.At is InjectAt.Head) {
                    int firstHeadBody = buffers.HeadBodies.Count;
                    ProcessHeadInjection(new InjectionSiteContext(injection, context.WrapperDefinition, context.Target, context.Locals), anchors.GuardStart, context.IsVoid, ref hasHead, buffers.HeadBodies);
                    PrefixSharedGenericGuard(context.WrapperDefinition, injection, buffers.HeadBodies, firstHeadBody, anchors.GuardStart);
                    continue;
                }

                if (injection.At is InjectAt.Tail) {
                    int firstTailBody = buffers.TailBodies.Count;
                    lastExit = DispatchTailInjection(context, anchors, buffers, injection, lastExit);
                    if (lastExit is not null) {
                        PrefixSharedGenericGuard(context.WrapperDefinition, injection, buffers.TailBodies, firstTailBody, lastExit);
                    }

                    continue;
                }

                if (injection.At is InjectAt.Return returnSite) {
                    DispatchReturnInjection(context, anchors, buffers, injection, returnSite);
                    continue;
                }

                if (injection.At is InjectAt.Invoke invoke) {
                    ProcessInvokeInjection(injection, invoke, context.WrapperDefinition, context.Target, context.Locals, anchors.Spine, preSpliceSpine);
                    continue;
                }

                if (injection.At is InjectAt.NewObj newObj) {
                    ProcessNewObjInjection(injection, newObj, context.WrapperDefinition, context.Target, context.Locals, anchors.Spine, preSpliceSpine);
                    continue;
                }

                if (injection.At is InjectAt.Constant constant) {
                    ProcessConstantInjection(injection, constant, context.WrapperDefinition, context.Target, anchors.Spine, preSpliceSpine);
                    continue;
                }

                if (injection.At is InjectAt.Local localSite) {
                    ProcessLocalInjection(injection, localSite, context.WrapperDefinition, context.Target, context.Locals, anchors.Spine, preSpliceSpine);
                    continue;
                }

                if (injection.At is InjectAt.Finally) {
                    ProcessFinallyInjection(new InjectionSiteContext(injection, context.WrapperDefinition, context.Target, context.Locals), anchors.FinallyEnd, buffers.FinallyBodies);
                    continue;
                }

                if (injection.At is InjectAt.Around) {
                    aroundInjection = RegisterAroundInjection(injection, aroundInjection, context.Target);
                    continue;
                }

                throw new ConcordEmitException("CONC116", $"Unsupported injection position '{injection.At.GetType().Name}' reached composition dispatch.");
            } catch (ConcordEmitException failure) when (failure.LocalBindingMethod is not null) {
                // Which injection owns the failure is only knowable here. LocalResolver sees the method,
                // and two injections can share one.
                failure.LocalBindingInjection ??= injection;
                throw;
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
                $"Multiple Around injections on '{target.DeclaringType?.Name}.{target.Name}' are not supported. Only one Around injection per target is allowed.");
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
            if (parts.FinallyBody.Count > 0) {
                assembled.Add(parts.LeaveEpilogue);
                assembled.AddRange(parts.FinallyBody);
                assembled.Add(parts.FinallyEnd);
            }

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
                new InjectionCopyRequest(injectionMethodDefinition.Definition, site.WrapperDefinition, site.Target, site.Injection.InjectionMethod, injectedMembers) { BoundArguments = site.Injection.BoundArguments },
                site.Locals,
                guardStart));
    }

    private static void ProcessFinallyInjection(InjectionSiteContext site, Instruction finallyEnd, List<List<Instruction>> finallyBodies) {
        MethodBase injectionMethod = site.Injection.InjectionMethod;
        if (ControlHandleLowering.FindControlHandleArgIndex(injectionMethod) >= 0) {
            throw new ConcordEmitException(
                "CONC138",
                $"At.Finally injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' declares a ControlHandle. A finally cannot cancel the target or change its return value.");
        }

        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injectionMethod.DeclaringType!, site.Target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injectionMethod);
        finallyBodies.Add(
            BodyCopier.CopyInjection(
                new InjectionCopyRequest(injectionMethodDefinition.Definition, site.WrapperDefinition, site.Target, injectionMethod, injectedMembers) { BoundArguments = site.Injection.BoundArguments },
                site.Locals,
                finallyEnd));
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
                $"Tail injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' on " +
                    $"'{target.DeclaringType?.Name}.{target.Name}' found no return in the target body, so there is nowhere to run it; " +
                    "the method always throws or never exits. Use At.Finally to run when the method exits by any path, or At.Head.");
        }

        Instruction lastExit = allExits[allExits.Count - 1];
        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
        List<Instruction> siteBody = BodyCopier.CopyInjection(
            new InjectionCopyRequest(injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers) { BoundArguments = injection.BoundArguments },
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
                $"Return injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' on " +
                    $"'{target.DeclaringType?.Name}.{target.Name}' found no return instruction in the target body, so there is nowhere to run it. " +
                    "The method always throws or never exits. Use At.Finally to run when the method exits by any path, or At.Head.");
        }

        List<Instruction> exits = SelectReturnExits(allExits, returnSite.By, target, injection);

        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
        foreach (Instruction exit in exits) {
            List<Instruction> siteBody = BodyCopier.CopyInjection(
                new InjectionCopyRequest(injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers) { BoundArguments = injection.BoundArguments },
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
                    $"Return injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' on " +
                    $"'{target.DeclaringType?.Name}.{target.Name}' found no return instruction in the target body, so there is nowhere to run it. " +
                    "The method always throws or never exits. Use At.Finally to run when the method exits by any path, or At.Head.");
            }

            List<Instruction> exits = SelectReturnExits(allExits, returnSite.By, target, injection);

            InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
            using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
            foreach (Instruction exit in exits) {
                List<Instruction> siteBody = BodyCopier.CopyInjection(
                    new InjectionCopyRequest(injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers) { BoundArguments = injection.BoundArguments },
                    locals,
                    exit,
                    insideAround: true);
                BodyCopier.RewriteSpliceArgs(siteBody, spineCopy.ArgLocals);
                BodyCopier.RewriteSpliceLocals(siteBody, spineCopy.LocalMap);

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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3267", Justification = "Loop body copies IL, redirects branches, and splices into two collections. Projecting to Select would obscure it.")]
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
                    $"Tail injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' on " +
                    $"'{target.DeclaringType?.Name}.{target.Name}' found no return in the target body, so there is nowhere to run it; " +
                    "the method always throws or never exits. Use At.Finally to run when the method exits by any path, or At.Head.");
            }

            Instruction lastExit = allExits[allExits.Count - 1];

            InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
            using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
            List<Instruction> siteBody = BodyCopier.CopyInjection(
                new InjectionCopyRequest(injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers) { BoundArguments = injection.BoundArguments },
                locals,
                lastExit,
                insideAround: true);
            BodyCopier.RewriteSpliceArgs(siteBody, spineCopy.ArgLocals);
            BodyCopier.RewriteSpliceLocals(siteBody, spineCopy.LocalMap);

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
        foreach (Instruction instruction in instructions) {
            if (!ReferenceEquals(instruction, originalTarget) && ReferenceEquals(instruction.Operand, originalTarget)) {
                instruction.Operand = newTarget;
            }
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
        List<Instruction> spine,
        List<Instruction> preSpliceSpine) {
        string effectiveName = AccessorNameResolver.ResolveAccessorName(
            invoke.DeclaringType,
            invoke.Method,
            injection.InjectionMethod,
            invoke.Shift is At.Around);

        bool includeFieldReads = invoke.Shift is At.Head or At.Tail;
        IReadOnlyList<Instruction> searchable = CallSiteQuery.Narrow(SearchList(invoke.Shift, spine, preSpliceSpine), invoke.Slice, target);
        List<Instruction> allSites = ControlHandleLowering.FindInvokeCallSites(
            searchable,
            invoke.DeclaringType,
            effectiveName,
            invoke.ParameterTypes,
            includeFieldReads);

        List<Instruction> sites = SelectCallSites(allSites, invoke.By, target, $"{invoke.DeclaringType.Name}.{invoke.Method}", invoke.Slice is not null);
        SpliceCallSiteInjection(
            new InjectionSiteContext(injection, wrapperDefinition, target, locals),
            invoke.Shift,
            invoke.Arg,
            newObj: false,
            sites,
            spine);
    }

    private static void ProcessNewObjInjection(
        Injection injection,
        InjectAt.NewObj newObj,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        List<Instruction> spine,
        List<Instruction> preSpliceSpine) {
        IReadOnlyList<Instruction> searchable = CallSiteQuery.Narrow(SearchList(newObj.Shift, spine, preSpliceSpine), newObj.Slice, target);
        List<Instruction> allSites = CallSiteQuery.Match(
            searchable,
            newObj.ConstructedType,
            ".ctor",
            newObj.ParameterTypes,
            includeFieldReads: false,
            matchNewObj: true);
        List<Instruction> sites = SelectCallSites(allSites, newObj.By, target, $"new {newObj.ConstructedType.Name}", newObj.Slice is not null);
        SpliceCallSiteInjection(
            new InjectionSiteContext(injection, wrapperDefinition, target, locals),
            newObj.Shift,
            newObj.Arg,
            newObj: true,
            sites,
            spine);
    }

    // At.Around replaces its site instead of inserting beside it, and the replacement's own copy of
    // the call is how a second Around chains onto the first. The pre-splice snapshot still holds the
    // instruction the first wrap removed, so Around has to keep counting live. Every other shift
    // only inserts, which is what makes the snapshot both safe and necessary there.
    private static List<Instruction> SearchList(At shift, List<Instruction> spine, List<Instruction> preSpliceSpine) {
        return shift is At.Around ? spine : preSpliceSpine;
    }

    // Matching runs on a pre-splice snapshot, so a match can outlive the live spine if some position
    // removes instructions during dispatch. Unguarded, IndexOf returns -1 and the splice silently
    // lands at index 0, which composes into a wrong body rather than failing.
    private static int SpliceIndexOf(List<Instruction> spine, Instruction match) {
        int index = spine.IndexOf(match);
        if (index < 0) {
            throw new InvalidOperationException(
                $"Concord bug: injection site '{match}' is no longer in the spine it is being spliced into.");
        }

        return index;
    }

    /// <summary>
    ///     Splices an injection onto an already-selected set of call sites. Shared by
    ///     <see cref="InjectAt.Invoke" /> and <see cref="InjectAt.NewObj" />, which differ only in how their
    ///     sites are matched and how the site's shape is resolved.
    /// </summary>
    /// <param name="site">The injection, wrapper, target, and protocol locals being composed.</param>
    /// <param name="shift">Where the injection runs relative to the matched sites.</param>
    /// <param name="arg">For <see cref="At.Argument" />, the 1-based argument to rewrite, or 0 to infer.</param>
    /// <param name="newObj">Whether the matched sites are <c>newobj</c> instructions.</param>
    /// <param name="sites">The matched sites, in body order.</param>
    /// <param name="spine">
    ///     The copied target body the sites live in. This must be the complete body, never a slice-narrowed
    ///     view of it: argument capture reads branch targets and stack depth out of it, and a subset makes
    ///     it report a plausible but wrong boundary.
    /// </param>
    private static void SpliceCallSiteInjection(
        InjectionSiteContext site,
        At shift,
        uint arg,
        bool newObj,
        List<Instruction> sites,
        List<Instruction> spine) {
        Injection injection = site.Injection;
        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, site.Target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);

        if (shift is At.Argument) {
            RewriteCallSiteArguments(site, arg, newObj, sites, injectionMethodDefinition.Definition, injectedMembers, spine);
            return;
        }

        if (shift is At.Around) {
            foreach (Instruction match in sites) {
                WrapCallSite(spine, match, injectionMethodDefinition.Definition, site.WrapperDefinition, site.Target, injection.InjectionMethod, injectedMembers, newObj);
            }

            return;
        }

        bool after = shift is At.Tail;
        foreach (Instruction match in sites) {
            Dictionary<int, VariableDefinition>? captureBinding = EmitCaptureSpills(site, match, newObj, spine);
            int siteIndex = SpliceIndexOf(spine, match);
            Instruction continuation = after ? spine[siteIndex + 1] : match;
            List<Instruction> invokeBody = BodyCopier.CopyInjection(
                new InjectionCopyRequest(injectionMethodDefinition.Definition, site.WrapperDefinition, site.Target, injection.InjectionMethod, injectedMembers) { BoundArguments = injection.BoundArguments },
                site.Locals,
                continuation,
                captureBinding: captureBinding);
            spine.InsertRange(after ? siteIndex + 1 : siteIndex, invokeBody);
        }
    }

    /// <summary>
    ///     Spills each <see cref="CaptureAttribute" /> parameter's matched-call argument into a fresh wrapper
    ///     local, by duplicating the value at the point the argument finished pushing.
    /// </summary>
    /// <param name="site">The injection, wrapper, target, and protocol locals being composed.</param>
    /// <param name="match">The matched call or construction instruction.</param>
    /// <param name="newObj">Whether <paramref name="match" /> is a <c>newobj</c> instruction.</param>
    /// <param name="spine">The copied target body the match lives in. Spills are inserted into it.</param>
    /// <returns>Maps an injection argument index to the local holding its captured value, or null when nothing is captured.</returns>
    private static Dictionary<int, VariableDefinition>? EmitCaptureSpills(
        InjectionSiteContext site,
        Instruction match,
        bool newObj,
        List<Instruction> spine) {
        MethodBase injectionMethod = site.Injection.InjectionMethod;
        if (!DeclaresCapture(injectionMethod)) {
            return null;
        }

        if (match.Operand is not MethodReference reference) {
            throw new ConcordEmitException(
                "CONC130",
                $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{site.Target.DeclaringType?.Name}.{site.Target.Name}' " +
                "uses [Capture], but the matched site is a field read, which takes no arguments.");
        }

        CallSiteShape shape = ResolveSiteShape(reference.ResolveReflection(), newObj);
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int argOffset = injectionMethod.IsStatic ? 0 : 1;

        Dictionary<int, VariableDefinition> binding = new Dictionary<int, VariableDefinition>();
        Dictionary<(uint Arg, bool Dereference), VariableDefinition> spilled =
            new Dictionary<(uint Arg, bool Dereference), VariableDefinition>();

        for (int i = 0; i < parameters.Length; i++) {
            CaptureAttribute? capture = parameters[i].GetCustomAttribute<CaptureAttribute>();
            if (capture is null) {
                continue;
            }

            if (capture.Arg == 0 || capture.Arg > shape.ParameterTypes.Length) {
                throw new ConcordEmitException(
                    "CONC130",
                    $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{site.Target.DeclaringType?.Name}.{site.Target.Name}' " +
                    $"captures argument {capture.Arg}, but the matched call takes {shape.ParameterTypes.Length} argument(s).");
            }

            Type slotType = shape.ParameterTypes[capture.Arg - 1];
            bool dereference = slotType.IsByRef && !parameters[i].ParameterType.IsByRef;
            Type storedType = dereference ? slotType.GetElementType()! : slotType;

            if (storedType != parameters[i].ParameterType) {
                throw new ConcordEmitException(
                    "CONC130",
                    $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{site.Target.DeclaringType?.Name}.{site.Target.Name}' " +
                    $"declares captured parameter '{parameters[i].Name}' as '{parameters[i].ParameterType}', but argument {capture.Arg} of the " +
                    $"matched call is '{slotType}'. A captured parameter must declare the argument's own type, or its element type to read a " +
                    "by-ref argument by value.");
            }

            (uint Arg, bool Dereference) slot = (capture.Arg, dereference);
            if (!spilled.TryGetValue(slot, out VariableDefinition? spill)) {
                spill = SpillCaptureSlot(site, match, shape, capture.Arg, storedType, dereference, spine);
                spilled[slot] = spill;
            }

            binding[i + argOffset] = spill;
        }

        return binding;
    }

    private static VariableDefinition SpillCaptureSlot(
        InjectionSiteContext site,
        Instruction match,
        CallSiteShape shape,
        uint arg,
        Type storedType,
        bool dereference,
        List<Instruction> spine) {
        MethodBase injectionMethod = site.Injection.InjectionMethod;
        int boundary = CallSiteProvenance.FindArgumentPushEnd(
            spine,
            SpliceIndexOf(spine, match),
            (int)(arg - 1),
            shape.ParameterTypes.Length,
            shape.HasThis,
            site.WrapperDefinition);

        // A prefix carries the stack depth of the instruction before it, so the backward walk can land on
        // one. Inserting there would split it from the instruction it modifies; the value is already on
        // top at the last non-prefix instruction, so step back to that.
        while (boundary >= 0 && spine[boundary].OpCode.OpCodeType == OpCodeType.Prefix) {
            boundary--;
        }

        if (boundary < 0) {
            throw new ConcordEmitException(
                "CONC129",
                $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{site.Target.DeclaringType?.Name}.{site.Target.Name}' " +
                $"cannot capture argument {arg}: its push boundary is not determinable at the matched call site. This happens when the " +
                "compiler evaluates an argument across a branch, which a conditional expression (?:, ??, ?., && or ||) in any argument of " +
                "that call produces. Rewrite the argument into a local before the call, or read the value the injection needs another way.");
        }

        ModuleDefinition module = site.WrapperDefinition.Module;
        VariableDefinition spill = new VariableDefinition(module.ImportReference(storedType));
        site.WrapperDefinition.Body.Variables.Add(spill);

        List<Instruction> emitted = [Instruction.Create(OpCodes.Dup)];
        if (dereference) {
            emitted.Add(Instruction.Create(OpCodes.Ldobj, module.ImportReference(storedType)));
        }

        emitted.Add(Instruction.Create(OpCodes.Stloc, spill));
        spine.InsertRange(boundary + 1, emitted);

        return spill;
    }

    private static void RewriteCallSiteArguments(
        InjectionSiteContext site,
        uint arg,
        bool newObj,
        List<Instruction> sites,
        MethodDefinition injectionMethodDefinition,
        InjectedMemberMap injectedMembers,
        List<Instruction> spine) {
        MethodDefinition wrapperDefinition = site.WrapperDefinition;
        MethodBase target = site.Target;
        ModuleDefinition module = wrapperDefinition.Module;
        MethodBody body = wrapperDefinition.Body;

        foreach (Instruction match in sites) {
            MethodBase resolvedOriginal = ((MethodReference)match.Operand).ResolveReflection();
            CallSiteShape shape = ResolveSiteShape(resolvedOriginal, newObj);
            int argIndex = ResolveArgumentIndex(arg, site.Injection.InjectionMethod, shape, target);
            ValidateValueInjectionShape(site.Injection.InjectionMethod, shape.ParameterTypes[argIndex], target);

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
                injectionMethodDefinition,
                wrapperDefinition,
                target,
                site.Injection.InjectionMethod,
                injectedMembers,
                argLocals[argIndex]));
            block.Add(Instruction.Create(OpCodes.Stloc, argLocals[argIndex]));

            if (receiverLocal is not null) {
                block.Add(Instruction.Create(OpCodes.Ldloc, receiverLocal));
            }

            for (int i = 0; i < argLocals.Count; i++) {
                block.Add(Instruction.Create(OpCodes.Ldloc, argLocals[i]));
            }

            int siteIndex = SpliceIndexOf(spine, match);
            spine.InsertRange(siteIndex, block);
        }
    }

    private static CallSiteShape ResolveSiteShape(MethodBase resolved, bool newObj) {
        return newObj ? CallSiteShape.ResolveNewObj((ConstructorInfo)resolved) : CallSiteShape.Resolve(resolved);
    }

    private static int ResolveArgumentIndex(uint arg, MethodBase injectionMethod, CallSiteShape shape, MethodBase target) {
        if (arg > 0) {
            if (arg > shape.ParameterTypes.Length) {
                throw new ConcordEmitException(
                    CodeCONC039,
                    $"Argument injection on '{target.DeclaringType?.Name}.{target.Name}' selects arg {arg}, but the call has {shape.ParameterTypes.Length} argument(s).");
            }

            return (int)(arg - 1);
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
                    $"Argument injection on '{target.DeclaringType?.Name}.{target.Name}' matches more than one '{valueType.Name}' argument. Pass arg: to select one.");
            }

            found = i;
        }

        if (found < 0) {
            throw new ConcordEmitException(
                CodeCONC039,
                $"Argument injection on '{target.DeclaringType?.Name}.{target.Name}' matches no '{valueType.Name}' argument. " +
                "The injection method's parameter type must equal one of the call site's parameter types exactly. Change the parameter type, or pass arg: to pick a specific argument.");
        }

        return found;
    }

    private static void ProcessConstantInjection(
        Injection injection,
        InjectAt.Constant constant,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        List<Instruction> spine,
        List<Instruction> preSpliceSpine) {
        List<Instruction> allMatches = ConstantMatcher.FindMatches(preSpliceSpine, constant.Value);
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

            int matchIndex = SpliceIndexOf(spine, match);
            spine.InsertRange(matchIndex + 1, splice);
        }
    }

    // preSpliceSpine is the spine before any injection spliced into it. Matching and By counting run
    // against it so Concord's own splices never renumber another injection's occurrences; the splice
    // still goes into the live spine, and every match is an object that list holds.
    private static void ProcessLocalInjection(
        Injection injection,
        InjectAt.Local local,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        ProtocolLocals locals,
        List<Instruction> spine,
        List<Instruction> preSpliceSpine) {
        VariableDefinition slot = LocalResolver.Resolve(
            wrapperDefinition.Body, locals, local.LocalType, local.Ordinal, local.Index, local.Name, injection.InjectionMethod, target);

        RejectIndirectlyWrittenSlot(preSpliceSpine, slot, local, injection.InjectionMethod, target);
        RejectLoadOnTheResultSlot(preSpliceSpine, slot, local, locals, injection.InjectionMethod, target);

        IReadOnlyList<Instruction> scope = CallSiteQuery.Narrow(preSpliceSpine, local.Slice, target);
        List<Instruction> allMatches = LocalAccessMatcher.FindMatches(scope, slot, local.Access);

        if (allMatches.Count == 0 || local.By > allMatches.Count) {
            string wanted = local.By == 0 ? "every " + AccessName(local.Access) : $"{AccessName(local.Access)} {local.By}";
            throw new ConcordEmitException(
                "CONC155",
                LocalResolver.Opener(injection.InjectionMethod, target) + $" targets {wanted} of slot {slot.Index} " +
                $"('{local.LocalType}'), but {allMatches.Count} {AccessName(local.Access)}(s) exist {CallSiteQuery.ScopeName(local.Slice is not null)}.") {
                LocalBindingMethod = injection.InjectionMethod,
                BrokenByAForeignEdit = SomethingElseMovedTheCount(target, slot, local, locals, allMatches.Count),
            };
        }

        List<Instruction> matches = CallSiteQuery.Select(
            allMatches, local.By, target, $"{AccessName(local.Access)} of '{local.LocalType}'", local.Slice is not null);

        ValidateValueInjectionShape(injection.InjectionMethod, local.LocalType, target);

        InjectedMemberMap injectedMembers = InjectedMemberResolver.Build(injection.InjectionMethod.DeclaringType!, target);
        using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);

        InjectionCopyRequest request = new InjectionCopyRequest(
            injectionMethodDefinition.Definition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers);
        IReadOnlyDictionary<int, VariableDefinition> localBinding =
            LocalResolver.Bind(request, locals, null) ?? new Dictionary<int, VariableDefinition>();
        LocalHandleLowering? localHandles = LocalHandleLowering.Plan(
            injectionMethodDefinition.Definition.Body, injection.InjectionMethod, wrapperDefinition.Body, locals, target);

        foreach (Instruction match in matches) {
            List<Instruction> splice = local.Access == LocalAccess.Store
                ? StoreSplice(injectionMethodDefinition.Definition, wrapperDefinition, target, injection, injectedMembers, slot, localBinding, localHandles)
                : LoadSplice(injectionMethodDefinition.Definition, wrapperDefinition, target, injection, injectedMembers, slot, localBinding, localHandles);

            int matchIndex = SpliceIndexOf(spine, match);
            spine.InsertRange(matchIndex + 1, splice);
        }
    }

    // The matched stloc already left the value in the slot, so the copied body reads it from there
    // and the trailing stloc writes the replacement back over it.
    private static List<Instruction> StoreSplice(
        MethodDefinition injectionDefinition,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        Injection injection,
        InjectedMemberMap injectedMembers,
        VariableDefinition slot,
        IReadOnlyDictionary<int, VariableDefinition> localBinding,
        LocalHandleLowering? localHandles) {
        List<Instruction> splice = BodyCopier.CopyValueInjection(
            injectionDefinition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers, slot, localBinding, localHandles);
        splice.Add(Instruction.Create(OpCodes.Stloc, slot));
        return splice;
    }

    // The matched ldloc left the value on the stack, so it spills into a temp the copied body reads,
    // and the body's result stays on the stack in its place. The slot itself is not written.
    private static List<Instruction> LoadSplice(
        MethodDefinition injectionDefinition,
        MethodDefinition wrapperDefinition,
        MethodBase target,
        Injection injection,
        InjectedMemberMap injectedMembers,
        VariableDefinition slot,
        IReadOnlyDictionary<int, VariableDefinition> localBinding,
        LocalHandleLowering? localHandles) {
        VariableDefinition loaded = new VariableDefinition(slot.VariableType);
        wrapperDefinition.Body.Variables.Add(loaded);

        List<Instruction> splice = new List<Instruction> { Instruction.Create(OpCodes.Stloc, loaded) };
        splice.AddRange(BodyCopier.CopyValueInjection(
            injectionDefinition, wrapperDefinition, target, injection.InjectionMethod, injectedMembers, loaded, localBinding, localHandles));
        return splice;
    }

    // Another mod having a transpiler on the target is not causation, so the question is asked
    // exactly: would the count the author wrote against have held on the target's own IL? Equal means
    // the By was always wrong and they have to fix it, so throw. Different means something else moved
    // it, and dropping this one injection beats failing the target for everyone. Only runs on the
    // failure path, so the second Cecil read costs nothing in the normal case.
    private static bool SomethingElseMovedTheCount(
        MethodBase target, VariableDefinition slot, InjectAt.Local local, ProtocolLocals locals, int composedCount) {
        if (slot.Index >= locals.RawLocalCount) {
            return true;
        }

        try {
            using DynamicMethodDefinition original = new DynamicMethodDefinition(target);
            MethodBody body = original.Definition.Body;
            if (slot.Index >= body.Variables.Count) {
                return true;
            }

            List<Instruction> own = new List<Instruction>(body.Instructions);
            IReadOnlyList<Instruction> scope = CallSiteQuery.Narrow(own, local.Slice, target);
            return LocalAccessMatcher.FindMatches(scope, body.Variables[slot.Index], local.Access).Count != composedCount;
        } catch (ConcordEmitException) {
            // A Slice that no longer narrows against the bare body says nothing about who moved the
            // count, and guessing "foreign" here would evict the author's own mistake.
            return false;
        }
    }

    // Only an address the body writes through is a problem: the store never fires for it, because
    // an indirect write leaves no stloc to match. An address handed to a call is fine, since a
    // read-only call mutates nothing and every real assignment still emits its own store. This
    // reads the whole spine and ignores Slice on purpose: a write outside the range still decides
    // whether the slot is writable at all.
    private static void RejectIndirectlyWrittenSlot(
        IReadOnlyList<Instruction> spine, VariableDefinition slot, InjectAt.Local local, MethodBase injectionMethod, MethodBase target) {
        if (local.Access != LocalAccess.Store || !LocalAccessMatcher.HasIndirectWrite(spine, slot)) {
            return;
        }

        throw new ConcordEmitException(
            "CONC154",
            LocalResolver.Opener(injectionMethod, target) + $" targets a store of slot {slot.Index} ('{local.LocalType}'), " +
            "but the body writes that slot through its address. In-place mutation will not be seen: an indirect write leaves no " +
            "store to match, so the injection would silently miss it and the value would look like it never changed. Target a slot " +
            "the body assigns by value.") {
            LocalBindingMethod = injectionMethod,
        };
    }

    // NormalizeReturnSites clones the shared 'ldloc' feeding the return into every branch site, so
    // the load count on the result slot goes from one to one per return path. It only runs when
    // some injection uses At.Return or At.Tail, so an unrelated mod would decide what By means.
    // One return path is safe: cloning one load into one site leaves the count where it was.
    private static void RejectLoadOnTheResultSlot(
        IReadOnlyList<Instruction> spine, VariableDefinition slot, InjectAt.Local local, ProtocolLocals locals, MethodBase injectionMethod, MethodBase target) {
        if (local.Access != LocalAccess.Load) {
            return;
        }

        int paths = CountResultPaths(spine, slot, locals);
        if (paths < 2) {
            return;
        }

        throw new ConcordEmitException(
            "CONC159",
            LocalResolver.Opener(injectionMethod, target) + $" targets a load of slot {slot.Index} ('{local.LocalType}'), " +
            $"but that slot carries the method's result across {paths} return paths. An unrelated At.Return or At.Tail injection " +
            $"rewrites the one shared read into one read per path, so the load count moves between 1 and {paths} depending on which " +
            "other mods are installed. Target the store instead, or use At.Return.") {
            LocalBindingMethod = injectionMethod,
        };
    }

    // How many return paths would read the slot once normalization has run. After it has, that is
    // the number of 'ldloc slot; stloc result' sites. Before it has, there is one such site and the
    // answer is how many branches jump to it, which is what NormalizeReturnSites would clone into.
    private static int CountResultPaths(IReadOnlyList<Instruction> spine, VariableDefinition slot, ProtocolLocals locals) {
        List<int> sites = new List<int>();
        for (int i = 0; i < spine.Count - 1; i++) {
            if (LocalAccessMatcher.IsLoad(spine[i], slot) && StoresTheResult(spine[i + 1], locals)) {
                sites.Add(i);
            }
        }

        if (sites.Count != 1) {
            return sites.Count;
        }

        // NormalizeReturnSites only clones a shared load nothing falls through into, so a load that
        // is both branched to and fallen into keeps its single read and the count cannot move.
        // Counting the branches anyway would reject a CONC159 the normalizer would never touch.
        int loadIndex = sites[0];
        if (loadIndex == 0 || !IsUnconditionalExit(spine[loadIndex - 1].OpCode)) {
            return 1;
        }

        int branches = 0;
        foreach (Instruction instruction in spine) {
            if (IsUnconditionalBranch(instruction.OpCode) && ReferenceEquals(instruction.Operand, spine[loadIndex])) {
                branches++;
            }
        }

        return branches == 0 ? 1 : branches;
    }

    private static bool StoresTheResult(Instruction instruction, ProtocolLocals locals) {
        return (locals.ReturnValue is not null && LocalAccessMatcher.IsStore(instruction, locals.ReturnValue))
            || (locals.SpliceValue is not null && LocalAccessMatcher.IsStore(instruction, locals.SpliceValue));
    }

    private static bool IsUnconditionalBranch(OpCode opCode) {
        return opCode == OpCodes.Br || opCode == OpCodes.Br_S || opCode == OpCodes.Leave || opCode == OpCodes.Leave_S;
    }

    private static string AccessName(LocalAccess access) {
        return access == LocalAccess.Store ? "store" : "load";
    }

    // A [Local] sibling is not part of the value shape: it reads another slot in the same signature,
    // which is the whole point of At.Local. Only the non-[Local] parameters are counted here.
    private static void ValidateValueInjectionShape(MethodBase injectionMethod, Type valueType, MethodBase target) {
        List<ParameterInfo> parameters = [];
        foreach (ParameterInfo parameter in injectionMethod.GetParameters()) {
            if (parameter.GetCustomAttribute<LocalAttribute>() is null
                && !LocalHandleLowering.IsLocalHandleType(parameter.ParameterType)) {
                parameters.Add(parameter);
            }
        }

        if (parameters.Count != 1) {
            throw new ConcordEmitException(
                CodeCONC039,
                $"Value injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on '{target.DeclaringType?.Name}.{target.Name}' must declare exactly one parameter, got {parameters.Count}.");
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
                $"Multiple Around injections on '{site.Target.DeclaringType?.Name}.{site.Target.Name}' are not supported. Only one Around injection per target is allowed.");
        }

        ValidateWholeMethodOperationOnly(site.Injection.InjectionMethod, site.Target);
        CallSiteShape shape = CallSiteShape.Resolve(site.Target);
        ValidateOperationShape(site.Injection.InjectionMethod, shape, site.Target, allowValueReceiver: true);

        HashSet<VariableDefinition> protocolLocals = CollectProtocolLocals(site.Locals);

        SpineTemplate template = SpineTemplate.Capture(spine, site.WrapperDefinition.Body.ExceptionHandlers, protocolLocals, site.WrapperDefinition.Body.Variables);

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
            new InjectionCopyRequest(injectionMethodDefinition.Definition, site.WrapperDefinition, site.Target, site.Injection.InjectionMethod, injectedMembers) { BoundArguments = site.Injection.BoundArguments },
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

        if (locals.State is not null) {
            foreach (KeyValuePair<Type, VariableDefinition> entry in locals.State) {
                protocolLocals.Add(entry.Value);
            }
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
                    "' is inside a loop. The original body can only be spliced once.");
            }
        }
    }

    private static void EnsureConstructorHasInvokeSite(MethodBody injectionBody, MethodBase injectionMethod, MethodBase target) {
        foreach (Instruction instruction in injectionBody.Instructions) {
            if (ControlHandleLowering.IsOperationInvoke(instruction)) {
                return;
            }
        }

        throw new ConcordEmitException(
            "CONC112",
            $"Whole-method Around injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on constructor '{target.DeclaringType?.Name}.{target.Name}' " +
            "never calls Invoke(...). A constructor Around must invoke the original constructor exactly once.");
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
                    "' is used mid-expression on a target with exception handlers. Splicing the original body clears the evaluation stack on any protected-region exit. " +
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
        InjectedMemberMap injectedMembers,
        bool newObj) {
        MethodReference originalCall = (MethodReference)site.Operand;
        MethodBase resolvedOriginal = originalCall.ResolveReflection();
        CallSiteShape shape = ResolveSiteShape(resolvedOriginal, newObj);
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

        int siteIndex = SpliceIndexOf(spine, site);
        spine.RemoveAt(siteIndex);

        List<Instruction> replacement = new List<Instruction>(spill.Count + wrapBody.Count + 1);
        replacement.AddRange(spill);
        replacement.AddRange(wrapBody);
        replacement.Add(wrapEnd);
        spine.InsertRange(siteIndex, replacement);
    }

    private static List<Instruction> SelectCallSites(List<Instruction> allSites, uint by, MethodBase target, string siteDescription, bool sliced) {
        if (allSites.Count == 0) {
            throw new ConcordEmitException(
                "CONC031",
                $"Injection on '{target.DeclaringType?.Name}.{target.Name}' targets call site '{siteDescription}' " +
                $"which does not occur {CallSiteQuery.ScopeName(sliced)}. Check the member name and parameterTypes on the [Inject] target and any [Slice] range; " +
                "if the call lives inside a lambda, local function, or async/iterator state machine, target that method instead.");
        }

        return CallSiteQuery.Select(allSites, by, target, siteDescription, sliced);
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

    private static List<Instruction> SelectReturnExits(List<Instruction> allExits, uint by, MethodBase target, Injection injection) {
        if (by == 0) {
            return allExits;
        }

        if (by > allExits.Count) {
            throw new ConcordEmitException(
                "CONC035",
                $"Return injection '{injection.InjectionMethod.DeclaringType?.Name}.{injection.InjectionMethod.Name}' on " +
                    $"'{target.DeclaringType?.Name}.{target.Name}' targets return occurrence {by}, but the body has only {allExits.Count}. " +
                    "Occurrences count from 1 in body order. Pass a lower by, or drop by to attach to every return.");
        }

        return [allExits[(int)(by - 1)]];
    }

    private static ProtocolLocals DeclareLocals(
        MethodBody body,
        ModuleDefinition module,
        Type returnType,
        bool isVoid,
        bool needsSpliceValue,
        bool needsCtorGuard,
        IReadOnlyDictionary<Type, VariableDefinition> stateLocals) {
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
            return new ProtocolLocals(cancel, null, null, null, ctorBodyRan, ctorBodyRanTwice, stateLocals);
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

        return new ProtocolLocals(cancel, hasReturn, returnValue, spliceValue, ctorBodyRan, ctorBodyRanTwice, stateLocals);
    }

    // One state local per patch declaration type, allocated up front so every injection body copied
    // from that declaration - head, tail, or spliced into an Around - lowers to the same slot.
    private static Dictionary<Type, VariableDefinition> AllocateStateLocals(
        IReadOnlyList<Injection> ordered,
        MethodDefinition wrapperDefinition,
        MethodBase target) {
        Dictionary<Type, Type> declared = new Dictionary<Type, Type>();
        foreach (Injection injection in ordered) {
            Type? owner = injection.InjectionMethod.DeclaringType;
            if (owner is null) {
                continue;
            }

            using DynamicMethodDefinition injectionMethodDefinition = new DynamicMethodDefinition(injection.InjectionMethod);
            CollectStateTypes(injectionMethodDefinition.Definition.Body, owner, target, declared);
        }

        Dictionary<Type, VariableDefinition> locals = new Dictionary<Type, VariableDefinition>(declared.Count);
        foreach (KeyValuePair<Type, Type> entry in declared) {
            VariableDefinition local = new VariableDefinition(wrapperDefinition.Module.ImportReference(entry.Value));
            wrapperDefinition.Body.Variables.Add(local);
            locals[entry.Key] = local;
        }

        return locals;
    }

    private static void CollectStateTypes(MethodBody injectionBody, Type owner, MethodBase target, Dictionary<Type, Type> declared) {
        foreach (Instruction instruction in injectionBody.Instructions) {
            ControlHandleLowering.ControlCallKind kind = ControlHandleLowering.ClassifyCall(instruction);
            if (kind != ControlHandleLowering.ControlCallKind.SetState && kind != ControlHandleLowering.ControlCallKind.GetState) {
                continue;
            }

            Type? stateType = ControlHandleLowering.ResolveStateType(instruction);
            if (stateType is null) {
                continue;
            }

            if (declared.TryGetValue(owner, out Type? existing) && existing != stateType) {
                throw new ConcordEmitException(
                    "CONC127",
                    $"Patch declaration '{owner.Name}' uses state type '{existing.Name}' and '{stateType.Name}' " +
                    $"for the same slot on '{target.DeclaringType?.Name}.{target.Name}'. One declaration must use one state type per target.");
            }

            declared[owner] = stateType;
        }
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
            Instruction.Create(OpCodes.Ldstr, "Constructor Around invoked the original constructor body more than once. The pre-entry guard blocked the second attempt."),
            Instruction.Create(OpCodes.Newobj, exceptionCtor),
            Instruction.Create(OpCodes.Throw),
            afterTwiceCheck,
            Instruction.Create(OpCodes.Ldloc, locals.CtorBodyRan!),
            Instruction.Create(OpCodes.Brtrue, afterZeroCheck),
            Instruction.Create(OpCodes.Ldstr, "Constructor Around never invoked the original constructor body. The object was not fully constructed."),
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
                $"Injection on non-void target '{target.DeclaringType?.Name}.{target.Name}' cancels without setting ReturnValue. A return value is required when the original method is skipped.");
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

        foreach (Instruction instruction in spine) {
            if (ReferenceEquals(instruction.Operand, afterSpine)) {
                instruction.Operand = spliceJoin;
            }
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
}
