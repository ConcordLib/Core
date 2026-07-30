using Concord.Orchestration;
using Xunit;

namespace Concord.Enums.Tests;

public abstract class ApplyWeather : ExtendedEnum<WeatherKind> {
    public static WeatherKind Frozen;
}

public abstract class ApplyAbility : ExtendedEnum<Ability> {
    public static Ability Glide;
}

[Patch]
public abstract class ReloadWeather : ExtendedEnum<WeatherKind> {
    public static WeatherKind Thaw;
}

[Collection(RegistryCollection.Name)]
public sealed class ApplyTests {
    [Fact]
    public void Apply_AssignsTheDeclarationField() {
        ExtendedEnumRegistry.ResetForTests();
        ExtendedEnumRegistry.UseStore(new FakeStore());

        ExtendedEnumRegistry.ApplyTo(ExtendedEnumRegistry.ReadDeclaration(typeof(ApplyWeather)));

        Assert.NotEqual(default, ApplyWeather.Frozen);
        Assert.Equal(3, (int)ApplyWeather.Frozen);
    }

    [Fact]
    public void Apply_MakesFlagMathWorkWithNoPatching() {
        ExtendedEnumRegistry.ResetForTests();
        ExtendedEnumRegistry.UseStore(new FakeStore());

        ExtendedEnumRegistry.ApplyTo(ExtendedEnumRegistry.ReadDeclaration(typeof(ApplyAbility)));

        Ability combined = Ability.Swim | ApplyAbility.Glide;

        Assert.True(combined.HasFlag(Ability.Swim));
        Assert.True(combined.HasFlag(ApplyAbility.Glide));
        Assert.False(combined.HasFlag(Ability.Fly));
        Assert.Equal(Ability.Swim, combined & Ability.Swim);
    }

    [Fact]
    public void Reload_RestoresSavedValuesAndReassignsFields() {
        ExtendedEnumRegistry.ResetForTests();
        ExtendedEnumRegistry.UseStore(new FakeStore());
        PatchDeclarationScanner.ScanExtendedEnums([typeof(ReloadWeather)]);

        Assert.Equal(3, (int)ReloadWeather.Thaw);

        Dictionary<string, long> saved = new Dictionary<string, long>(StringComparer.Ordinal) {
            [typeof(ReloadWeather).FullName + ".Thaw"] = 9,
        };

        ExtendedEnumRegistry.Reload(saved);

        Assert.Equal(9, (int)ReloadWeather.Thaw);
        Assert.Contains(typeof(WeatherKind), ExtendedEnumRegistry.ExtendedEnums());
    }
}
