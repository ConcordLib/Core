namespace Concord;

/// <summary>
///     Public injection-position values for <see cref="InjectAttribute" /> and the fluent <c>PatchBuilder</c>.
///     Maps to a <see cref="Concord.Emit.InjectAt" /> for composition.
/// </summary>
public enum At {
    /// <summary>Run the injection method at the head of the target (default).</summary>
    Head,

    /// <summary>
    ///     Run the injection method before each <c>return</c> in the target body (or a single chosen return),
    ///     reading and replacing the value returned at that site.
    /// </summary>
    Return,

    /// <summary>Run the injection method once at the end of the target, just before the wrapper returns.</summary>
    Tail,

    /// <summary>Wrap the whole target body.</summary>
    Around,

    /// <summary>Replace an inlined literal constant in the target body with the injection method's return value.</summary>
    Constant,

    /// <summary>Rewrite one argument of a matched call inside the target body through the injection method.</summary>
    Argument,
}
