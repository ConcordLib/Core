using System.Reflection;
using MonoMod.Utils;

namespace Concord.Emit.Tests;

public static class StreamRoundTrip {
    public static MethodInfo Generate(IReadOnlyList<CodeInstruction> instructions, MethodBase shapedLike) {
        MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(shapedLike);
        Type returnType = WrapperComposer.ResolveReturnType(resolved);
        Type[] parameterTypes = WrapperComposer.ResolveParameterTypes(resolved);
        using DynamicMethodDefinition wrapper = new DynamicMethodDefinition("streamroundtrip_" + resolved.Name, returnType, parameterTypes);
        TranspilerContext context = new TranspilerContext(resolved);
        DeclareReferencedLocals(instructions, context);
        CecilCodeConverter.WriteBack(wrapper.Definition, instructions, context);
        return wrapper.Generate();
    }

    // A composed stream references its locals purely by index/type on each LocalRef operand; it
    // carries no separate locals table the way a NeutralBody does. Reconstructing a real body to
    // execute means re-declaring every referenced index densely from 0, exactly as a caller is
    // expected to do before feeding TransformStream's output onward.
    private static void DeclareReferencedLocals(IReadOnlyList<CodeInstruction> instructions, TranspilerContext context) {
        Dictionary<int, Type> typesByIndex = new Dictionary<int, Type>();
        int maxIndex = -1;
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.operand is LocalRef local) {
                typesByIndex[local.Index] = local.Type;
                maxIndex = Math.Max(maxIndex, local.Index);
            }
        }

        for (int i = 0; i <= maxIndex; i++) {
            context.DeclareLocal(typesByIndex.TryGetValue(i, out Type? type) ? type : typeof(object));
        }
    }
}
