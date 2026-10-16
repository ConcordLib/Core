using System.Collections.Generic;
using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Holds the patched methods a composed wrapper hands to an injection that asks for the target
///     itself. A method handle has no literal encoding, so the wrapper loads an index instead.
/// </summary>
public static class OriginalMethods {
    private static readonly List<MethodBase> Entries = new List<MethodBase>();

    private static readonly Dictionary<MethodBase, int> Ids = new Dictionary<MethodBase, int>();

    /// <summary>
    ///     Returns the method registered under an index.
    /// </summary>
    /// <param name="id">The index a composed wrapper was emitted with.</param>
    /// <returns>The patched method.</returns>
    public static MethodBase Get(int id) {
        lock (Entries) {
            return Entries[id];
        }
    }

    internal static int Register(MethodBase method) {
        lock (Entries) {
            if (Ids.TryGetValue(method, out int existing)) {
                return existing;
            }

            Entries.Add(method);
            int id = Entries.Count - 1;
            Ids[method] = id;
            return id;
        }
    }
}
