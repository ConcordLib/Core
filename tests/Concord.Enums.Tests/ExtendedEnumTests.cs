using Xunit;

namespace Concord.Enums.Tests;

public enum WeatherKind {
    Clear,
    Rain,
    Storm,
}

public abstract class WeatherExtension : ExtendedEnum<WeatherKind> {
    public static WeatherKind Frozen;
}

public sealed class ExtendedEnumTests {
    [Fact]
    public void DeclarationBase_CarriesTheEnumType() {
        Type? baseType = typeof(WeatherExtension).BaseType;

        Assert.NotNull(baseType);
        Assert.True(baseType!.IsGenericType);
        Assert.Equal(typeof(ExtendedEnum<>), baseType.GetGenericTypeDefinition());
        Assert.Equal(typeof(WeatherKind), baseType.GetGenericArguments()[0]);
    }
}
