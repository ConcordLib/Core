using System;
using System.Collections.Generic;
using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Builds the one report a host logs when the composer drops a foreign patch, so every backend
///     words it the same way and no target repeats the same eviction on a later rebuild.
/// </summary>
public static class EvictionReport {
    private static readonly HashSet<string> Reported = [];

    /// <summary>
    ///     Composes the report for one eviction, or returns <c>null</c> when this target has already
    ///     reported this owner's eviction for this code.
    /// </summary>
    /// <param name="marker">The host's log marker, which the report is appended to.</param>
    /// <param name="target">The method whose composition dropped the injection.</param>
    /// <param name="rejection">The injection the composer dropped.</param>
    /// <returns>The line to log, or <c>null</c> when it was already reported.</returns>
    public static string? Compose(string marker, MethodBase target, RejectedInjection rejection) {
        if (marker is null) {
            throw new ArgumentNullException(nameof(marker));
        }

        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        if (rejection is null) {
            throw new ArgumentNullException(nameof(rejection));
        }

        string described = target.DeclaringType?.FullName + "." + target.Name;
        lock (Reported) {
            if (!Reported.Add(described + " :: " + rejection.Owner + " :: " + rejection.Code)) {
                return null;
            }
        }

        return marker + " " + described + " owner '" + rejection.Owner + "': " + rejection.Message +
               " Concord dropped only that one patch and kept the rest of this method working. If '" +
               rejection.Owner + "' is a mod you use, report this line at " +
               "https://github.com/ConcordLib/Core/issues so we can tell a broken patch apart from a Concord bug.";
    }
}
