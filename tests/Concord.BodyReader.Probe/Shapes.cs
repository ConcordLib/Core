using System.Reflection;
using System.Reflection.Emit;

namespace Concord.BodyReader.Probe;

public static class PlainShape {
    public static int M(int seed) {
        int a = seed + 1;
        return a * 2;
    }
}

public static class FnPtrLocalShape {
    public static unsafe int M(int seed) {
        delegate*<int, int> fp = &Step;
        return fp(seed);
    }

    private static int Step(int value) => value + 1;
}

public static class FnPtrParamShape {
    public static unsafe int M(delegate*<int, int> fp, int seed) => fp(seed);
}

public static class FnPtrReturnShape {
    public static unsafe delegate*<int, int> M() => &Step;

    private static int Step(int value) => value + 1;
}

public static class FnPtrArrayLocalShape {
    public static unsafe int M(int seed) {
        delegate*<int, int>[] table = new delegate*<int, int>[1];
        table[0] = &Step;
        return table[0](seed);
    }

    private static int Step(int value) => value + 1;
}

public static class FnPtrPointerLocalShape {
    public static unsafe int M(int seed) {
        delegate*<int, int> fp = &Step;
        delegate*<int, int>* indirect = &fp;
        return indirect[0](seed);
    }

    private static int Step(int value) => value + 1;
}

public static class FnPtrPinnedLocalShape {
    public static unsafe int M(int seed) {
        delegate*<int, int>[] table = new delegate*<int, int>[1];
        table[0] = &Step;
        fixed (delegate*<int, int>* pinned = table) {
            return pinned[0](seed);
        }
    }

    private static int Step(int value) => value + 1;
}

public static class UnmanagedCallConvFnPtrShape {
    public static unsafe int M(int seed) {
        delegate* unmanaged[Cdecl]<int, int> fp = (delegate* unmanaged[Cdecl]<int, int>)(void*)0;
        void* raw = fp;
        return raw == null ? seed : 0;
    }
}

public static class CalliNoFnPtrLocalShape {
    public static unsafe int M(void* raw, int seed) => ((delegate*<int, int>)raw)(seed);
}

public struct FnPtrHolder {
    public unsafe delegate*<int, int> Fp;
}

public static class FnPtrFieldShape {
    public static unsafe int M(ref FnPtrHolder holder, int seed) => holder.Fp(seed);
}

public sealed class FnPtrClassHolder {
    public unsafe delegate*<int, int> Fp;
}

public static class FnPtrClassFieldLocalShape {
    public static int M(int seed) {
        FnPtrClassHolder holder = new FnPtrClassHolder();
        return Consume(holder) + seed;
    }

    private static unsafe int Consume(FnPtrClassHolder holder) => (void*)holder.Fp == null ? 0 : 1;
}

public struct NestedFnPtrHolder {
    public FnPtrHolder Inner;
}

public static class NestedFnPtrStructLocalShape {
    public static int M(int seed) {
        NestedFnPtrHolder holder = default;
        return Consume(ref holder) + seed;
    }

    private static unsafe int Consume(ref NestedFnPtrHolder holder) => (void*)holder.Inner.Fp == null ? 0 : 1;
}

public static class FnPtrStructByValParamShape {
    public static int M(FnPtrHolder holder, int seed) => seed;
}

public static class FnPtrStructReturnShape {
    public static FnPtrHolder M() => default;
}

public static class FnPtrStructLocalShape {
    public static int M(int seed) {
        FnPtrHolder holder = default;
        return Consume(ref holder) + seed;
    }

    private static unsafe int Consume(ref FnPtrHolder holder) => (void*)holder.Fp == null ? 0 : 1;
}

public static class PointerLocalShape {
    public static unsafe int M(int seed) {
        int x = seed;
        int* p = &x;
        return *p + 1;
    }
}

public static class PointerParamShape {
    public static unsafe int M(int* p) => *p;
}

public static class PointerReturnShape {
    public static unsafe int* M(int* p) => p + 1;
}

public static class VoidPointerLocalShape {
    public static unsafe int M(int seed) {
        int x = seed;
        void* p = &x;
        return *(int*)p;
    }
}

public static class ByRefLocalShape {
    public static int M(int seed) {
        int x = seed;
        ref int slot = ref x;
        slot++;
        return x;
    }
}

public ref struct Scratch {
    public int Value;
}

public static class RefStructLocalShape {
    public static int M(int seed) {
        Scratch scratch = default;
        scratch.Value = seed + 1;
        return scratch.Value;
    }
}

public static class PinnedLocalShape {
    public static unsafe int M(int[] data) {
        fixed (int* p = data) {
            return *p;
        }
    }
}

public static class StackallocShape {
    public static unsafe int M(int seed) {
        int* buffer = stackalloc int[4];
        buffer[0] = seed;
        return buffer[0];
    }
}

public static class TypedReferenceLocalShape {
    public static int M(int seed) {
        int x = seed;
        TypedReference reference = __makeref(x);
        return __refvalue(reference, int) + 1;
    }
}

public static class ArglistShape {
    public static int M(int first, __arglist) => first + 1;
}

public static class ArgIteratorLocalShape {
    public static int M(int first, __arglist) {
        ArgIterator iterator = new ArgIterator(__arglist);
        int total = first;
        while (iterator.GetRemainingCount() > 0) {
            total += __refvalue(iterator.GetNextArg(), int);
        }

        return total;
    }
}

public static class ModreqInParamShape {
    public static int M(in int value) => value + 1;
}

public static class ModreqRefReadonlyReturnShape {
    private static int store = 3;

    public static ref readonly int M() => ref store;
}

public static class VolatileModreqShape {
    private static volatile int flag;

    public static int M(int seed) {
        flag = seed;
        return flag + 1;
    }
}

public static class GenericPointerLocalShape {
    public static unsafe int M<T>(T value)
        where T : unmanaged {
        T* p = &value;
        return sizeof(T) ^ (*p).GetHashCode();
    }
}

public static class GenericFnPtrLocalShape {
    public static unsafe int M<T>(T value) {
        delegate*<T, int> fp = &Step<T>;
        return fp(value);
    }

    private static int Step<T>(T value) => value is null ? 0 : 1;
}

public static class GenericValueTupleTryCatchShape {
    public static int M(int seed) {
        (int First, int Second) pair = (seed, seed + 1);
        try {
            return pair.First + pair.Second;
        } catch (InvalidOperationException) {
            return pair.Second;
        }
    }
}

public static class DynamicAssemblyLocalShape {
    public static MethodBase Build() {
        AssemblyName name = new AssemblyName("Concord.BodyReader.Probe.Dynamic");
#if NETFRAMEWORK
        AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
#else
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
#endif
        ModuleBuilder module = assembly.DefineDynamicModule("Main");
        TypeBuilder payload = module.DefineType("Payload", TypeAttributes.Public | TypeAttributes.Class);
        Type payloadType = payload.CreateType()!;

        TypeBuilder host = module.DefineType("Host", TypeAttributes.Public | TypeAttributes.Class);
        MethodBuilder method = host.DefineMethod("M", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        ILGenerator il = method.GetILGenerator();
        il.DeclareLocal(payloadType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        Type hostType = host.CreateType()!;
        return hostType.GetMethod("M")!;
    }
}
