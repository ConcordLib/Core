using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

/// <summary>
///     Diagnostic formatter for a composed wrapper body: prints each instruction with its operand
///     and the operand's owning module, the locals table, and a running stack-depth estimate so a
///     malformed or cross-module instruction is visible before the JIT rejects it.
/// </summary>
internal static class IlDump {
    internal static string Format(MethodDefinition method) {
        MethodBody body = method.Body;
        StringBuilder sb = new StringBuilder();

        sb.Append("=== WRAPPER ").Append(method.Name).Append(" ===\n");
        sb.Append("module: ").Append(method.Module.Name).Append('\n');
        sb.Append("returnType: ").Append(Describe(method.ReturnType)).Append('\n');

        sb.Append("params[").Append(method.Parameters.Count).Append("]:\n");
        foreach (ParameterDefinition p in method.Parameters) {
            sb.Append("  arg ").Append(p.Index).Append(": ").Append(Describe(p.ParameterType)).Append('\n');
        }

        sb.Append("locals[").Append(body.Variables.Count).Append("]:\n");
        foreach (VariableDefinition v in body.Variables) {
            sb.Append("  loc ").Append(v.Index).Append(": ").Append(Describe(v.VariableType)).Append('\n');
        }

        sb.Append("instructions[").Append(body.Instructions.Count).Append("]:\n");
        int depth = 0;
        int index = 0;
        foreach (Instruction instruction in body.Instructions) {
            int before = depth;
            int pop = PopCount(instruction, method);
            int push = PushCount(instruction);
            depth = depth - pop + push;

            sb.Append("  ").Append(index.ToString("D3")).Append("  ");
            sb.Append("stk ").Append(before).Append("->").Append(depth).Append("  ");
            sb.Append(instruction.OpCode.Name.PadRight(12));
            sb.Append(FormatOperand(instruction.Operand, method, body));
            if (depth < 0) {
                sb.Append("   <<< STACK UNDERFLOW");
            }

            sb.Append('\n');
            index++;
        }

        sb.Append("final stack depth: ").Append(depth).Append('\n');

        Dictionary<Instruction, int> ehIndexOf = new Dictionary<Instruction, int>();
        for (int i = 0; i < body.Instructions.Count; i++) {
            ehIndexOf[body.Instructions[i]] = i;
        }

        int Idx(Instruction? ins) {
            return ins is not null && ehIndexOf.TryGetValue(ins, out int i) ? i : -1;
        }

        sb.Append("exceptionHandlers[").Append(body.ExceptionHandlers.Count).Append("]:\n");
        foreach (ExceptionHandler handler in body.ExceptionHandlers) {
            sb.Append("  ").Append(handler.HandlerType);
            sb.Append(" try[").Append(Idx(handler.TryStart)).Append("..").Append(Idx(handler.TryEnd)).Append(')');
            sb.Append(" handler[").Append(Idx(handler.HandlerStart)).Append("..").Append(Idx(handler.HandlerEnd)).Append(')');
            if (handler.CatchType is not null) {
                sb.Append(" catch ").Append(Describe(handler.CatchType));
            }

            sb.Append('\n');
        }

        sb.Append("verify:\n");
        sb.Append(Verify(method));

        return sb.ToString();
    }

    internal static int PopCount(Instruction instruction, MethodDefinition method) {
        switch (instruction.OpCode.StackBehaviourPop) {
            case StackBehaviour.Pop0:
                return 0;
            case StackBehaviour.Pop1:
            case StackBehaviour.Popi:
            case StackBehaviour.Popref:
                return 1;
            case StackBehaviour.Pop1_pop1:
            case StackBehaviour.Popi_pop1:
            case StackBehaviour.Popi_popi:
            case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4:
            case StackBehaviour.Popi_popr8:
            case StackBehaviour.Popref_pop1:
            case StackBehaviour.Popref_popi:
                return 2;
            case StackBehaviour.Popi_popi_popi:
            case StackBehaviour.Popref_popi_popi:
            case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4:
            case StackBehaviour.Popref_popi_popr8:
            case StackBehaviour.Popref_popi_popref:
                return 3;
            case StackBehaviour.PopAll:
                return 0;
            case StackBehaviour.Varpop:
                return VarPop(instruction, method);
            default:
                return 0;
        }
    }

