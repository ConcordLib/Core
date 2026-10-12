using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Concord.Host.Adapter;

/// <summary>
///     Asks whether IL living in one module is allowed to touch a member, before a token for it is minted. The CLR
///     only checks accessibility when the replaced body first runs, which is far too late to report usefully.
/// </summary>
public static class MemberReach {
    private const int MaxNesting = 32;

    private static readonly ConcurrentDictionary<(Assembly Owner, Assembly Consumer), bool> Grants = new();

    /// <summary>Checks whether IL in <paramref name="target" /> may reference <paramref name="member" />.</summary>
    /// <param name="member">The type, method, constructor or field a token is wanted for.</param>
    /// <param name="target">The module the patched IL is written into.</param>
    /// <returns><c>null</c> when the reference is legal, else a message naming what is out of reach and how to open it up.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <c>null</c>.</exception>
    public static string? Explain(MemberInfo member, Module target) {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(target);

        Assembly consumer = target.Assembly;
        string consumerName = NameOf(consumer);

        if (FirstUnreachableType(member, consumer) is Type hidden) {
            return $"cannot mint a metadata token for {Describe(member)} into module '{target.Name}': the type " +
                $"'{FullName(hidden)}' is not visible outside '{NameOf(hidden.Assembly)}'. Patched IL in " +
                $"'{consumerName}' would throw TypeAccessException the first time it ran. " +
                Fix(hidden, hidden.Assembly, consumerName);
        }

        Assembly owner = member.Module.Assembly;
        if (Reaches(owner, consumer) || AssemblyPrivateLevel(member) is not string level) {
            return null;
        }

        return $"cannot mint a metadata token for {Describe(member)} into module '{target.Name}': it is declared " +
            $"{level}, so no code in '{consumerName}' can reach it. Patched IL in '{consumerName}' would throw " +
            (member is FieldInfo ? "FieldAccessException" : "MethodAccessException") +
            " the first time it ran. " + Fix(member, owner, consumerName);
    }

    private static string Fix(MemberInfo subject, Assembly owner, string consumerName) {
        return $"Make {Describe(subject)} public, or add [assembly: InternalsVisibleTo(\"{consumerName}\")] to " +
            $"'{NameOf(owner)}' if it is only internal.";
    }

    private static Type? FirstUnreachableType(MemberInfo member, Assembly consumer) {
        if (member is Type type) {
            return FirstUnreachableType(type, consumer, 0);
        }

        if (member.DeclaringType is Type declaring && FirstUnreachableType(declaring, consumer, 0) is Type inDeclaring) {
            return inDeclaring;
        }

        if (member is MethodBase { IsGenericMethod: true } generic) {
            foreach (Type argument in generic.GetGenericArguments()) {
                if (FirstUnreachableType(argument, consumer, 0) is Type inArgument) {
                    return inArgument;
                }
            }
        }

        return null;
    }

    private static Type? FirstUnreachableType(Type type, Assembly consumer, int depth) {
        if (type.IsGenericParameter || type.IsVisible) {
            return null;
        }

        if (depth > MaxNesting) {
            return type;
        }

        if (type.HasElementType) {
            return FirstUnreachableType(type.GetElementType()!, consumer, depth + 1);
        }

        foreach (Type argument in type.GetGenericArguments()) {
            if (FirstUnreachableType(argument, consumer, depth + 1) is Type inArgument) {
                return inArgument;
            }
        }

        Type leaf = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;
        return leaf.IsVisible || Reaches(leaf.Assembly, consumer) ? null : leaf;
    }

    private static string? AssemblyPrivateLevel(MemberInfo member) {
        return member switch {
            MethodBase { IsPrivate: true } => "private",
            MethodBase { IsAssembly: true } => "internal",
            MethodBase { IsFamilyAndAssembly: true } => "private protected",
            FieldInfo { IsPrivate: true } => "private",
            FieldInfo { IsAssembly: true } => "internal",
            FieldInfo { IsFamilyAndAssembly: true } => "private protected",
            _ => null,
        };
    }

    private static bool Reaches(Assembly owner, Assembly consumer) {
        return owner == consumer || Grants.GetOrAdd((owner, consumer), static pair => ScanGrants(pair.Owner, pair.Consumer));
    }

    private static bool ScanGrants(Assembly owner, Assembly consumer) {
        string consumerName = NameOf(consumer);
        foreach (InternalsVisibleToAttribute grant in owner.GetCustomAttributes<InternalsVisibleToAttribute>()) {
            string granted = grant.AssemblyName;
            int comma = granted.IndexOf(',');
            if (comma >= 0) {
                granted = granted[..comma];
            }

            if (string.Equals(granted.Trim(), consumerName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string Describe(MemberInfo member) {
        return member switch {
            Type type => $"the type '{FullName(type)}'",
            ConstructorInfo constructor => $"the constructor '{FullName(constructor.DeclaringType)}..ctor'",
            MethodBase method => $"the method '{FullName(method.DeclaringType)}.{method.Name}'",
            FieldInfo field => $"the field '{FullName(field.DeclaringType)}.{field.Name}'",
            _ => $"the member '{member.Name}'",
        };
    }

    private static string FullName(Type? type) {
        return type is null ? "<global>" : type.FullName ?? type.Name;
    }

    private static string NameOf(Assembly assembly) {
        return assembly.GetName().Name ?? assembly.FullName ?? "<unnamed>";
    }
}
