using System.Reflection.Emit;

namespace Concord.Host.Adapter.Tests;

internal static class Bodies {
    internal static IReadOnlyList<ComposedInstruction> Wrap(IReadOnlyList<CodeInstruction> stream) {
        ComposedInstruction[] wrapped = new ComposedInstruction[stream.Count];
        for (int index = 0; index < stream.Count; index++) {
            wrapped[index] = new ComposedInstruction(stream[index], null, null);
        }

        return wrapped;
    }

    internal static ComposedBody Of(params CodeInstruction[] stream) {
        return new ComposedBody(Wrap(stream), [], [], false);
    }

    internal static ComposedBody Guarded(CodeInstruction[] stream, ClauseKind kind) {
        ComposedClause clause = new ComposedClause(
            kind,
            0,
            1,
            1,
            stream.Length,
            kind == ClauseKind.Catch ? typeof(Exception) : null,
            kind == ClauseKind.Filter ? 1 : -1);

        return new ComposedBody(Wrap(stream), [], [clause], false);
    }

    internal static ComposedBody Pushes(int count) {
        CodeInstruction[] stream = new CodeInstruction[count + 1];
        for (int index = 0; index < count; index++) {
            stream[index] = new CodeInstruction(OpCodes.Ldc_I4_0);
        }

        stream[count] = new CodeInstruction(OpCodes.Ret);
        return Of(stream);
    }
}
