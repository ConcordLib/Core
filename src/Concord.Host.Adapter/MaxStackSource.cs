namespace Concord.Host.Adapter;

/// <summary>
///     Supplies the <c>maxstack</c> a composed body's header declares. A body is rejected by the runtime's IL
///     verifier when the value is too small, so a source either computes it or names a bound it can defend.
/// </summary>
public interface IMaxStackSource {
    /// <summary>The <c>maxstack</c> value to write into the header of <paramref name="body" />.</summary>
    /// <param name="body">The composed body about to be encoded.</param>
    /// <returns>The largest evaluation-stack depth the body can reach, in slots.</returns>
    int MaxStackFor(ComposedBody body);
}

/// <summary>The default source: walks the instruction stream and returns the depth it proves, rather than a guess.</summary>
public sealed class ComputedStack : IMaxStackSource {
    /// <summary>The computed <c>maxstack</c> for <paramref name="body" />.</summary>
    /// <param name="body">The composed body about to be encoded.</param>
    /// <returns>The deepest evaluation stack reachable along any path through the body.</returns>
    public int MaxStackFor(ComposedBody body) {
        return StackBound.Compute(body).MaxStack;
    }
}
