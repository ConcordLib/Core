using System.Globalization;
using System.Reflection;

namespace Concord;

/// <summary>
///     One member an extended enum declaration adds.
/// </summary>
/// <param name="EnumType">The enum the member belongs to.</param>
/// <param name="Field">The declaration field Concord assigns.</param>
/// <param name="Id">The persisted id.</param>
/// <param name="PinnedValue">The value the author demanded, or <see langword="null" /> to let the allocator pick.</param>
public sealed record ExtendedEnumMember(Type EnumType, FieldInfo Field, string Id, long? PinnedValue);

/// <summary>
///     Reads extended enum declarations, allocates member values, and assigns the declaration fields.
/// </summary>
public static partial class ExtendedEnumRegistry {
    private const BindingFlags DeclaredFields = BindingFlags.Public |
                                                BindingFlags.NonPublic |
                                                BindingFlags.Instance |
                                                BindingFlags.Static |
                                                BindingFlags.DeclaredOnly;

    private static readonly List<ExtendedEnumMember> PendingMembers = new List<ExtendedEnumMember>();
    private static readonly Dictionary<string, Type> PendingIds = new Dictionary<string, Type>(StringComparer.Ordinal);
    private static readonly HashSet<Type> RegisteredDeclarations = new HashSet<Type>();

    internal static IReadOnlyList<ExtendedEnumMember> Pending => PendingMembers;

    internal static void RegisterDeclaration(Type declaration) {
        if (!RegisteredDeclarations.Add(declaration)) {
            return;
        }

        foreach (ExtendedEnumMember member in ReadDeclaration(declaration)) {
            if (PendingIds.TryGetValue(member.Id, out Type? owner)) {
                throw new ConcordEnumException(
                    "CONC140",
                    $"Member id '{member.Id}' is declared by both '{owner.FullName}' and '{declaration.FullName}'.");
            }

            PendingIds.Add(member.Id, declaration);
            PendingMembers.Add(member);
        }
    }

    internal static void Reset() {
        PendingMembers.Clear();
        PendingIds.Clear();
        RegisteredDeclarations.Clear();
    }

    internal static IReadOnlyList<ExtendedEnumMember> ReadDeclaration(Type declaration) {
        Type? enumType = ResolveExtendedEnumType(declaration);
        if (enumType is null) {
            return [];
        }

        List<ExtendedEnumMember> members = new List<ExtendedEnumMember>();

        foreach (FieldInfo field in declaration.GetFields(DeclaredFields)) {
            EnumMemberAttribute? attribute = field.GetCustomAttribute<EnumMemberAttribute>();
            bool typed = field.FieldType == enumType;

            if (!field.IsStatic) {
                if (typed || attribute is not null) {
                    throw new ConcordEnumException(
                        "CONC139",
                        $"Field '{declaration.Name}.{field.Name}' must be static to be a member of '{enumType.Name}'.");
                }

                continue;
            }

            if (!typed) {
                if (attribute is null) {
                    continue;
                }

                throw new ConcordEnumException(
                    "CONC139",
                    $"Field '{declaration.Name}.{field.Name}' carries [EnumMember] but is not typed as '{enumType.Name}'.");
            }

            string id = attribute?.Id ?? declaration.FullName + "." + field.Name;
            long? pinned = field.IsLiteral
                ? Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)
                : null;

            members.Add(new ExtendedEnumMember(enumType, field, id, pinned));
        }

        return members;
    }

    internal static Type? ResolveExtendedEnumType(Type declaration) {
        Type? current = declaration.BaseType;
        while (current is not null) {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ExtendedEnum<>)) {
                return current.GetGenericArguments()[0];
            }

            current = current.BaseType;
        }

        return null;
    }
}
