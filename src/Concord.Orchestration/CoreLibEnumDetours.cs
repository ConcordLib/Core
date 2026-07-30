using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Concord.Emit;

namespace Concord;

/// <summary>
///     Routes the <see cref="Enum" /> static methods through the extended enum registry, so an added
///     member appears to code the adapter never registered a consumer for.
/// </summary>
/// <remarks>
///     <para>
///         Every detour opens with a registry lookup and returns immediately for an enum no declaration
///         extends, so an unaffected enum pays one dictionary lookup.
///     </para>
///     <para>
///         String interpolation does not route. <c>$"{value}"</c> and the internal callers behind it skip
///         the public <see cref="Enum.ToString()" />, so an added member still prints as its number there.
///         Register a <c>Display</c> consumer for text the player sees.
///     </para>
/// </remarks>
public static class CoreLibEnumDetours {
    private static readonly List<IPatchHandle> Handles = new List<IPatchHandle>();

    /// <summary>
    ///     Installs the detour set. Calling it twice without <see cref="Uninstall" /> does nothing the
    ///     second time. A method that cannot be detoured logs <c>CONC141</c> and leaves the rest installed.
    /// </summary>
    /// <param name="includeFormat">
    ///     Whether to detour <see cref="Enum.Format" />. Concord measured this shape at about 0.8 nanoseconds
    ///     on an unextended enum, so it defaults to on.
    /// </param>
    public static void Install(bool includeFormat = true) {
        if (Handles.Count > 0) {
            return;
        }

        Install("GetValues", [typeof(Type)], nameof(CoreLibEnumInjections.GetValuesTail), At.Tail);
        Install("GetNames", [typeof(Type)], nameof(CoreLibEnumInjections.GetNamesTail), At.Tail);
        Install("GetName", [typeof(Type), typeof(object)], nameof(CoreLibEnumInjections.GetNameHead), At.Head);
        Install("IsDefined", [typeof(Type), typeof(object)], nameof(CoreLibEnumInjections.IsDefinedHead), At.Head);
        Install("Parse", [typeof(Type), typeof(string)], nameof(CoreLibEnumInjections.ParseHead), At.Head);
        Install("Parse", [typeof(Type), typeof(string), typeof(bool)], nameof(CoreLibEnumInjections.ParseIgnoreCaseHead), At.Head);
        Install("TryParse", [typeof(Type), typeof(string), typeof(object).MakeByRefType()], nameof(CoreLibEnumInjections.TryParseHead), At.Head);

        if (!includeFormat) {
            return;
        }

        Install("Format", [typeof(Type), typeof(object), typeof(string)], nameof(CoreLibEnumInjections.FormatHead), At.Head);
    }

    /// <summary>
    ///     Reverts every detour this class installed.
    /// </summary>
    public static void Uninstall() {
        foreach (IPatchHandle handle in Handles) {
            try {
                handle.Dispose();
            } catch (Exception ex) {
                Debug.WriteLine("[Concord] CONC141: could not revert an Enum detour: " + ex.Message);
            }
        }

        Handles.Clear();
    }

