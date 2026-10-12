namespace Concord.Emit.Tests.ForeignTargets;

/// <summary>
///     A vararg method, for the refusal check that no metadata-token api can express a standalone
///     signature. Lives here because StyleCop's SA1414 throws on <c>__arglist</c>.
/// </summary>
public static class ForeignVarargTarget {
    /// <summary>Returns <paramref name="first" />, ignoring the vararg tail.</summary>
    /// <param name="first">The one fixed argument.</param>
    /// <returns>The value of <paramref name="first" />.</returns>
    public static int Vararg(int first, __arglist) {
        return first;
    }
}
