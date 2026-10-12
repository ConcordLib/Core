using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public struct SampleValue {
    public int X { get; set; }
}

public class SampleReference {
    public int X { get; set; }
}

public enum SampleEnum {
    None,
}

public sealed class SampleGeneric<T> {
    public T? Value { get; set; }

    public void Method<TOther>(TOther value) {
    }
}

public sealed class StubTokenSource : ITokenSource {
    private readonly Dictionary<Type, int> assigned = new Dictionary<Type, int>();
    private int nextRow = 1;

    public StubTokenSource(int tableByte = 0x01) {
        TableByte = tableByte;
    }

    public int TableByte { get; }

    public List<Type> Asked { get; } = new List<Type>();

    public int TokenFor(Type type) {
        Asked.Add(type);

        if (!assigned.TryGetValue(type, out int token)) {
            token = (TableByte << 24) | nextRow;
            nextRow++;
            assigned[type] = token;
        }

        return token;
    }

    public int Coded(Type type) {
        int row = assigned[type] & 0x00FFFFFF;
        return (row << 2) | (TableByte == 0x02 ? 0 : 1);
    }

    public int TokenFor(FieldInfo field) => throw new NotSupportedException();

    public int TokenFor(MethodBase method) => throw new NotSupportedException();

    public int TokenFor(string literal) => throw new NotSupportedException();

    public int TokenForLocals(byte[] signature) => throw new NotSupportedException();
}

public sealed class NilTokenSource : ITokenSource {
    public int TokenFor(Type type) => 0x02000000;

    public int TokenFor(FieldInfo field) => throw new NotSupportedException();

    public int TokenFor(MethodBase method) => throw new NotSupportedException();

    public int TokenFor(string literal) => throw new NotSupportedException();

    public int TokenForLocals(byte[] signature) => throw new NotSupportedException();
}

public sealed class TypeSpecTokenSource : ITokenSource {
    public int TokenFor(Type type) => 0x1B000005;

    public int TokenFor(FieldInfo field) => throw new NotSupportedException();

    public int TokenFor(MethodBase method) => throw new NotSupportedException();

    public int TokenFor(string literal) => throw new NotSupportedException();

    public int TokenForLocals(byte[] signature) => throw new NotSupportedException();
}

public class LocalSignatureEncoderTests {
    private const byte LocalSig = 0x07;
    private const byte Pinned = 0x45;
    private const byte ByRef = 0x10;
    private const byte Ptr = 0x0F;
    private const byte SzArray = 0x1D;
    private const byte MdArray = 0x14;
    private const byte Struct = 0x11;
    private const byte Class = 0x12;
    private const byte GenericInst = 0x15;
    private const byte Var = 0x13;
    private const byte MVar = 0x1E;

    private static byte[] Encode(ITokenSource tokens, params ComposedLocal[] locals) =>
        LocalSignatureEncoder.Encode(locals, tokens);

    private static byte[] EncodeTypes(ITokenSource tokens, params Type[] types) =>
        LocalSignatureEncoder.Encode(types.Select(type => new ComposedLocal(type, false)).ToArray(), tokens);

    private static Type MethodParameter() =>
        typeof(SampleGeneric<int>).GetMethod(nameof(SampleGeneric<int>.Method))!.GetGenericArguments()[0];

    private static Type TypeParameter() =>
        typeof(SampleGeneric<>).GetGenericArguments()[0];

    [Fact]
    public void EveryPrimitiveGetsItsEcmaElementType() {
        Type[] types = [
            typeof(bool), typeof(char), typeof(sbyte), typeof(byte),
            typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double),
            typeof(nint), typeof(nuint), typeof(string), typeof(object),
        ];

        byte[] actual = EncodeTypes(new StubTokenSource(), types);