    private static void Install(string name, Type[] parameterTypes, string injectionName, At at) {
        try {
            MethodInfo? target = typeof(Enum).GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null);

            if (target is null) {
                Report(name, "the runtime declares no matching overload");
                return;
            }

            MethodInfo injection = typeof(CoreLibEnumInjections).GetMethod(injectionName, BindingFlags.Public | BindingFlags.Static)!;
            Handles.Add(Patcher.Patch(target, injection, at));
        } catch (Exception ex) {
            Report(name, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void Report(string name, string reason) {
        string message = "[Concord] CONC141: could not detour Enum." + name + ", " + reason + ". The rest of the set stays installed.";
        Debug.WriteLine(message);
        Console.Error.WriteLine(message);
    }
}

internal static class CoreLibEnumInjections {
    public static void GetValuesTail(ControlHandle<Array> ch, Type enumType) {
        IReadOnlyList<(long Value, string Name)> added = ExtendedEnumRegistry.MembersOf(enumType);
        if (added.Count == 0) {
            return;
        }

        Array original = ch.ReturnValue;
        Array combined = Array.CreateInstance(enumType, original.Length + added.Count);
        original.CopyTo(combined, 0);

        for (int i = 0; i < added.Count; i++) {
            combined.SetValue(ToEnum(enumType, added[i].Value), original.Length + i);
        }

        ch.ReturnValue = combined;
    }

    public static void GetNamesTail(ControlHandle<string[]> ch, Type enumType) {
        IReadOnlyList<(long Value, string Name)> added = ExtendedEnumRegistry.MembersOf(enumType);
        if (added.Count == 0) {
            return;
        }

        string[] original = ch.ReturnValue;
        string[] combined = new string[original.Length + added.Count];
        original.CopyTo(combined, 0);

        for (int i = 0; i < added.Count; i++) {
            combined[original.Length + i] = added[i].Name;
        }

        ch.ReturnValue = combined;
    }

    public static void GetNameHead(ControlHandle<string?> ch, Type enumType, object value) {
        if (!TryFindName(enumType, value, out string? name)) {
            return;
        }

        ch.ReturnValue = name;
        ch.Cancel();
    }

    public static void IsDefinedHead(ControlHandle<bool> ch, Type enumType, object value) {
        if (!TryFindName(enumType, value, out string? _)) {
            return;
        }

        ch.ReturnValue = true;
        ch.Cancel();
    }

    public static void ParseHead(ControlHandle<object> ch, Type enumType, string value) {
        if (!TryFindValue(enumType, value, false, out long raw)) {
            return;
        }

        ch.ReturnValue = ToEnum(enumType, raw);
        ch.Cancel();
    }

    public static void ParseIgnoreCaseHead(ControlHandle<object> ch, Type enumType, string value, bool ignoreCase) {
        if (!TryFindValue(enumType, value, ignoreCase, out long raw)) {
            return;
        }

        ch.ReturnValue = ToEnum(enumType, raw);
        ch.Cancel();
    }

    public static void TryParseHead(ControlHandle<bool> ch, Type enumType, string value, ref object? result) {
        if (!TryFindValue(enumType, value, false, out long raw)) {
            return;
        }

        result = ToEnum(enumType, raw);
        ch.ReturnValue = true;
        ch.Cancel();
    }

    public static void FormatHead(ControlHandle<string> ch, Type enumType, object value, string format) {
        if (!ExtendedEnumRegistry.Extends(enumType)) {
            return;
        }

        if (!string.Equals(format, "G", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(format, "F", StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        if (!TryFindName(enumType, value, out string? name)) {
            return;
        }

        ch.ReturnValue = name!;
        ch.Cancel();
    }

    private static bool TryFindName(Type enumType, object value, out string? name) {
        name = null;

        IReadOnlyList<(long Value, string Name)> added = ExtendedEnumRegistry.MembersOf(enumType);
        if (added.Count == 0) {
            return false;
        }

        long raw = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        foreach ((long candidate, string candidateName) in added) {
            if (candidate != raw) {
                continue;
            }

            name = candidateName;
            return true;
        }

        return false;
    }

    private static bool TryFindValue(Type enumType, string name, bool ignoreCase, out long value) {
        value = 0;

        IReadOnlyList<(long Value, string Name)> added = ExtendedEnumRegistry.MembersOf(enumType);
        if (added.Count == 0) {
            return false;
        }

        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach ((long candidate, string candidateName) in added) {
            if (!string.Equals(candidateName, name, comparison)) {
                continue;
            }

            value = candidate;
            return true;
        }

        return false;
    }

    private static object ToEnum(Type enumType, long value) {
        return Enum.ToObject(
            enumType,
            Convert.ChangeType(value, Enum.GetUnderlyingType(enumType), CultureInfo.InvariantCulture));
    }
}
