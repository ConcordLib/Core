using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;

namespace Concord.Harmony;

internal static class OriginalStubs {
  private const string StubTypePrefix = "Concord.Harmony.OriginalStubs.Stub";

  private static readonly object Gate = new object();
  private static readonly Dictionary<MethodBase, MethodInfo> Stubs = new Dictionary<MethodBase, MethodInfo>();

  private static readonly ConstructorInfo NotSupportedCtor =
    typeof(NotSupportedException).GetConstructor(new[] { typeof(string) })!;

  private static readonly ModuleBuilder Module = AssemblyBuilder
    .DefineDynamicAssembly(new AssemblyName("Concord.Harmony.OriginalStubs"), AssemblyBuilderAccess.Run)
    .DefineDynamicModule("Concord.Harmony.OriginalStubs");

  internal static MethodInfo For(MethodBase original) {
    if (original is null) {
      throw new ArgumentNullException(nameof(original));
    }

    MethodBase key = MethodIdentity.Normalize(original);
    lock (Gate) {
      if (!Stubs.TryGetValue(key, out MethodInfo? stub)) {
        stub = Mint(key, Stubs.Count);
        Stubs[key] = stub;
      }

      return stub;
    }
  }

  private static MethodInfo Mint(MethodBase original, int ordinal) {
    ParameterInfo[] parameters = original.GetParameters();
    int offset = original.IsStatic ? 0 : 1;
    Type[] parameterTypes = new Type[parameters.Length + offset];
    if (offset == 1) {
      Type declaring = original.DeclaringType!;
      parameterTypes[0] = Closed(declaring.IsValueType ? declaring.MakeByRefType() : declaring);
    }

    for (int i = 0; i < parameters.Length; i++) {
      parameterTypes[i + offset] = Closed(parameters[i].ParameterType);
    }

    Type returnType = original is MethodInfo method ? Closed(method.ReturnType) : typeof(void);
    string name = (original.DeclaringType?.FullName ?? "GLOBALTYPE") + "." + original.Name + "_ConcordStub";

    TypeBuilder type = Module.DefineType(StubTypePrefix + ordinal, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
    MethodBuilder stub = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, parameterTypes);
    if (offset == 1) {
      stub.DefineParameter(1, ParameterAttributes.None, "this");
    }

    for (int i = 0; i < parameters.Length; i++) {
      stub.DefineParameter(i + offset + 1, parameters[i].Attributes, parameters[i].Name);
    }

    ILGenerator il = stub.GetILGenerator();
    il.Emit(OpCodes.Ldstr, "Concord owns " + name + ". Harmony handed this MethodInfo back as the patch result, but Concord built the method body, so there is nothing here to run. Call the original method instead.");
    il.Emit(OpCodes.Newobj, NotSupportedCtor);
    il.Emit(OpCodes.Throw);

    Type created = type.CreateTypeInfo()!.AsType();
    return created.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
  }

  private static Type Closed(Type type) {
    if (!type.ContainsGenericParameters) {
      return type;
    }

    return type.IsByRef ? typeof(object).MakeByRefType() : typeof(object);
  }
}
