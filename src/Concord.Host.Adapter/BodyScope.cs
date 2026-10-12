using System.Reflection;
using Concord.Detour;

namespace Concord.Host.Adapter;

/// <summary>
///     The key every per-target table in this backend is indexed by. One replaced body covers a whole shared-body
///     scope, so bookkeeping has to be keyed the same way or two instantiations fight over one entry.
/// </summary>
public static class BodyScope {
    /// <summary>The scope key for <paramref name="target" />, shared by every method that shares its IL body.</summary>
    /// <param name="target">The method being patched.</param>
    /// <returns>The normalized generic definition. Every instantiation of one generic maps to the same key.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static MethodBase KeyOf(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        return MethodIdentity.Normalize(SharedGenericPolicy.TypicalDefinition(target));
    }
}
