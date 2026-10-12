using System.Reflection;

namespace Concord.Host.Adapter;

/// <summary>
///     Mints metadata tokens in one target module, so IL written into that module can name a type, field, method or
///     string. Every token comes back as a raw 32-bit metadata token already present in that module's tables.
/// </summary>
public interface ITokenSource {
    /// <summary>Mints a token for <paramref name="type" />.</summary>
    /// <param name="type">The type to name from the target module.</param>
    /// <returns>A <c>TypeDef</c> or <c>TypeRef</c> token. Never a <c>TypeSpec</c>: a signature blob rejects one where it wants a TypeDefOrRef.</returns>
    int TokenFor(Type type);

    /// <summary>Mints a token for <paramref name="field" />.</summary>
    /// <param name="field">The field to name from the target module.</param>
    /// <returns>A <c>FieldDef</c> or <c>MemberRef</c> token.</returns>
    int TokenFor(FieldInfo field);

    /// <summary>Mints a token for <paramref name="method" />.</summary>
    /// <param name="method">The method or constructor to name from the target module.</param>
    /// <returns>A <c>MethodDef</c>, <c>MemberRef</c> or <c>MethodSpec</c> token, whichever the call needs.</returns>
    int TokenFor(MethodBase method);

    /// <summary>Mints a user-string token for <paramref name="literal" />, the operand form <c>ldstr</c> takes.</summary>
    /// <param name="literal">The string to add to the target module's user-string heap.</param>
    /// <returns>A <c>UserString</c> token.</returns>
    int TokenFor(string literal);

    /// <summary>Adds an encoded <c>LocalVarSig</c> blob to the target module and mints a token for it.</summary>
    /// <param name="signature">The signature blob, as produced by <see cref="LocalSignatureEncoder.Encode" />.</param>
    /// <returns>A <c>StandAloneSig</c> token, which is what a method body header carries for its locals.</returns>
    int TokenForLocals(byte[] signature);
}