    internal static int PushCount(Instruction instruction) {
        switch (instruction.OpCode.StackBehaviourPush) {
            case StackBehaviour.Push0:
                return 0;
            case StackBehaviour.Push1:
            case StackBehaviour.Pushi:
            case StackBehaviour.Pushi8:
            case StackBehaviour.Pushr4:
            case StackBehaviour.Pushr8:
            case StackBehaviour.Pushref:
                return 1;
            case StackBehaviour.Push1_push1:
                return 2;
            case StackBehaviour.Varpush:
                return VarPush(instruction);
            default:
                return 0;
        }
    }

    private static string Verify(MethodDefinition method) {
        MethodBody body = method.Body;
        Dictionary<Instruction, int> indexOf = new Dictionary<Instruction, int>();
        for (int i = 0; i < body.Instructions.Count; i++) {
            indexOf[body.Instructions[i]] = i;
        }

        int?[] entryDepth = new int?[body.Instructions.Count];
        Queue<int> work = new Queue<int>();

        void Seed(Instruction? target, int depth) {
            if (target is null || !indexOf.TryGetValue(target, out int idx)) {
                return;
            }

            if (entryDepth[idx] is null) {
                entryDepth[idx] = depth;
                work.Enqueue(idx);
            }
        }

        SeedVerifyEntryPoints(body, Seed);

        StringBuilder problems = new StringBuilder();
        while (work.Count > 0) { // NOSONAR diagnostic helper; loop runs once seed/checkorseed enqueue work items
            int idx = work.Dequeue();
            ProcessVerifyInstruction(idx, method, body, indexOf, entryDepth, work, problems, Seed);
        }

        return problems.Length == 0 ? "  OK (stack balances at every reachable instruction)\n" : problems.ToString();
    }

