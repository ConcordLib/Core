using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Concord.Emit;

/// <summary>
///     Concord's reverse patch: a standalone delegate onto a target body, private targets included.
/// </summary>
public static class ReversePatchFactory {
    /// <summary>
    ///     Answers whether Concord currently owns a method's body. Null until a detour backend wires it.
    /// </summary>
    public static Func<MethodBase, bool>? OwnsBody { get; set; }

    /// <summary>
    ///     Creates a delegate onto a target body. Instance methods take the instance as the first parameter.
    /// </summary>
    /// <param name="original">The target method.</param>
    /// <param name="delegateType">The delegate type to create.</param>
    /// <returns>The body Concord runs now when it owns the method, else a clone of the original that bypasses every patch.</returns>
    public static Delegate Bind(MethodBase original, Type delegateType) {
        MethodInfo body = OwnsBody?.Invoke(original) == true ? LiveBody(original) : OriginalBody.Clone(original);
        return body.CreateDelegate(delegateType);
    }

    private static MethodInfo LiveBody(MethodBase original) {
        Type[] parameterTypes = WrapperComposer.ResolveParameterTypes(original);
        Type returnType = WrapperComposer.ResolveReturnType(original);
        using DynamicMethodDefinition dmd = new DynamicMethodDefinition("<ReversePatch>" + original.Name, returnType, parameterTypes);
        ILProcessor il = dmd.GetILProcessor();
        foreach (ParameterDefinition parameter in dmd.Definition.Parameters) {
            il.Emit(OpCodes.Ldarg, parameter);
        }

        il.Emit(OpCodes.Call, dmd.Module.ImportReference(original));
        il.Emit(OpCodes.Ret);
        return dmd.Generate();
    }
}
