using System.Diagnostics;
using System.Globalization;

namespace Concord;

/// <content>
///     Allocation: the id-to-value map, the taken-value rules, and adapter-backed persistence.
/// </content>
public static partial class ExtendedEnumRegistry {
    private static readonly Dictionary<string, long> Map = new Dictionary<string, long>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Type> Owners = new Dictionary<string, Type>(StringComparer.Ordinal);
    private static readonly Dictionary<Type, List<(long Value, string Name)>> ByEnum = new Dictionary<Type, List<(long Value, string Name)>>();
    private static readonly HashSet<Type> WarnedLowBits = new HashSet<Type>();

    private static IEnumValueStore? store;
    private static bool warnedNoStore;

    /// <summary>
    ///     Registers the adapter storage that persists the id-to-value map. Without one, values live in
    ///     memory only and are not stable across runs.
    /// </summary>
    /// <param name="valueStore">The adapter's storage.</param>
    public static void UseStore(IEnumValueStore valueStore) {
        store = valueStore;
        if (valueStore.TryLoad(out IReadOnlyDictionary<string, long> loaded)) {
            foreach (KeyValuePair<string, long> entry in loaded) {
                Map[entry.Key] = entry.Value;
            }
        }
    }

    /// <summary>
    ///     Whether any declaration has added members to this enum. Detours call this first, so an
    ///     unextended enum pays one dictionary lookup and nothing else.
    /// </summary>
    /// <param name="enumType">The enum to test.</param>
    /// <returns><see langword="true" /> when the enum has added members.</returns>
    public static bool Extends(Type enumType) {
        return ByEnum.ContainsKey(enumType);
    }

    internal static IReadOnlyList<(long Value, string Name)> MembersOf(Type enumType) {
        return ByEnum.TryGetValue(enumType, out List<(long Value, string Name)>? list) ? list : [];
    }

