namespace Concord;

/// <summary>
///     Opt-in marker consumed by Concord.Generators: generates the injected-member declaration
///     (<see cref="InjectFieldAttribute" />, <see cref="InjectPropertyAttribute" />, or
///     <see cref="InjectMethodAttribute" />) for the named target member into the partial
///     declaration class. Ignored at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ShadowAttribute : Attribute {
    /// <summary>Initializes a shadow request for a uniquely-named target member.</summary>
    /// <param name="member">The target member name.</param>
    public ShadowAttribute(string member) {
        Member = member;
    }

    /// <summary>Initializes a shadow request disambiguating a method overload by parameter types.</summary>
    /// <param name="member">The target method name.</param>
    /// <param name="parameterTypes">The parameter types of the overload to shadow.</param>
    public ShadowAttribute(string member, params Type[] parameterTypes) {
        Member = member;
        ParameterTypes = parameterTypes;
    }

    /// <summary>Gets the target member name.</summary>
    public string Member { get; }

    /// <summary>Gets the overload parameter types, or <see langword="null" /> when not given.</summary>
    public Type[]? ParameterTypes { get; }
}
