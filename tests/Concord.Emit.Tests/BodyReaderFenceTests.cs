using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public struct FenceFnPtrHolder {
    public unsafe delegate*<int, int> Fp;
}

public struct FenceNestedFnPtrHolder {
    public FenceFnPtrHolder Inner;
}

public sealed class FenceFnPtrClassHolder {
    public unsafe delegate*<int, int> Fp;
}

public static class FenceTargets {
    public static int Plain(int seed) {
        int a = seed + 1;
        return a * 2;
    }

    public static unsafe int FnPtrLocal(int seed) {
        delegate*<int, int> fp = &Step;
        return fp(seed);
    }

    public static int StructParam(FenceFnPtrHolder holder, int seed) => seed;

    public static FenceFnPtrHolder StructReturn() => default;

    public static int StructLocal(int seed) {
        FenceFnPtrHolder holder = default;
        return Consume(ref holder) + seed;
    }

    public static int NestedStructLocal(int seed) {
        FenceNestedFnPtrHolder holder = default;
        return ConsumeNested(ref holder) + seed;
    }

    public static int ClassFieldLocal(int seed) {
        FenceFnPtrClassHolder holder = new FenceFnPtrClassHolder();
        return ConsumeClass(holder) + seed;
    }

    public static unsafe int PointerLocal(int seed) {
        int x = seed;
        int* p = &x;
        return *p + 1;
    }

    public static unsafe int Stackalloc(int seed) {
        int* buffer = stackalloc int[4];
        buffer[0] = seed;
        return buffer[0];
    }

    public static int TypedReferenceLocal(int seed) {
        int x = seed;
        TypedReference reference = __makeref(x);
        return __refvalue(reference, int) + 1;
    }

    public static unsafe int PinnedLocal(int[] data) {
        fixed (int* p = data) {
            return *p;
        }
    }

    public static int ByRefLocal(int seed) {
        int x = seed;
        ref int slot = ref x;
        slot++;
        return x;
    }

    private static int Step(int value) => value + 1;

    private static unsafe int Consume(ref FenceFnPtrHolder holder) => (void*)holder.Fp == null ? 0 : 1;

    private static unsafe int ConsumeNested(ref FenceNestedFnPtrHolder holder) => (void*)holder.Inner.Fp == null ? 0 : 1;

    private static unsafe int ConsumeClass(FenceFnPtrClassHolder holder) => (void*)holder.Fp == null ? 0 : 1;
}

public sealed class BodyReaderFenceTests {
    private static bool OnMono => Type.GetType("Mono.Runtime") is not null;

    public static TheoryData<string> Refused() {
        return new TheoryData<string> {
            nameof(FenceTargets.FnPtrLocal),
            nameof(FenceTargets.StructParam),
            nameof(FenceTargets.StructReturn),
            nameof(FenceTargets.StructLocal),
            nameof(FenceTargets.NestedStructLocal),
        };
    }

    public static TheoryData<string> Allowed() {
        return new TheoryData<string> {
            nameof(FenceTargets.Plain),
            nameof(FenceTargets.PointerLocal),
            nameof(FenceTargets.Stackalloc),
            nameof(FenceTargets.TypedReferenceLocal),
            nameof(FenceTargets.PinnedLocal),
            nameof(FenceTargets.ByRefLocal),
            nameof(FenceTargets.ClassFieldLocal),
        };
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void Inspect_ShapeMonoCannotReflect_NamesWhatItFound(string name) {
        string? found = BodyReader.Inspect(Target(name));

        Assert.NotNull(found);
        Assert.Contains("function pointer", found);
    }

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Inspect_ShapeMonoReadsCleanly_ReturnsNull(string name) {
        Assert.Null(BodyReader.Inspect(Target(name)));
    }

    [Fact]
    public void Inspect_ValueTypeCarryingAFunctionPointer_NamesTheValueType() {
        string? found = BodyReader.Inspect(Target(nameof(FenceTargets.StructLocal)));

        Assert.NotNull(found);
        Assert.Contains(nameof(FenceFnPtrHolder), found);
    }

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Clone_ShapeTheFenceAllows_Generates(string name) {
        Assert.NotNull(OriginalBody.Clone(Target(name)));
    }

    [MonoOnlyTheory]
    [MemberData(nameof(Refused))]
    public void Clone_ShapeTheFenceRefuses_ThrowsTheRefusalOnMono(string name) {
        ConcordEmitException failure = Assert.Throws<ConcordEmitException>(() => OriginalBody.Clone(Target(name)));

        Assert.Equal(BodyReader.RefusalCode, failure.Code);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void Reject_ShapeTheFenceRefuses_OnlyThrowsOnMono(string name) {
        if (OnMono) {
            Assert.Throws<ConcordEmitException>(() => BodyReader.Reject(Target(name)));
            return;
        }

        BodyReader.Reject(Target(name));
    }

    [Fact]
    public void Inspect_MethodEmittedAtRuntime_ReturnsNull() {
        MethodInfo emitted = OriginalBody.Clone(Target(nameof(FenceTargets.Plain)));

        Assert.Null(BodyReader.Inspect(emitted));
    }

    private static MethodInfo Target(string name) {
        return typeof(FenceTargets).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
               ?? throw new InvalidOperationException($"FenceTargets has no '{name}'.");
    }
}
