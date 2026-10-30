using System.Reflection;
using System.Runtime.InteropServices;

namespace Concord.Host.Adapter;

/// <summary>
///     Mints the metadata tokens a composed body's IL carries as operands. A token is only valid in the
///     module it was minted against, so the module is passed on every call.
/// </summary>
public interface IMetadataMinter {
    /// <summary>Mints a token for <paramref name="method" /> against <paramref name="target" />.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="method">The method the body refers to.</param>
    /// <returns>A metadata token, meaningless in any other module.</returns>
    int Token(Module target, MethodBase method);

    /// <summary>Mints a token for <paramref name="field" /> against <paramref name="target" />.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="field">The field the body refers to.</param>
    /// <returns>A metadata token, meaningless in any other module.</returns>
    int Token(Module target, FieldInfo field);

    /// <summary>Mints a token for <paramref name="type" /> against <paramref name="target" />.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="type">The type the body refers to.</param>
    /// <returns>A metadata token, meaningless in any other module.</returns>
    int Token(Module target, Type type);

    /// <summary>Interns <paramref name="literal" /> in <paramref name="target" /> and hands back its token.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="literal">The string the body loads.</param>
    /// <returns>A metadata token, meaningless in any other module.</returns>
    int Token(Module target, string literal);

    /// <summary>Mints a token for <paramref name="signature" /> against <paramref name="target" />.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="signature">An encoded ECMA-335 local variable signature blob.</param>
    /// <returns>A token a body header can point at instead of listing its locals.</returns>
    int LocalsToken(Module target, byte[] signature);
}

/// <summary>
///     Mints tokens through the forked runtime's own token api, bound by reflection once per process. Check
///     <see cref="IsAvailable" /> first: on a stock runtime nothing binds and every call here throws.
/// </summary>
public sealed class ForkMinter : IMetadataMinter {
    private const string UpdaterName = "WaylithClr.MethodBodyUpdater, System.Private.CoreLib";

    private const string TokenEntry = "GetTokenFor";

    private const string LocalsEntry = "GetTokenForLocals";

    private static readonly Func<Module, MethodBase, int>? MethodToken = Bind<MethodBase>(TokenEntry);

    private static readonly Func<Module, FieldInfo, int>? FieldToken = Bind<FieldInfo>(TokenEntry);

    private static readonly Func<Module, Type, int>? TypeToken = Bind<Type>(TokenEntry);

    private static readonly Func<Module, string, int>? LiteralToken = Bind<string>(TokenEntry);

    private static readonly Func<Module, byte[], int>? LocalsSignatureToken = Bind<byte[]>(LocalsEntry);

    /// <summary>
    ///     <see langword="true" /> only when all five entry points bound. It is <see langword="false" /> on
    ///     any runtime but the fork, and a caller that mints anyway gets an exception per call.
    /// </summary>
    public static bool IsAvailable =>
        MethodToken is not null
        && FieldToken is not null
        && TypeToken is not null
        && LiteralToken is not null
        && LocalsSignatureToken is not null;

    /// <inheritdoc />
    /// <remarks>A generic method operand needs <see cref="RuntimeCapability.MintsMethodSpecs" /> checked first; the MethodSpec the runtime builds here is unusable without it.</remarks>
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api; keep the detour backend.</exception>
    public int Token(Module target, MethodBase method) {
        return Bound(MethodToken, TokenEntry, nameof(MethodBase))(target, method);
    }

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api; keep the detour backend.</exception>
    public int Token(Module target, FieldInfo field) {
        return Bound(FieldToken, TokenEntry, nameof(FieldInfo))(target, field);
    }

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api; keep the detour backend.</exception>
    public int Token(Module target, Type type) {
        return Bound(TypeToken, TokenEntry, nameof(Type))(target, type);
    }

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api; keep the detour backend.</exception>
    public int Token(Module target, string literal) {
        return Bound(LiteralToken, TokenEntry, nameof(String))(target, literal);
    }

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api; keep the detour backend.</exception>
    public int LocalsToken(Module target, byte[] signature) {
        return Bound(LocalsSignatureToken, LocalsEntry, "byte[]")(target, signature);
    }

    private static Func<Module, T, int> Bound<T>(Func<Module, T, int>? bound, string entry, string operandKind) {
        return bound ?? throw new PlatformNotSupportedException(
            $"'{UpdaterName}'.{entry}(Module, {operandKind}) is not present on this runtime " +
            $"('{RuntimeInformation.FrameworkDescription}'). Minting metadata tokens needs the WaylithCLR fork of " +
            "CoreLib, so the waylith backend cannot run here and the caller has to keep the detour backend.");
    }

    private static Func<Module, T, int>? Bind<T>(string entry) {
        Type? updater = Type.GetType(UpdaterName, throwOnError: false);
        MethodInfo? found = updater?.GetMethod(
            entry,
            BindingFlags.Public | BindingFlags.Static,
            [typeof(Module), typeof(T)]);

        return found?.CreateDelegate<Func<Module, T, int>>();
    }
}