    private static void SeedVerifyEntryPoints(MethodBody body, Action<Instruction?, int> seed) {
        seed(body.Instructions.Count > 0 ? body.Instructions[0] : null, 0);
        foreach (ExceptionHandler handler in body.ExceptionHandlers) { // NOSONAR project forbids LINQ in loops (perf/determinism); for-loop is intentional
            seed(handler.HandlerStart, handler.HandlerType == ExceptionHandlerType.Finally ? 0 : 1);
            seed(handler.FilterStart, 1);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107", Justification = "Internal IL-verification walk state; these are the working buffers of a single stack-depth pass and do not form a reusable bundle.")]
    private static void ProcessVerifyInstruction(
        int idx,
        MethodDefinition method,
        MethodBody body,
        Dictionary<Instruction, int> indexOf,
        int?[] entryDepth,
        Queue<int> work,
        StringBuilder problems,
        Action<Instruction?, int> seed) {
        int depth = entryDepth[idx]!.Value;
        Instruction instruction = body.Instructions[idx];

        int after = depth - PopCount(instruction, method) + PushCount(instruction);
        if (after < 0) {
            problems.Append("  ")
                .Append(idx.ToString("D3"))
                .Append("  STACK UNDERFLOW entering=")
                .Append(depth)
                .Append(' ')
                .Append(instruction.OpCode.Name)
                .Append('\n');
            after = 0;
        }

        FlowControl flow = instruction.OpCode.FlowControl;
        if (instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S) {
            seed(instruction.Operand as Instruction, 0);
            return;
        }

        if (instruction.Operand is Instruction branch) {
            CheckOrSeed(branch, after, indexOf, entryDepth, work, problems);
        } else if (instruction.Operand is Instruction[] switchTargets) {
            foreach (Instruction t in switchTargets) { // NOSONAR project forbids LINQ in loops (perf/determinism); for-loop is intentional
                CheckOrSeed(t, after, indexOf, entryDepth, work, problems);
            }
        }

        if (flow is FlowControl.Branch or FlowControl.Return or FlowControl.Throw) {
            return;
        }

        if (idx + 1 < body.Instructions.Count) {
            CheckOrSeed(body.Instructions[idx + 1], after, indexOf, entryDepth, work, problems);
        }
    }

    private static void CheckOrSeed(
        Instruction target,
        int depth,
        Dictionary<Instruction, int> indexOf,
        int?[] entryDepth,
        Queue<int> work,
        StringBuilder problems) {
        if (!indexOf.TryGetValue(target, out int idx)) {
            return;
        }

        if (entryDepth[idx] is int existing) {
            if (existing != depth) {
                problems.Append("  ")
                    .Append(idx.ToString("D3"))
                    .Append("  STACK DEPTH CONFLICT: arrives with ")
                    .Append(depth)
                    .Append(" but already seen with ")
                    .Append(existing)
                    .Append(" (")
                    .Append(target.OpCode.Name)
                    .Append(")\n");
            }

            return;
        }

        entryDepth[idx] = depth;
        work.Enqueue(idx);
    }

    private static string FormatOperand(object? operand, MethodDefinition wrapper, MethodBody body) {
        if (operand is null) {
            return string.Empty;
        }

        switch (operand) {
            case Instruction branch:
                return "-> #" + body.Instructions.IndexOf(branch) + " (" + branch.OpCode.Name + ")";
            case Instruction[] branches:
                return "-> [" + branches.Length + " targets]";
            case MethodReference method:
                return "method " + method.FullName + ForeignTag(method.Module, wrapper.Module);
            case FieldReference field:
                return "field " + field.FullName + ForeignTag(field.DeclaringType?.Scope, wrapper.Module, field.Module);
            case TypeReference type:
                return "type " + type.FullName + ForeignTag(type.Module, wrapper.Module);
            case VariableDefinition variable:
                return "loc " + variable.Index;
            case ParameterDefinition parameter:
                return "arg " +
                       parameter.Index +
                       " (ParameterDefinition seq=" +
                       parameter.Sequence +
                       ") <<< RAW PARAMETERDEFINITION OPERAND";
            case string s:
                return "\"" + s + "\"";
            default:
                return operand + " [" + operand.GetType().Name + "]";
        }
    }

    private static string ForeignTag(ModuleDefinition? operandModule, ModuleDefinition wrapperModule) {
        if (operandModule is null) {
            return "  [module=null]";
        }

        if (!ReferenceEquals(operandModule, wrapperModule)) {
            return "  <<< FOREIGN MODULE: " + operandModule.Name;
        }

        return string.Empty;
    }

    private static string ForeignTag(IMetadataScope? scope, ModuleDefinition wrapperModule, ModuleDefinition? fallback) {
        if (fallback is not null && !ReferenceEquals(fallback, wrapperModule)) {
            return "  <<< FOREIGN MODULE: " + fallback.Name;
        }

        return scope is null ? "  [scope=null]" : "  [scope=" + scope.Name + "]";
    }

    private static string Describe(TypeReference type) {
        return type.FullName + " @ " + (type.Scope?.Name ?? "?");
    }

    private static int VarPop(Instruction instruction, MethodDefinition method) {
        if (instruction.Operand is MethodReference reference) {
            if (instruction.OpCode == OpCodes.Newobj) {
                return reference.Parameters.Count;
            }

            int count = reference.Parameters.Count;
            if (reference.HasThis) {
                count++;
            }

            return count;
        }

        if (instruction.OpCode == OpCodes.Ret) {
            return method.ReturnType.FullName == "System.Void" ? 0 : 1;
        }

        return 0;
    }

    private static int VarPush(Instruction instruction) {
        if (instruction.Operand is not MethodReference reference) {
            return 1;
        }

        if (instruction.OpCode == OpCodes.Newobj) {
            return 1;
        }

        return reference.ReturnType.FullName == "System.Void" ? 0 : 1;
    }
}
