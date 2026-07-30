using System.Globalization;
using System.Reflection;
using Concord.Emit;

namespace Concord;

/// <summary>
///     Registers the adapter methods that expose extended enum members to the rest of a game. Concord
///     patches each registered method so it reports the added members alongside the compiled ones.
/// </summary>
/// <remarks>
///     Flag math needs no registration. <c>|</c>, <c>&amp;</c> and <see cref="Enum.HasFlag" /> run on the
///     underlying integer, so a registered bit already works.
/// </remarks>
public static class EnumConsumers {
    /// <summary>
    ///     Starts registering the consumer methods for one enum.
    /// </summary>
    /// <param name="enumType">The enum whose consumers follow.</param>
    /// <returns>A builder that accepts one consumer method at a time.</returns>
    public static ConsumerBuilder For(Type enumType) {
        return new ConsumerBuilder(enumType);
    }
}

/// <summary>
///     Accepts the four consumer shapes for one enum. Each method patches the target and returns the
///     same builder, so registrations chain.
/// </summary>
public sealed class ConsumerBuilder {
    private readonly Type enumType;

    internal ConsumerBuilder(Type enumType) {
        this.enumType = enumType;
    }

    /// <summary>
    ///     Registers a method that lists every value of the enum. It must return <c>TEnum[]</c> or
    ///     <c>IEnumerable&lt;TEnum&gt;</c>. Concord appends the added members to whatever it returns.
    /// </summary>
    /// <param name="owner">The type declaring the method.</param>
    /// <param name="methodName">The method name.</param>
    /// <returns>This builder.</returns>
    public ConsumerBuilder Values(Type owner, string methodName) {
        MethodInfo target = Resolve(owner, methodName);
        Type arrayType = enumType.MakeArrayType();
        Type enumerableType = typeof(IEnumerable<>).MakeGenericType(enumType);

        string stock;
        if (target.ReturnType == arrayType) {
            stock = nameof(EnumConsumerInjections.ValuesArrayTail);
        } else if (target.ReturnType == enumerableType) {
            stock = nameof(EnumConsumerInjections.ValuesEnumerableTail);
        } else {
            throw Shape(target, $"return '{enumType.Name}[]' or 'IEnumerable<{enumType.Name}>'");
        }

        if (target.GetParameters().Length != 0) {
            throw Shape(target, "take no parameters");
        }

        Patcher.Patch(target, Stock(stock), At.Tail);
        return this;
    }

    /// <summary>
    ///     Registers a method that turns a name into a value. It must be <c>TEnum (string)</c>. Concord
    ///     answers before the original runs when the name matches an added member.
    /// </summary>
    /// <param name="owner">The type declaring the method.</param>
    /// <param name="methodName">The method name.</param>
    /// <returns>This builder.</returns>
    public ConsumerBuilder Parse(Type owner, string methodName) {
        MethodInfo target = Resolve(owner, methodName);
        ParameterInfo[] parameters = target.GetParameters();

        if (target.ReturnType != enumType || parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) {
            throw Shape(target, $"be '{enumType.Name} (string)'");
        }

        Patcher.Patch(target, Stock(nameof(EnumConsumerInjections.ParseHead)), At.Head);
        return this;
    }

    /// <summary>
    ///     Registers a method that turns a value into display text. It must be <c>string (TEnum)</c>.
    ///     Concord answers with the member's field name for an added member.
    /// </summary>
    /// <param name="owner">The type declaring the method.</param>
    /// <param name="methodName">The method name.</param>
    /// <returns>This builder.</returns>
    public ConsumerBuilder Display(Type owner, string methodName) {
        MethodInfo target = Resolve(owner, methodName);
        ParameterInfo[] parameters = target.GetParameters();

        if (target.ReturnType != typeof(string) || parameters.Length != 1 || parameters[0].ParameterType != enumType) {
            throw Shape(target, $"be 'string ({enumType.Name})'");
        }

        Patcher.Patch(target, Stock(nameof(EnumConsumerInjections.DisplayHead)), At.Head);
        return this;
    }

