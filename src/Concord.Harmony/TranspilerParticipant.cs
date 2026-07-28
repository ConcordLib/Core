#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;

namespace Concord.Harmony;

using CodeInstruction = HarmonyLib.CodeInstruction;

internal static class TranspilerParticipant {
    internal static readonly BridgeTargetRegistry Registry = new BridgeTargetRegistry();

    internal static readonly MethodInfo TranspileMethod = typeof(TranspilerParticipant).GetMethod(nameof(Transpile), BindingFlags.NonPublic | BindingFlags.Static);

    // [ThreadStatic] is only valid on a field, so this one cannot become a property.
#pragma warning disable SA1401
    [ThreadStatic]
    internal static Exception LastStreamFailure;
#pragma warning restore SA1401

    internal static Action<string> Log { get; set; }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original, ILGenerator generator) {
        if (original == null) {
            return instructions;
        }

        Injection[] ordered = Registry.OrderedSnapshot(MethodIdentity.Normalize(original));
        if (ordered.Length == 0) {
            return instructions;
        }

        List<CodeInstruction> stream = new List<CodeInstruction>(instructions);
        try {
            MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(original);
            Concord.ITranspilerContext context = WrapperComposer.CreateStreamContext(resolved);
            List<Concord.CodeInstruction> incoming = CodeInstructionConverter.ToConcord(stream, context, out HarmonyStreamContext harmonyContext);
            List<Concord.CodeInstruction> outgoing = WrapperComposer.TransformStream(resolved, incoming, ordered, context);
            return CodeInstructionConverter.FromConcord(outgoing, harmonyContext, generator);
        } catch (ConcordEmitException ex) {
            LastStreamFailure = ex;
            Log?.Invoke(CoexistenceLogMarkers.StreamRejected + " " + original.Name + ": " + ex.Message);
            return instructions;
        }
    }
}
