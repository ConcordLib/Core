using System.Collections.Immutable;

namespace Concord.Host.Adapter;

/// <summary>
///     Encodes a composed body's local slots as an ECMA-335 <c>LocalVarSig</c> blob. Constructed generics, arrays and
///     pointers are written out structurally, because a signature only accepts a TypeDefOrRef token at its leaves.
/// </summary>
public static class LocalSignatureEncoder {
    /// <summary>The most locals one signature can declare.</summary>
    public const int MaxLocals = 0xFFFE;

    /// <summary>Encodes <paramref name="locals" /> into a signature blob ready for <see cref="ITokenSource.TokenForLocals" />.</summary>
    /// <param name="locals">The slots in slot order. Must hold at least one: a body with no locals takes no signature at all.</param>
    /// <param name="tokens">The token source for the module the body is written into.</param>
    /// <returns>The encoded blob bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when the list is empty, longer than <see cref="MaxLocals" />, or holds a slot type with no encoding.</exception>
    public static byte[] Encode(IReadOnlyList<ComposedLocal> locals, ITokenSource tokens) {
        ArgumentNullException.ThrowIfNull(locals);
        ArgumentNullException.ThrowIfNull(tokens);

        if (locals.Count == 0) {
            throw new ArgumentException(
                "a LocalVarSig must declare at least one local; a body with no locals takes no signature token at all",
                nameof(locals));
        }

        if (locals.Count > MaxLocals) {
            throw new ArgumentException(
                $"{locals.Count} locals exceeds the ECMA-335 limit of {MaxLocals}",
                nameof(locals));
        }

        BlobBuilder builder = new BlobBuilder();
        LocalVariablesEncoder signature = new BlobEncoder(builder).LocalVariableSignature(locals.Count);

        for (int slot = 0; slot < locals.Count; slot++) {
            EncodeSlot(signature.AddVariable(), locals[slot], tokens, slot);
        }

        return builder.ToArray();
    }

    private static void EncodeSlot(LocalVariableTypeEncoder encoder, ComposedLocal local, ITokenSource tokens, int slot) {
        Type type = Unwrap(local.Type, slot);

        if (type == typeof(TypedReference)) {
            if (local.IsPinned) {
                throw Reject(slot, type, "TypedReference cannot carry the PINNED constraint");
            }

            encoder.TypedReference();
            return;
        }

        bool byRef = type.IsByRef;
        if (byRef) {
            type = Unwrap(type.GetElementType(), slot);
        }

        EncodeType(encoder.Type(byRef, local.IsPinned), type, tokens, slot);
    }

    private static void EncodeType(SignatureTypeEncoder encoder, Type type, ITokenSource tokens, int slot) {
        if (type.IsPointer) {
            Type target = Unwrap(type.GetElementType(), slot);
            if (target == typeof(void)) {
                encoder.VoidPointer();
                return;
            }

            EncodeType(encoder.Pointer(), target, tokens, slot);
            return;
        }

        if (type.IsArray) {
            Type element = Unwrap(type.GetElementType(), slot);
            if (type.IsSZArray) {
                EncodeType(encoder.SZArray(), element, tokens, slot);
                return;
            }

            int rank = type.GetArrayRank();
            encoder.Array(out SignatureTypeEncoder elementEncoder, out ArrayShapeEncoder shapeEncoder);
            EncodeType(elementEncoder, element, tokens, slot);
            shapeEncoder.Shape(rank, ImmutableArray<int>.Empty, ImmutableArray.CreateRange(Enumerable.Repeat(0, rank)));
            return;
        }

        if (type.IsGenericMethodParameter) {
            encoder.GenericMethodTypeParameter(type.GenericParameterPosition);
            return;
        }

        if (type.IsGenericTypeParameter) {
            encoder.GenericTypeParameter(type.GenericParameterPosition);
            return;
        }

        if (type == typeof(void)) {
            throw Reject(slot, type, "void is only encodable as a pointer target");
        }

        if (type.IsFunctionPointer) {
            throw Reject(slot, type, "a function-pointer local is not encoded");
        }

        if (PrimitiveOf(type) is PrimitiveTypeCode primitive) {
            encoder.PrimitiveType(primitive);
            return;
        }

        if (type.IsGenericType) {
            Type[] arguments = type.GetGenericArguments();
            GenericTypeArgumentsEncoder argumentEncoder = encoder.GenericInstantiation(
                TypeDefOrRef(type.GetGenericTypeDefinition(), tokens, slot),
                arguments.Length,
                type.IsValueType);

            foreach (Type argument in arguments) {
                EncodeType(argumentEncoder.AddArgument(), Unwrap(argument, slot), tokens, slot);
            }

            return;
        }

        encoder.Type(TypeDefOrRef(type, tokens, slot), type.IsValueType);
    }

    private static EntityHandle TypeDefOrRef(Type type, ITokenSource tokens, int slot) {
        int token = tokens.TokenFor(type);
        EntityHandle handle = MetadataTokens.EntityHandle(token);

        if (handle.Kind is not (HandleKind.TypeDefinition or HandleKind.TypeReference)) {
            throw Reject(slot, type, $"the token source returned a {handle.Kind} token (0x{token:x8}); a TypeSpec is rejected where a signature wants a TypeDefOrRef, so constructed types must be encoded structurally");
        }

        if (handle.IsNil) {
            throw Reject(slot, type, $"the token source returned the nil token 0x{token:x8}");
        }

        return handle;
    }

    private static PrimitiveTypeCode? PrimitiveOf(Type type) {
        if (type == typeof(bool)) return PrimitiveTypeCode.Boolean;
        if (type == typeof(char)) return PrimitiveTypeCode.Char;
        if (type == typeof(sbyte)) return PrimitiveTypeCode.SByte;
        if (type == typeof(byte)) return PrimitiveTypeCode.Byte;
        if (type == typeof(short)) return PrimitiveTypeCode.Int16;
        if (type == typeof(ushort)) return PrimitiveTypeCode.UInt16;
        if (type == typeof(int)) return PrimitiveTypeCode.Int32;
        if (type == typeof(uint)) return PrimitiveTypeCode.UInt32;
        if (type == typeof(long)) return PrimitiveTypeCode.Int64;
        if (type == typeof(ulong)) return PrimitiveTypeCode.UInt64;
        if (type == typeof(float)) return PrimitiveTypeCode.Single;
        if (type == typeof(double)) return PrimitiveTypeCode.Double;
        if (type == typeof(nint)) return PrimitiveTypeCode.IntPtr;
        if (type == typeof(nuint)) return PrimitiveTypeCode.UIntPtr;
        if (type == typeof(string)) return PrimitiveTypeCode.String;
        if (type == typeof(object)) return PrimitiveTypeCode.Object;
        return null;
    }

    private static Type Unwrap(Type? type, int slot) {
        if (type is null) {
            throw new ArgumentException($"local slot {slot} has a null type", "locals");
        }

        return type.UnderlyingSystemType ?? type;
    }

    private static ArgumentException Reject(int slot, Type type, string reason) =>
        new ArgumentException($"local slot {slot} of type {type}: {reason}", "locals");
}
