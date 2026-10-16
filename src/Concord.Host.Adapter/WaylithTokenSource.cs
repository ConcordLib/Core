using System.Reflection;

namespace Concord.Host.Adapter;

/// <summary>Thrown when a member cannot get a metadata token in the target module, so the body cannot be built and the caller has to stay on the detour backend.</summary>
public sealed class TokenAccessException : InvalidOperationException {
    /// <summary>Initializes a new instance.</summary>
    /// <param name="message">Why the token was refused, and what the caller can change to make it mintable.</param>
    public TokenAccessException(string message) : base(message) {
    }
}

/// <summary>An <see cref="ITokenSource" /> that mints each operand's token into one module, refusing members that module's code could not legally reach.</summary>
public sealed class WaylithTokenSource : ITokenSource {
    private readonly Module target;

    private readonly IMetadataMinter minter;

    /// <summary>Initializes a new instance that mints through <see cref="ForkMinter" />, the forked runtime's <c>MethodBodyUpdater</c>.</summary>
    /// <param name="target">The module whose metadata the tokens are minted into; operands must be reachable from it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public WaylithTokenSource(Module target) : this(target, new ForkMinter()) {
    }

    /// <summary>Initializes a new instance over a supplied minter, for tests and for hosts that mint tokens another way.</summary>
    /// <param name="target">The module whose metadata the tokens are minted into; operands must be reachable from it.</param>
    /// <param name="minter">The minter that issues the tokens.</param>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public WaylithTokenSource(Module target, IMetadataMinter minter) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(minter);
        this.target = target;
        this.minter = minter;
    }

    /// <summary>Mints a token for a type operand, as used by <c>ldtoken</c>, <c>castclass</c>, <c>newarr</c> and the boxing opcodes.</summary>
    /// <param name="type">The type to name in the body.</param>
    /// <returns>The metadata token for <paramref name="type" /> in the target module.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type" /> is <c>null</c>.</exception>
    /// <exception cref="TokenAccessException">The type, or a type nested in it, is not visible to code in the target module.</exception>
    /// <exception cref="PlatformNotSupportedException">The default minter is in use and this runtime is not the fork.</exception>
    public int TokenFor(Type type) {
        ArgumentNullException.ThrowIfNull(type);
        Refuse(type);
        return minter.Token(target, type);
    }

    /// <summary>Mints a token for a field operand, as used by the <c>ldfld</c>, <c>stfld</c>, <c>ldsfld</c> and <c>stsfld</c> family.</summary>
    /// <param name="field">The field to name in the body.</param>
    /// <returns>The metadata token for <paramref name="field" /> in the target module.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field" /> is <c>null</c>.</exception>
    /// <exception cref="TokenAccessException">The field or its declaring type is out of reach from the target module, so the IL would throw <c>FieldAccessException</c> when it ran.</exception>
    /// <exception cref="PlatformNotSupportedException">The default minter is in use and this runtime is not the fork.</exception>
    public int TokenFor(FieldInfo field) {
        ArgumentNullException.ThrowIfNull(field);
        Refuse(field);
        return minter.Token(target, field);
    }

    /// <summary>Mints a token for a method operand, as used by <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c> and <c>ldtoken</c>.</summary>
    /// <param name="method">The method to name in the body.</param>
    /// <returns>The metadata token for <paramref name="method" /> in the target module.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method" /> is <c>null</c>.</exception>
    /// <exception cref="TokenAccessException">The method is generic, or it is out of reach from the target module. Any generic method is refused, including the open definition a generic method resolves to when it calls itself, because the runtime mints its MethodSpec from a malformed signature blob and the published body throws <c>BadImageFormatException</c> on first compile.</exception>
    /// <exception cref="PlatformNotSupportedException">The default minter is in use and this runtime is not the fork.</exception>
    public int TokenFor(MethodBase method) {
        ArgumentNullException.ThrowIfNull(method);
        RefuseMethodSpec(method);
        Refuse(method);
        return minter.Token(target, method);
    }

    /// <summary>Mints a user-string token for an <c>ldstr</c> operand. No reachability check applies, since a string literal has no declaring type.</summary>
    /// <param name="literal">The string to add to the module's user-string heap.</param>
    /// <returns>The metadata token for <paramref name="literal" /> in the target module.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="literal" /> is <c>null</c>.</exception>
    /// <exception cref="PlatformNotSupportedException">The default minter is in use and this runtime is not the fork.</exception>
    public int TokenFor(string literal) {
        ArgumentNullException.ThrowIfNull(literal);
        return minter.Token(target, literal);
    }

    /// <summary>Mints the standalone-signature token a method header points at for its local variables.</summary>
    /// <param name="signature">An ECMA-335 LocalVarSig blob describing the body's locals in order.</param>
    /// <returns>The standalone-signature token to write into the body's header.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signature" /> is <c>null</c>.</exception>
    /// <exception cref="PlatformNotSupportedException">The default minter is in use and this runtime is not the fork.</exception>
    public int TokenForLocals(byte[] signature) {
        ArgumentNullException.ThrowIfNull(signature);
        return minter.LocalsToken(target, signature);
    }

    private void RefuseMethodSpec(MethodBase method) {
        if (method is not MethodInfo { IsGenericMethod: true } generic || MethodSpecMinter.IsAvailable) {
            return;
        }

        string owner = generic.DeclaringType?.FullName ?? generic.Module.Name;
        string arguments = string.Join(
            ", ",
            generic.GetGenericArguments().Select(argument => argument.FullName ?? argument.Name));
        string form = generic.IsGenericMethodDefinition
            ? $"{generic.Name}<{arguments}> (open generic method definition, not yet constructed)"
            : $"{generic.Name}<{arguments}>";
        throw new TokenAccessException(
            $"cannot mint a metadata token for the method '{owner}.{form}' into module " +
            $"'{target.Name}': a call, callvirt, ldftn or ldtoken operand on any generic method needs a MethodSpec " +
            "token, whether or not the operand itself resolves to the generic method definition (a generic method " +
            "calling itself resolves that way), and this runtime mints that token from a malformed signature blob. " +
            "The blob opens with 0x15, the element code for a generic type instance, where ECMA-335 II.23.2.15 " +
            "requires 0x0A. Nothing refuses the write, so a body carrying that token publishes, counts as " +
            "published, and then throws BadImageFormatException the first time the method is compiled. Refusing " +
            "here instead keeps the target on the detour backend.");
    }

    private void Refuse(MemberInfo member) {
        if (RuntimeCapability.IgnoresAccessChecks) {
            return;
        }

        if (MemberReach.Explain(member, target) is string reason) {
            throw new TokenAccessException(reason);
        }
    }
}
