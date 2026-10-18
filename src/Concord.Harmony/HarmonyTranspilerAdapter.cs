using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony;

using CodeInstruction = HarmonyLib.CodeInstruction;

internal delegate IEnumerable<CodeInstruction> HarmonyTranspile(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original);

internal static class HarmonyTranspilerAdapter {
    private static readonly MethodInfo GetExecutingAssembly = typeof(Assembly).GetMethod(nameof(Assembly.GetExecutingAssembly), Type.EmptyTypes)!;

    private static readonly MethodInfo GetTypeFromHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), new[] { typeof(RuntimeTypeHandle) })!;

    private static readonly MethodInfo TypeAssemblyGetter = typeof(Type).GetProperty(nameof(Type.Assembly))!.GetGetMethod()!;

    static HarmonyTranspilerAdapter() {
        TranspilerInvoker.AddShape(TryCompile);
    }

    internal static string? Validate(MethodInfo transpiler) {
        if (transpiler == null) {
            return "Transpiler method is null.";
        }

        string name = (transpiler.DeclaringType?.Name ?? "<global>") + "." + transpiler.Name;
        if (!transpiler.IsStatic) {
            return "Transpiler '" + name + "' is an instance method. Harmony invokes a transpiler as a static.";
        }

        if (!typeof(IEnumerable<CodeInstruction>).IsAssignableFrom(transpiler.ReturnType)) {
            return "Transpiler '" + name + "' has signature " + Describe(transpiler) + ". Harmony needs it to return IEnumerable<HarmonyLib.CodeInstruction>.";
        }

        ParameterInfo[] parameters = transpiler.GetParameters();
        for (int i = 0; i < parameters.Length; i++) {
            Type type = parameters[i].ParameterType;
            if (type == typeof(ILGenerator) || type == typeof(MethodBase) || type == typeof(IEnumerable<CodeInstruction>)) {
                continue;
            }

            return "Transpiler '" + name + "' has signature " + Describe(transpiler) + ". Parameter '" + parameters[i].Name +
                   "' of type " + type.Name + " is not one Concord binds; only ILGenerator, MethodBase and IEnumerable<HarmonyLib.CodeInstruction> are.";
        }

        return null;
    }

    internal static Injection Map(MethodInfo transpiler, string owner) {
        string? reason = Validate(transpiler);
        if (reason != null) {
            throw new ConcordEmitException("CONC116", reason);
        }

        return new Injection(transpiler, new InjectAt.Transpiler(), owner, 0) {
            ForeignOrigin = true,
        };
    }

    internal static IReadOnlyList<Injection> MapForLiveList(IReadOnlyList<Patch> harmonyRunOrder) {
        Injection[] appendOrder = new Injection[harmonyRunOrder.Count];
        for (int i = 0; i < harmonyRunOrder.Count; i++) {
            Patch patch = harmonyRunOrder[harmonyRunOrder.Count - 1 - i];
            appendOrder[i] = Map(patch.PatchMethod, patch.owner);
        }

        return appendOrder;
    }

    private static Func<IEnumerable<Concord.CodeInstruction>, ITranspilerContext, IEnumerable<Concord.CodeInstruction>>? TryCompile(MethodInfo method) {
        if (Validate(method) != null) {
            return null;
        }

        HarmonyTranspile compiled = Bind(method);
        return (incoming, context) => Run(compiled, incoming, context);
    }

    private static HarmonyTranspile Bind(MethodInfo method) {
        ParameterExpression instructions = Expression.Parameter(typeof(IEnumerable<CodeInstruction>), "instructions");
        ParameterExpression generator = Expression.Parameter(typeof(ILGenerator), "generator");
        ParameterExpression original = Expression.Parameter(typeof(MethodBase), "original");

        ParameterInfo[] parameters = method.GetParameters();
        Expression[] arguments = new Expression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            Type type = parameters[i].ParameterType;
            arguments[i] = type == typeof(ILGenerator) ? generator : type == typeof(MethodBase) ? original : instructions;
        }

        Expression call = Expression.Call(method, arguments);
        Expression body = method.ReturnType == typeof(IEnumerable<CodeInstruction>) ? call : Expression.Convert(call, typeof(IEnumerable<CodeInstruction>));
        return Expression.Lambda<HarmonyTranspile>(body, instructions, generator, original).Compile();
    }

    private static IEnumerable<Concord.CodeInstruction> Run(HarmonyTranspile transpile, IEnumerable<Concord.CodeInstruction> incoming, ITranspilerContext context) {
        List<Concord.CodeInstruction> copy = new List<Concord.CodeInstruction>();
        foreach (Concord.CodeInstruction instruction in incoming) {
            copy.Add(new Concord.CodeInstruction(instruction));
        }

        using TranspilerScratch scratch = new TranspilerScratch();
        ILGenerator generator = scratch.Generator;
        HarmonyStreamContext outbound = new HarmonyStreamContext();
        int existing = HighestLocalSlot(copy) + 1;
        for (int slot = 0; slot < existing; slot++) {
            Concord.LocalRef local = context.GetLocal(slot);
            outbound.HarmonyLocalByRef[local] = generator.DeclareLocal(local.Type);
        }

        List<CodeInstruction> harmonyStream = CodeInstructionConverter.FromConcord(copy, outbound, generator);
        IEnumerable<CodeInstruction> produced = transpile(harmonyStream, generator, context.Original);
        if (produced == null) {
            return null!;
        }

        List<CodeInstruction> materialized = new List<CodeInstruction>(produced);
        List<Concord.CodeInstruction> outgoing = CodeInstructionConverter.ToConcord(
            materialized, new SlotMappingContext(context, existing), out _, scratch.DeclaredLocalTypes);
        ReplaceGetExecutingAssembly(outgoing, context.Original.DeclaringType);
        return outgoing;
    }

    private static int HighestLocalSlot(List<Concord.CodeInstruction> stream) {
        int highest = -1;
        foreach (Concord.CodeInstruction instruction in stream) {
            int slot = instruction.operand is Concord.LocalRef local ? local.Index : CompactLocalSlot(instruction.opcode);
            if (slot > highest) {
                highest = slot;
            }
        }

        return highest;
    }

    private static int CompactLocalSlot(OpCode opcode) {
        if (opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Stloc_0) {
            return 0;
        }

        if (opcode == OpCodes.Ldloc_1 || opcode == OpCodes.Stloc_1) {
            return 1;
        }

        if (opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Stloc_2) {
            return 2;
        }

        if (opcode == OpCodes.Ldloc_3 || opcode == OpCodes.Stloc_3) {
            return 3;
        }

        return -1;
    }

    private static void ReplaceGetExecutingAssembly(List<Concord.CodeInstruction> stream, Type? declaringType) {
        if (declaringType == null) {
            return;
        }

        for (int i = 0; i < stream.Count; i++) {
            Concord.CodeInstruction current = stream[i];
            if (current.opcode != OpCodes.Call || !Equals(current.operand, GetExecutingAssembly)) {
                continue;
            }

            Concord.CodeInstruction token = new Concord.CodeInstruction(OpCodes.Ldtoken, declaringType);
            token.labels.AddRange(current.labels);
            token.blocks.AddRange(current.blocks);
            stream[i] = token;
            stream.Insert(i + 1, new Concord.CodeInstruction(OpCodes.Call, GetTypeFromHandle));
            stream.Insert(i + 2, new Concord.CodeInstruction(OpCodes.Callvirt, TypeAssemblyGetter));
            i += 2;
        }
    }

    private static string Describe(MethodInfo method) {
        ParameterInfo[] parameters = method.GetParameters();
        string[] names = new string[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) {
            names[i] = parameters[i].ParameterType.Name + " " + parameters[i].Name;
        }

        return method.ReturnType.Name + " (" + string.Join(", ", names) + ")";
    }

    private sealed class SlotMappingContext : ITranspilerContext {
        private readonly ITranspilerContext inner;

        private readonly int existing;

        private int declared;

        internal SlotMappingContext(ITranspilerContext inner, int existing) {
            this.inner = inner;
            this.existing = existing;
        }

        public MethodBase Original => inner.Original;

        public Concord.Label DefineLabel() {
            return inner.DefineLabel();
        }

        public Concord.LocalRef DeclareLocal(Type type) {
            int slot = declared++;
            return slot < existing ? inner.GetLocal(slot) : inner.DeclareLocal(type);
        }

        public Concord.LocalRef GetLocal(int index) {
            return inner.GetLocal(index);
        }
    }
}