    /// <summary>
    ///     Registers a method that reports whether a value is a real member. It must be
    ///     <c>bool (TEnum)</c>. Concord answers true for an added member.
    /// </summary>
    /// <param name="owner">The type declaring the method.</param>
    /// <param name="methodName">The method name.</param>
    /// <returns>This builder.</returns>
    public ConsumerBuilder IsDefined(Type owner, string methodName) {
        MethodInfo target = Resolve(owner, methodName);
        ParameterInfo[] parameters = target.GetParameters();

        if (target.ReturnType != typeof(bool) || parameters.Length != 1 || parameters[0].ParameterType != enumType) {
            throw Shape(target, $"be 'bool ({enumType.Name})'");
        }

        Patcher.Patch(target, Stock(nameof(EnumConsumerInjections.IsDefinedHead)), At.Head);
        return this;
    }

    private static MethodInfo Resolve(Type owner, string methodName) {
        MethodInfo? target = owner.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        if (target is null) {
            throw new ConcordEnumException(
                "CONC137",
                $"'{owner.FullName}' declares no method named '{methodName}'.");
        }

        return target;
    }

    private static ConcordEnumException Shape(MethodInfo target, string expectation) {
        return new ConcordEnumException(
            "CONC137",
            $"Consumer '{target.DeclaringType?.FullName}.{target.Name}' must {expectation}.");
    }

    private MethodInfo Stock(string name) {
        return typeof(EnumConsumerInjections).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(enumType);
    }
}

internal static class EnumConsumerInjections {
    public static void ValuesArrayTail<TEnum>(ControlHandle<TEnum[]> ch)
        where TEnum : struct, Enum {
        IReadOnlyList<(long Value, string Name)> added = ExtendedEnumRegistry.MembersOf(typeof(TEnum));
        if (added.Count == 0) {
            return;
        }

        TEnum[] original = ch.ReturnValue ?? [];
        TEnum[] combined = new TEnum[original.Length + added.Count];
        original.CopyTo(combined, 0);

        for (int i = 0; i < added.Count; i++) {
            combined[original.Length + i] = ToEnum<TEnum>(added[i].Value);
        }

        ch.ReturnValue = combined;
    }

    public static void ValuesEnumerableTail<TEnum>(ControlHandle<IEnumerable<TEnum>> ch)
        where TEnum : struct, Enum {
        IReadOnlyList<(long Value, string Name)> added = ExtendedEnumRegistry.MembersOf(typeof(TEnum));
        if (added.Count == 0) {
            return;
        }

        List<TEnum> combined = new List<TEnum>();
        if (ch.ReturnValue is not null) {
            combined.AddRange(ch.ReturnValue);
        }

        foreach ((long value, string _) in added) {
            combined.Add(ToEnum<TEnum>(value));
        }

        ch.ReturnValue = combined;
    }

    public static void ParseHead<TEnum>(ControlHandle<TEnum> ch, string name)
        where TEnum : struct, Enum {
        foreach ((long value, string memberName) in ExtendedEnumRegistry.MembersOf(typeof(TEnum))) {
            if (!string.Equals(memberName, name, StringComparison.Ordinal)) {
                continue;
            }

            ch.ReturnValue = ToEnum<TEnum>(value);
            ch.Cancel();
            return;
        }
    }

    public static void DisplayHead<TEnum>(ControlHandle<string> ch, TEnum kind)
        where TEnum : struct, Enum {
        long raw = Convert.ToInt64(kind, CultureInfo.InvariantCulture);

        foreach ((long value, string memberName) in ExtendedEnumRegistry.MembersOf(typeof(TEnum))) {
            if (value != raw) {
                continue;
            }

            ch.ReturnValue = memberName;
            ch.Cancel();
            return;
        }
    }

    public static void IsDefinedHead<TEnum>(ControlHandle<bool> ch, TEnum kind)
        where TEnum : struct, Enum {
        long raw = Convert.ToInt64(kind, CultureInfo.InvariantCulture);

        foreach ((long value, string _) in ExtendedEnumRegistry.MembersOf(typeof(TEnum))) {
            if (value != raw) {
                continue;
            }

            ch.ReturnValue = true;
            ch.Cancel();
            return;
        }
    }

    private static TEnum ToEnum<TEnum>(long value)
        where TEnum : struct, Enum {
        return (TEnum)Enum.ToObject(
            typeof(TEnum),
            Convert.ChangeType(value, Enum.GetUnderlyingType(typeof(TEnum)), CultureInfo.InvariantCulture));
    }
}