    internal static IReadOnlyDictionary<string, long> Allocate(IReadOnlyList<ExtendedEnumMember> members) {
        if (store is null && !warnedNoStore) {
            warnedNoStore = true;
            Warn("no IEnumValueStore is registered, so extended enum values live in memory only and are not stable across runs.");
        }

        Dictionary<string, long> assigned = new Dictionary<string, long>(StringComparer.Ordinal);
        List<ExtendedEnumMember> pending = new List<ExtendedEnumMember>();

        foreach (ExtendedEnumMember member in members) {
            if (Map.TryGetValue(member.Id, out long known)) {
                assigned[member.Id] = known;
                Record(member, known);
                continue;
            }

            pending.Add(member);
        }

        pending.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));

        foreach (ExtendedEnumMember member in pending) {
            if (member.PinnedValue is not long pinned) {
                continue;
            }

            HashSet<long> taken = TakenValues(member.EnumType);
            if (taken.Contains(pinned)) {
                throw new ConcordEnumException(
                    "CONC135",
                    $"Member '{member.Id}' pins the value {pinned.ToString(CultureInfo.InvariantCulture)} on '{member.EnumType.Name}', but that value is already taken.");
            }

            assigned[member.Id] = pinned;
            Record(member, pinned);
        }

        foreach (ExtendedEnumMember member in pending) {
            if (member.PinnedValue is not null) {
                continue;
            }

            HashSet<long> taken = TakenValues(member.EnumType);
            long value = IsFlags(member.EnumType)
                ? NextFreeBit(member.EnumType, taken)
                : NextFreeValue(member.EnumType, taken);

            assigned[member.Id] = value;
            Record(member, value);
        }

        store?.Save(Map);
        return assigned;
    }

    internal static void ApplyTo(IReadOnlyList<ExtendedEnumMember> members) {
        IReadOnlyDictionary<string, long> allocated = Allocate(members);

        foreach (ExtendedEnumMember member in members) {
            if (member.Field.IsLiteral) {
                continue;
            }

            object value = Enum.ToObject(
                member.EnumType,
                Convert.ChangeType(
                    allocated[member.Id],
                    Enum.GetUnderlyingType(member.EnumType),
                    CultureInfo.InvariantCulture));

            member.Field.SetValue(null, value);
        }
    }

    internal static void ResetForTests() {
        Map.Clear();
        Owners.Clear();
        ByEnum.Clear();
        WarnedLowBits.Clear();
        store = null;
        warnedNoStore = false;
        Reset();
    }

    private static void Record(ExtendedEnumMember member, long value) {
        Map[member.Id] = value;
        Owners[member.Id] = member.EnumType;

        if (!ByEnum.TryGetValue(member.EnumType, out List<(long Value, string Name)>? list)) {
            list = new List<(long Value, string Name)>();
            ByEnum.Add(member.EnumType, list);
        }

        string name = member.Field.Name;
        for (int i = 0; i < list.Count; i++) {
            if (list[i].Name == name) {
                list[i] = (value, name);
                return;
            }
        }

        list.Add((value, name));
    }

    private static HashSet<long> TakenValues(Type enumType) {
        HashSet<long> taken = new HashSet<long>();

        foreach (object value in Enum.GetValues(enumType)) {
            taken.Add(ToLong(value));
        }

        foreach (KeyValuePair<string, long> entry in Map) {
            if (!Owners.TryGetValue(entry.Key, out Type? owner) || owner == enumType) {
                taken.Add(entry.Value);
            }
        }

        return taken;
    }

    private static long NextFreeValue(Type enumType, HashSet<long> taken) {
        long start = 0;
        foreach (long value in taken) {
            if (value > start) {
                start = value;
            }
        }

        long max = MaxValue(enumType);
        for (long candidate = start + 1; candidate <= max; candidate++) {
            if (!taken.Contains(candidate)) {
                return candidate;
            }
        }

        throw new ConcordEnumException(
            "CONC136",
            $"'{enumType.Name}' has no free value left in its underlying type '{Enum.GetUnderlyingType(enumType).Name}'.");
    }

    private static long NextFreeBit(Type enumType, HashSet<long> taken) {
        int width = UnderlyingWidth(enumType);
        long occupied = 0;
        foreach (long value in taken) {
            occupied |= value;
        }

        long free = -1;
        int remaining = 0;

        for (int position = 0; position < width; position++) {
            if ((occupied & (1L << position)) != 0) {
                continue;
            }

            remaining++;
            if (free < 0) {
                free = 1L << position;
            }
        }

        if (free < 0) {
            throw new ConcordEnumException(
                "CONC136",
                $"Flags enum '{enumType.Name}' has no free bit left in its underlying type '{Enum.GetUnderlyingType(enumType).Name}'.");
        }

        if (remaining <= 3 && WarnedLowBits.Add(enumType)) {
            Warn($"flags enum '{enumType.Name}' has {remaining.ToString(CultureInfo.InvariantCulture)} free bits left.");
        }

        return free;
    }

    private static bool IsFlags(Type enumType) {
        return enumType.IsDefined(typeof(FlagsAttribute), false);
    }

    private static int UnderlyingWidth(Type enumType) {
        Type underlying = Enum.GetUnderlyingType(enumType);

        if (underlying == typeof(byte) || underlying == typeof(sbyte)) {
            return 8;
        }

        if (underlying == typeof(short) || underlying == typeof(ushort)) {
            return 16;
        }

        if (underlying == typeof(int) || underlying == typeof(uint)) {
            return 32;
        }

        return 64;
    }

    private static long MaxValue(Type enumType) {
        Type underlying = Enum.GetUnderlyingType(enumType);

        if (underlying == typeof(byte)) {
            return byte.MaxValue;
        }

        if (underlying == typeof(sbyte)) {
            return sbyte.MaxValue;
        }

        if (underlying == typeof(short)) {
            return short.MaxValue;
        }

        if (underlying == typeof(ushort)) {
            return ushort.MaxValue;
        }

        if (underlying == typeof(int)) {
            return int.MaxValue;
        }

        if (underlying == typeof(uint)) {
            return uint.MaxValue;
        }

        return long.MaxValue;
    }

    private static long ToLong(object value) {
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static void Warn(string message) {
        Debug.WriteLine("[Concord] " + message);
        Console.Error.WriteLine("[Concord] " + message);
    }
}
