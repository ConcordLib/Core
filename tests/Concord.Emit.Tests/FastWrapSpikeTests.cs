using System;
using System.Linq.Expressions;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;

namespace Concord.Emit.Tests;

public static class FastWrapSlots {
    public static readonly Delegate?[] Slots = new Delegate?[8];

    public static readonly List<string> Log = [];
}

public static class FastWrapSpikeTargets {
    public static int Add(int a, int b) {
        return a + b;
    }

    public static string Concat(string a, int b) {
        return a + b;
    }
}

public sealed class FastWrapSpikeTests {
    [Fact]
    public void FixedFormWrapper_CallsTheClonedOriginalThroughARootedDelegate() {
        MethodInfo wrapper = BuildFixedFormWrapper(typeof(FastWrapSpikeTargets).GetMethod(nameof(FastWrapSpikeTargets.Add))!, 0);
        FastWrapSlots.Log.Clear();

        Assert.Equal(7, wrapper.Invoke(null, [3, 4]));
        Assert.Equal(["enter", "exit"], FastWrapSlots.Log);
    }

    [Fact]
    public void FixedFormWrapper_HandlesReferenceTypesAndRunsExitOnAThrow() {
        MethodInfo wrapper = BuildFixedFormWrapper(typeof(FastWrapSpikeTargets).GetMethod(nameof(FastWrapSpikeTargets.Concat))!, 1);
        FastWrapSlots.Log.Clear();

        Assert.Equal("a1", wrapper.Invoke(null, ["a", 1]));
        Assert.Equal(["enter", "exit"], FastWrapSlots.Log);
    }

    private static MethodInfo BuildFixedFormWrapper(MethodBase target, int slot) {
        MethodInfo clone = OriginalBody.Clone(target);

        Type[] parameterTypes = WrapperComposer.ResolveParameterTypes(target);
        Type returnType = WrapperComposer.ResolveReturnType(target);
        Type delegateType = Expression.GetDelegateType([.. parameterTypes, returnType]);
        FastWrapSlots.Slots[slot] = clone.CreateDelegate(delegateType);

        using DynamicMethodDefinition wrapper = new DynamicMethodDefinition("fastwrap", returnType, parameterTypes);
        MethodDefinition definition = wrapper.Definition;
        ModuleDefinition module = definition.Module;
        ILProcessor il = definition.Body.GetILProcessor();

        MethodInfo log = typeof(List<string>).GetMethod(nameof(List<string>.Add))!;
        FieldInfo logField = typeof(FastWrapSlots).GetField(nameof(FastWrapSlots.Log))!;
        FieldInfo slotsField = typeof(FastWrapSlots).GetField(nameof(FastWrapSlots.Slots))!;

        il.Emit(OpCodes.Ldsfld, module.ImportReference(logField));
        il.Emit(OpCodes.Ldstr, "enter");
        il.Emit(OpCodes.Callvirt, module.ImportReference(log));

        il.Emit(OpCodes.Ldsfld, module.ImportReference(slotsField));
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, module.ImportReference(delegateType));
        for (int i = 0; i < parameterTypes.Length; i++) {
            il.Emit(OpCodes.Ldarg, i);
        }

        il.Emit(OpCodes.Callvirt, module.ImportReference(delegateType.GetMethod("Invoke")!));

        VariableDefinition result = new VariableDefinition(module.ImportReference(returnType));
        definition.Body.Variables.Add(result);
        definition.Body.InitLocals = true;
        il.Emit(OpCodes.Stloc, result);

        il.Emit(OpCodes.Ldsfld, module.ImportReference(logField));
        il.Emit(OpCodes.Ldstr, "exit");
        il.Emit(OpCodes.Callvirt, module.ImportReference(log));

        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        return wrapper.Generate();
    }
}
