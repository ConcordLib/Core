using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

/// <summary>
///     Pairs each <see cref="LocalHandle{T}" /> receiver load in an injection body with the
///     <c>get_Value</c> or <c>set_Value</c> call that consumes it, so the copier can drop the load and
///     emit a plain load or store of the wrapper local the handle selected.
/// </summary>
/// <remarks>
///     <see cref="ControlHandleLowering" /> can rewrite its receiver and its consuming call
///     independently because an injection may declare at most one control handle. Nothing limits how
///     many local handles an injection declares, and C# emits <c>ldarg h; expr; call set_Value</c>, so a
///     call is not adjacent to its receiver and the two have to be matched by argument index instead.
/// </remarks>
internal sealed class LocalHandleLowering {
    private readonly HashSet<Instruction> receivers;
    private readonly Dictionary<Instruction, (VariableDefinition Slot, bool Write)> accesses;

    private LocalHandleLowering(
        HashSet<Instruction> receivers,
        Dictionary<Instruction, (VariableDefinition Slot, bool Write)> accesses) {
        this.receivers = receivers;
        this.accesses = accesses;
    }

    private enum ValueCallKind {
        None,
        Read,
        Write,
    }

    internal static bool IsLocalHandleType(Type type) {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LocalHandle<>);
    }

    internal static bool DeclaresLocalHandle(MethodBase injectionMethod) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            if (IsLocalHandleType(parameters[i].ParameterType)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Resolves every <see cref="LocalHandle{T}" /> parameter to a wrapper local and pairs its loads
    ///     with the property calls that consume them.
    /// </summary>
    /// <param name="injectionBody">The injection method's own decompiled body.</param>
    /// <param name="injectionMethod">The reflection handle for the injection method.</param>
    /// <param name="wrapperBody">The wrapper body whose locals the handles select from.</param>
    /// <param name="locals">The wrapper's protocol locals, which bound the searchable slot range.</param>
    /// <param name="target">The original method being patched, used for diagnostic messages.</param>
    /// <returns>The pairing, or null when the injection declares no handle.</returns>
    internal static LocalHandleLowering? Plan(
        MethodBody injectionBody,
        MethodBase injectionMethod,
        MethodBody wrapperBody,
        ProtocolLocals locals,
        MethodBase target) {
        Dictionary<int, VariableDefinition> binding = Bind(injectionMethod, wrapperBody, locals, target);
        if (binding.Count == 0) {
            return null;
        }

        HashSet<Instruction> receivers = new HashSet<Instruction>();
        Dictionary<Instruction, (VariableDefinition Slot, bool Write)> accesses =
            new Dictionary<Instruction, (VariableDefinition, bool)>();

        foreach (Instruction instruction in injectionBody.Instructions) {
            int argIndex = ControlHandleLowering.GetLoadArgIndex(instruction);
            if (argIndex < 0 || !binding.TryGetValue(argIndex, out VariableDefinition? slot)) {
                continue;
            }

            if (instruction.OpCode == OpCodes.Ldarga || instruction.OpCode == OpCodes.Ldarga_S) {
                throw Escaped(injectionMethod, target);
            }

            // `h.Value *= 10` compiles to `ldarg h; dup; call get_Value; ...; call set_Value`, so one
            // load can put several copies of the handle in flight. They are consumed top down, and
            // each consumer leaves the next copy's walk one slot deeper than a fresh one.
            List<Instruction> copies = new List<Instruction> { instruction };
            for (Instruction? next = instruction.Next; next is not null && next.OpCode == OpCodes.Dup; next = next.Next) {
                copies.Add(next);
            }

            Instruction cursor = copies[copies.Count - 1];
            int depth = 1;
            for (int i = copies.Count - 1; i >= 0; i--) {
                Instruction? consumer = BodyCopier.FindStackConsumer(cursor, depth, out bool blockedByFlow);
                if (consumer is null && blockedByFlow) {
                    throw Branched(injectionMethod, target);
                }

                ValueCallKind kind = consumer is null ? ValueCallKind.None : ClassifyValueCall(consumer);
                if (consumer is null || kind == ValueCallKind.None || accesses.ContainsKey(consumer)) {
                    throw Escaped(injectionMethod, target);
                }

                receivers.Add(copies[i]);
                accesses[consumer] = (slot, kind == ValueCallKind.Write);
                cursor = consumer;
                depth = 1 + IlDump.PushCount(consumer);
            }
        }

        // A Value call the loop never paired got its receiver from somewhere other than the
        // parameter, which means the handle was stashed first.
        foreach (Instruction instruction in injectionBody.Instructions) {
            if (ClassifyValueCall(instruction) != ValueCallKind.None && !accesses.ContainsKey(instruction)) {
                throw Escaped(injectionMethod, target);
            }
        }

        return new LocalHandleLowering(receivers, accesses);
    }

    internal static IEnumerable<int> BoundArgIndices(MethodBase injectionMethod) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int offset = injectionMethod.IsStatic ? 0 : 1;
        for (int i = 0; i < parameters.Length; i++) {
            if (IsLocalHandleType(parameters[i].ParameterType)) {
                yield return i + offset;
            }
        }
    }

    internal bool TryLower(Instruction source, out List<Instruction> emitted) {
        if (this.receivers.Contains(source)) {
            emitted = new List<Instruction>(0);
            return true;
        }

        if (this.accesses.TryGetValue(source, out (VariableDefinition Slot, bool Write) access)) {
            emitted = new List<Instruction> {
                Instruction.Create(access.Write ? OpCodes.Stloc : OpCodes.Ldloc, access.Slot),
            };
            return true;
        }

        emitted = new List<Instruction>(0);
        return false;
    }

    private static Dictionary<int, VariableDefinition> Bind(
        MethodBase injectionMethod, MethodBody wrapperBody, ProtocolLocals locals, MethodBase target) {
        ParameterInfo[] parameters = injectionMethod.GetParameters();
        int offset = injectionMethod.IsStatic ? 0 : 1;
        Dictionary<int, VariableDefinition> binding = new Dictionary<int, VariableDefinition>();

        for (int i = 0; i < parameters.Length; i++) {
            if (!IsLocalHandleType(parameters[i].ParameterType)) {
                continue;
            }

            Type element = parameters[i].ParameterType.GetGenericArguments()[0];
            LocalAttribute? selector = parameters[i].GetCustomAttribute<LocalAttribute>();
            uint ordinal = selector?.Ordinal ?? 0;
            int index = selector?.Index ?? -1;

            binding[i + offset] = LocalResolver.Resolve(
                wrapperBody, locals, element, ordinal, index, selector?.Name, injectionMethod, target, parameters[i].Name);
        }

        return binding;
    }

    private static ValueCallKind ClassifyValueCall(Instruction instruction) {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) {
            return ValueCallKind.None;
        }

        if (instruction.Operand is not MethodReference reference) {
            return ValueCallKind.None;
        }

        MethodBase resolved = reference.ResolveReflection();
        Type? declaringType = resolved.DeclaringType;
        if (declaringType is null || !IsLocalHandleType(declaringType)) {
            return ValueCallKind.None;
        }

        return resolved.Name switch {
            "get_Value" => ValueCallKind.Read,
            "set_Value" => ValueCallKind.Write,
            _ => ValueCallKind.None,
        };
    }

    // The value feeding a Value call came from a conditional, so the call is on the far side of a
    // branch and the linear walk cannot reach it. Same code as an escape because the remedy is the
    // same shape - keep the access a plain statement - but the cause is worth saying out loud.
    private static ConcordEmitException Branched(MethodBase injectionMethod, MethodBase target) {
        return new ConcordEmitException(
            "CONC161",
            $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on " +
            $"'{target.DeclaringType?.Name}.{target.Name}' reads or writes a LocalHandle<T> Value across a conditional. " +
            "Concord lowers the handle away while copying IL, and it cannot follow a branch to the consuming call. " +
            "Split the conditional into an if/else with a plain h.Value assignment in each arm.");
    }

    private static ConcordEmitException Escaped(MethodBase injectionMethod, MethodBase target) {
        return new ConcordEmitException(
            "CONC161",
            $"Injection '{injectionMethod.DeclaringType?.Name}.{injectionMethod.Name}' on " +
            $"'{target.DeclaringType?.Name}.{target.Name}' uses a LocalHandle<T> parameter for something other than a " +
            "direct Value read or write. Concord lowers the handle away, so it cannot be stored in a local or field, " +
            "passed to a call, captured by a lambda, or returned. Keep every h.Value read and write a plain statement.");
    }
}
