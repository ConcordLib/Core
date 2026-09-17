using System;
using System.Runtime.CompilerServices;

namespace Concord.Emit;

/// <summary>
///     Runtime helper for the receiver check a composed wrapper emits when its target is a
///     reference-type generic instantiation. The runtime shares one compiled body across every
///     reference-type instantiation, so calls arriving on a different instantiation reach the same
///     wrapper; this check lets the wrapper run its injections only for the requested one.
/// </summary>
public static class SharedGenericGuard {
    /// <summary>
    ///     Reports whether a wrapper's receiver belongs to the instantiation the patch was requested for.
    /// </summary>
    /// <param name="receiver">The wrapper's <c>this</c> argument.</param>
    /// <param name="requested">The constructed generic type the patch was requested for.</param>
    /// <returns><see langword="true" /> when the receiver is that instantiation or a subclass of it.</returns>
    /// <remarks>
    ///     Never inlined: an inlined <c>isinst</c> folds away, because the wrapper's receiver argument is
    ///     statically typed as the requested instantiation even when a different one calls in.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Matches(object receiver, Type requested) {
        return requested.IsInstanceOfType(receiver);
    }
}
