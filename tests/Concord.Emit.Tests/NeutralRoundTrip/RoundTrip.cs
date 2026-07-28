using System.Collections.Generic;
using System.Reflection;
using Concord.Emit;
using MonoMod.Utils;

namespace Concord.Emit.Tests.NeutralRoundTrip;

public static class RoundTrip {
    public static MethodInfo ComposeThroughNeutralBody(MethodBase target, IReadOnlyList<Injection> ordered) {
        NeutralBody supplied = BodyTransformer.FromMethod(target);
        NeutralBody composed = BodyTransformer.Transform(target, supplied, ordered);
        return Generate(composed, target);
    }

    public static NeutralBody Extract(MethodBase target) {
        MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(target);
        using DynamicMethodDefinition source = new DynamicMethodDefinition(resolved);
        return CecilToNeutralConverter.Convert(source.Definition);
    }

    public static MethodInfo Generate(NeutralBody body, MethodBase shapedLike) {
        MethodBase resolved = WrapperComposer.ResolveStateMachineTarget(shapedLike);
        using DynamicMethodDefinition wrapper = new DynamicMethodDefinition("roundtrip_" + resolved.Name, ResolveReturnType(resolved), ResolveParameterTypes(resolved));
        NeutralToCecilConverter.Populate(body, wrapper.Definition);
        return wrapper.Generate();
    }

    private static Type ResolveReturnType(MethodBase resolved) {
        return resolved is MethodInfo methodInfo ? methodInfo.ReturnType : typeof(void);
    }

    private static Type[] ResolveParameterTypes(MethodBase resolved) {
        ParameterInfo[] parameters = resolved.GetParameters();
        bool hasThis = !resolved.IsStatic;
        Type[] types = new Type[parameters.Length + (hasThis ? 1 : 0)];
        int offset = 0;
        if (hasThis) {
            types[0] = resolved.DeclaringType!.IsValueType ? resolved.DeclaringType.MakeByRefType() : resolved.DeclaringType!;
            offset = 1;
        }

        for (int i = 0; i < parameters.Length; i++) {
            types[i + offset] = parameters[i].ParameterType;
        }

        return types;
    }
}