        Assert.Equal(
            new byte[] {
                LocalSig, 0x10,
                0x02, 0x03, 0x04, 0x05,
                0x06, 0x07, 0x08, 0x09,
                0x0A, 0x0B, 0x0C, 0x0D,
                0x18, 0x19, 0x0E, 0x1C,
            },
            actual);
    }

    [Fact]
    public void AValueTypeIsValueTypeAndAReferenceTypeIsClass() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] actual = EncodeTypes(tokens, typeof(SampleValue), typeof(SampleReference));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x02,
                Struct, (byte)tokens.Coded(typeof(SampleValue)),
                Class, (byte)tokens.Coded(typeof(SampleReference)),
            },
            actual);
    }

    [Fact]
    public void EnumIsAValueTypeButSystemEnumAndSystemValueTypeAreNot() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] actual = EncodeTypes(tokens, typeof(SampleEnum), typeof(Enum), typeof(ValueType));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x03,
                Struct, (byte)tokens.Coded(typeof(SampleEnum)),
                Class, (byte)tokens.Coded(typeof(Enum)),
                Class, (byte)tokens.Coded(typeof(ValueType)),
            },
            actual);
    }

    [Fact]
    public void ANaiveValueTypeTestWouldGetSystemEnumWrong() {
        Assert.True(typeof(Enum).IsSubclassOf(typeof(ValueType)));
        Assert.False(typeof(Enum).IsValueType);
    }

    [Fact]
    public void ATypeDefTokenEncodesWithTheTypeDefTag() {
        StubTokenSource tokens = new StubTokenSource(0x02);

        byte[] actual = EncodeTypes(tokens, typeof(SampleReference));

        Assert.Equal(new byte[] { LocalSig, 0x01, Class, (byte)tokens.Coded(typeof(SampleReference)) }, actual);
    }

    [Fact]
    public void SingleDimensionArraysAreSzArrayAndJaggedArraysNest() {
        byte[] actual = EncodeTypes(new StubTokenSource(), typeof(int[]), typeof(int[][]));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x02,
                SzArray, 0x08,
                SzArray, SzArray, 0x08,
            },
            actual);
    }

    [Fact]
    public void MultiDimensionArraysCarryAnArrayShape() {
        byte[] actual = EncodeTypes(new StubTokenSource(), typeof(int[,]), typeof(int[,,]));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x02,
                MdArray, 0x08, 0x02, 0x00, 0x02, 0x00, 0x00,
                MdArray, 0x08, 0x03, 0x00, 0x03, 0x00, 0x00, 0x00,
            },
            actual);
    }

    [Fact]
    public void ARankOneNonVectorArrayIsStillAnArrayShapeNotAnSzArray() {
        byte[] actual = EncodeTypes(new StubTokenSource(), typeof(int).MakeArrayType(1));

        Assert.Equal(new byte[] { LocalSig, 0x01, MdArray, 0x08, 0x01, 0x00, 0x01, 0x00 }, actual);
    }

    [Fact]
    public void ByRefPrefixesTheElementType() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] actual = EncodeTypes(tokens, typeof(int).MakeByRefType(), typeof(SampleValue).MakeByRefType());

        Assert.Equal(
            new byte[] {
                LocalSig, 0x02,
                ByRef, 0x08,
                ByRef, Struct, (byte)tokens.Coded(typeof(SampleValue)),
            },
            actual);
    }

    [Fact]
    public unsafe void PointersNestAndVoidPointerIsPointerToVoid() {
        byte[] actual = EncodeTypes(new StubTokenSource(), typeof(int*), typeof(void*), typeof(int**), typeof(int*[]));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x04,
                Ptr, 0x08,
                Ptr, 0x01,
                Ptr, Ptr, 0x08,
                SzArray, Ptr, 0x08,
            },
            actual);
    }

    [Fact]
    public void PinnedWritesTheConstraintBeforeTheTypeAndBeforeByRef() {
        byte[] actual = Encode(
            new StubTokenSource(),
            new ComposedLocal(typeof(int[]), true),
            new ComposedLocal(typeof(char).MakeByRefType(), true),
            new ComposedLocal(typeof(object), true));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x03,
                Pinned, SzArray, 0x08,
                Pinned, ByRef, 0x03,
                Pinned, 0x1C,
            },
            actual);
    }

    [Fact]
    public void PinnedAndUnpinnedSlotsOfTheSameTypeDifferByExactlyTheConstraintByte() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] unpinned = Encode(tokens, new ComposedLocal(typeof(int[]), false));
        byte[] pinned = Encode(tokens, new ComposedLocal(typeof(int[]), true));

        Assert.Equal(new byte[] { LocalSig, 0x01, SzArray, 0x08 }, unpinned);
        Assert.Equal(new byte[] { LocalSig, 0x01, Pinned, SzArray, 0x08 }, pinned);
        Assert.Equal(unpinned.Length + 1, pinned.Length);
    }

    [Fact]
    public void ClosedGenericInstantiationsEncodeStructurally() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] actual = EncodeTypes(
            tokens,
            typeof(List<int>),
            typeof(KeyValuePair<int, string>),
            typeof(Dictionary<string, SampleValue>));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x03,
                GenericInst, Class, (byte)tokens.Coded(typeof(List<>)), 0x01, 0x08,
                GenericInst, Struct, (byte)tokens.Coded(typeof(KeyValuePair<,>)), 0x02, 0x08, 0x0E,
                GenericInst, Class, (byte)tokens.Coded(typeof(Dictionary<,>)), 0x02, 0x0E, Struct, (byte)tokens.Coded(typeof(SampleValue)),
            },
            actual);
    }

    [Fact]
    public void NestedGenericInstantiationsRecurse() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] actual = EncodeTypes(tokens, typeof(List<List<int>>), typeof(List<Dictionary<string, List<int>>>));

        byte list = (byte)tokens.Coded(typeof(List<>));
        byte dictionary = (byte)tokens.Coded(typeof(Dictionary<,>));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x02,
                GenericInst, Class, list, 0x01, GenericInst, Class, list, 0x01, 0x08,
                GenericInst, Class, list, 0x01, GenericInst, Class, dictionary, 0x02, 0x0E, GenericInst, Class, list, 0x01, 0x08,
            },
            actual);
    }

    [Fact]
    public void NullableEncodesAsAValueTypeInstantiation() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] actual = EncodeTypes(tokens, typeof(int?));

        Assert.Equal(
            new byte[] { LocalSig, 0x01, GenericInst, Struct, (byte)tokens.Coded(typeof(Nullable<>)), 0x01, 0x08 },
            actual);
    }

    [Fact]
    public void OnlyTheGenericDefinitionIsAskedForAToken() {
        StubTokenSource tokens = new StubTokenSource();

        EncodeTypes(tokens, typeof(Dictionary<string, List<SampleValue>>));

        Assert.Equal(
            new[] { typeof(Dictionary<,>), typeof(List<>), typeof(SampleValue) },
            tokens.Asked);
    }

    [Fact]
    public void ATypeParameterIsVarAndAMethodTypeParameterIsMVar() {
        StubTokenSource tokens = new StubTokenSource();
        Type typeParameter = TypeParameter();
        Type methodParameter = MethodParameter();

        byte[] actual = EncodeTypes(
            tokens,
            typeParameter,
            methodParameter,
            typeof(List<>).MakeGenericType(typeParameter),
            typeof(List<>).MakeGenericType(methodParameter),
            typeParameter.MakeArrayType(),
            methodParameter.MakeArrayType());

        byte list = (byte)tokens.Coded(typeof(List<>));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x06,
                Var, 0x00,
                MVar, 0x00,
                GenericInst, Class, list, 0x01, Var, 0x00,
                GenericInst, Class, list, 0x01, MVar, 0x00,
                SzArray, Var, 0x00,
                SzArray, MVar, 0x00,
            },
            actual);
    }

    [Fact]
    public void AVarAndAnMVarAtTheSamePositionDoNotEncodeTheSame() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] typeParameter = EncodeTypes(tokens, TypeParameter());
        byte[] methodParameter = EncodeTypes(tokens, MethodParameter());

        Assert.Equal(0, TypeParameter().GenericParameterPosition);
        Assert.Equal(0, MethodParameter().GenericParameterPosition);
        Assert.NotEqual(typeParameter, methodParameter);
    }

    [Fact]
    public void GenericParameterPositionIsCarriedThrough() {
        StubTokenSource tokens = new StubTokenSource();
        Type second = typeof(Dictionary<,>).GetGenericArguments()[1];

        byte[] actual = EncodeTypes(tokens, second);

        Assert.Equal(new byte[] { LocalSig, 0x01, Var, 0x01 }, actual);
    }

    [Fact]
    public void GenericParametersAreNeverAskedForAToken() {
        StubTokenSource tokens = new StubTokenSource();

        EncodeTypes(tokens, TypeParameter(), MethodParameter(), typeof(List<>).MakeGenericType(TypeParameter()));

        Assert.Equal(new[] { typeof(List<>) }, tokens.Asked);
    }

    [Fact]
    public void TypedReferenceIsItsOwnElementType() {
        byte[] actual = EncodeTypes(new StubTokenSource(), typeof(TypedReference));

        Assert.Equal(new byte[] { LocalSig, 0x01, 0x16 }, actual);
    }

    [Fact]
    public void ARealisticMixedBodyEncodesEndToEnd() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] actual = Encode(
            tokens,
            new ComposedLocal(typeof(int), false),
            new ComposedLocal(typeof(List<string>), false),
            new ComposedLocal(typeof(SampleValue), false),
            new ComposedLocal(typeof(int[]), true),
            new ComposedLocal(typeof(int).MakeByRefType(), false));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x05,
                0x08,
                GenericInst, Class, (byte)tokens.Coded(typeof(List<>)), 0x01, 0x0E,
                Struct, (byte)tokens.Coded(typeof(SampleValue)),
                Pinned, SzArray, 0x08,
                ByRef, 0x08,
            },
            actual);
    }

    [Fact]
    public void ALocalCountOverAByteCompressesToTwoBytes() {
        ComposedLocal[] locals = Enumerable.Repeat(new ComposedLocal(typeof(int), false), 200).ToArray();

        byte[] actual = LocalSignatureEncoder.Encode(locals, new StubTokenSource());

        Assert.Equal(new byte[] { LocalSig, 0x80, 0xC8 }, actual.Take(3).ToArray());
        Assert.Equal(3 + 200, actual.Length);
    }

    [Fact]
    public void AnEmptyLocalListIsRejected() {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => LocalSignatureEncoder.Encode(Array.Empty<ComposedLocal>(), new StubTokenSource()));

        Assert.Contains("at least one local", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreLocalsThanEcmaAllowsIsRejected() {
        ComposedLocal[] locals = Enumerable.Repeat(new ComposedLocal(typeof(int), false), LocalSignatureEncoder.MaxLocals + 1).ToArray();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => LocalSignatureEncoder.Encode(locals, new StubTokenSource()));

        Assert.Contains("exceeds the ECMA-335 limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLargestAllowedLocalCountStillEncodes() {
        ComposedLocal[] locals = Enumerable.Repeat(new ComposedLocal(typeof(int), false), LocalSignatureEncoder.MaxLocals).ToArray();

        byte[] actual = LocalSignatureEncoder.Encode(locals, new StubTokenSource());

        Assert.Equal(new byte[] { LocalSig, 0xC0, 0x00, 0xFF, 0xFE }, actual.Take(5).ToArray());
        Assert.Equal(5 + LocalSignatureEncoder.MaxLocals, actual.Length);
    }

    [Fact]
    public void AnOpenGenericDefinitionEncodesOverItsOwnParameters() {
        StubTokenSource tokens = new StubTokenSource();

        byte[] actual = EncodeTypes(tokens, typeof(HashSet<>), typeof(Dictionary<,>), typeof(KeyValuePair<,>));

        Assert.Equal(
            new byte[] {
                LocalSig, 0x03,
                GenericInst, Class, (byte)tokens.Coded(typeof(HashSet<>)), 0x01, Var, 0x00,
                GenericInst, Class, (byte)tokens.Coded(typeof(Dictionary<,>)), 0x02, Var, 0x00, Var, 0x01,
                GenericInst, Struct, (byte)tokens.Coded(typeof(KeyValuePair<,>)), 0x02, Var, 0x00, Var, 0x01,
            },
            actual);
    }

    [Fact]
    public void BareVoidIsRejected() {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => EncodeTypes(new StubTokenSource(), typeof(void)));

        Assert.Contains("pointer target", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullLocalTypeIsRejected() {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Encode(new StubTokenSource(), new ComposedLocal(null!, false)));

        Assert.Contains("null type", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APinnedTypedReferenceIsRejected() {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Encode(new StubTokenSource(), new ComposedLocal(typeof(TypedReference), true)));

        Assert.Contains("PINNED", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeSpecTokenIsRejectedInTypeDefOrRefPosition() {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => EncodeTypes(new TypeSpecTokenSource(), typeof(SampleReference)));

        Assert.Contains("local slot 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("encoded structurally", error.Message, StringComparison.Ordinal);
        Assert.Contains("0x1b000005", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANilTokenIsRejected() {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => EncodeTypes(new NilTokenSource(), typeof(SampleReference)));

        Assert.Contains("local slot 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("nil token 0x02000000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSlotIndexIsNamedInTheRejection() {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => EncodeTypes(new StubTokenSource(), typeof(int), typeof(int), typeof(void)));

        Assert.Contains("local slot 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullArgumentsAreRejected() {
        Assert.Throws<ArgumentNullException>(() => LocalSignatureEncoder.Encode(null!, new StubTokenSource()));
        Assert.Throws<ArgumentNullException>(() => LocalSignatureEncoder.Encode([new ComposedLocal(typeof(int), false)], null!));
    }
}
