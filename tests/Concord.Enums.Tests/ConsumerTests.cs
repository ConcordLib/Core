using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Enums.Tests;

public static class WeatherUtility {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeatherKind[] All() => [WeatherKind.Clear, WeatherKind.Rain, WeatherKind.Storm];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeatherKind Parse(string name) => (WeatherKind)Enum.Parse(typeof(WeatherKind), name);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Display(WeatherKind kind) => kind.ToString();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsValid(WeatherKind kind) => Enum.IsDefined(typeof(WeatherKind), kind);

    public static int WrongShape(int unrelated) => unrelated;
}

public abstract class ConsumerWeather : ExtendedEnum<WeatherKind> {
    public static WeatherKind Frozen;
}

[Collection(RegistryCollection.Name)]
public sealed class ConsumerTests {
    [Fact]
    public void Values_IncludesTheAddedMember() {
        ApplyMembers();
        EnumConsumers.For(typeof(WeatherKind)).Values(typeof(WeatherUtility), nameof(WeatherUtility.All));

        Assert.Contains(ConsumerWeather.Frozen, WeatherUtility.All());
    }

    [Fact]
    public void Parse_ResolvesTheAddedMember() {
        ApplyMembers();
        EnumConsumers.For(typeof(WeatherKind)).Parse(typeof(WeatherUtility), nameof(WeatherUtility.Parse));

        Assert.Equal(ConsumerWeather.Frozen, WeatherUtility.Parse("Frozen"));
    }

    [Fact]
    public void Display_NamesTheAddedMember() {
        ApplyMembers();
        EnumConsumers.For(typeof(WeatherKind)).Display(typeof(WeatherUtility), nameof(WeatherUtility.Display));

        Assert.Equal("Frozen", WeatherUtility.Display(ConsumerWeather.Frozen));
    }

    [Fact]
    public void IsDefined_AcceptsTheAddedMember() {
        ApplyMembers();
        EnumConsumers.For(typeof(WeatherKind)).IsDefined(typeof(WeatherUtility), nameof(WeatherUtility.IsValid));

        Assert.True(WeatherUtility.IsValid(ConsumerWeather.Frozen));
    }

    [Fact]
    public void WrongShape_ThrowsConc137() {
        ApplyMembers();

        ConcordEnumException ex = Assert.Throws<ConcordEnumException>(
            () => EnumConsumers.For(typeof(WeatherKind)).Values(typeof(WeatherUtility), nameof(WeatherUtility.WrongShape)));

        Assert.Contains("CONC137", ex.ToString(), StringComparison.Ordinal);
    }

    private static void ApplyMembers() {
        ExtendedEnumRegistry.ResetForTests();
        ExtendedEnumRegistry.UseStore(new FakeStore());
        ExtendedEnumRegistry.ApplyTo(ExtendedEnumRegistry.ReadDeclaration(typeof(ConsumerWeather)));
    }
}
