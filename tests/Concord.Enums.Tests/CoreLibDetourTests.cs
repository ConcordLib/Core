using System.Reflection;
using Xunit;

namespace Concord.Enums.Tests;

public abstract class DetourWeather : ExtendedEnum<WeatherKind> {
    public static WeatherKind Frozen;
}

[Collection(RegistryCollection.Name)]
public sealed class CoreLibDetourTests {
    [Fact]
    public void GetValues_IncludesTheAddedMember() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Array values = Enum.GetValues(typeof(WeatherKind));

            Assert.Contains(DetourWeather.Frozen, values.Cast<WeatherKind>());
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void GetValues_OnAnUnextendedEnum_IsUnchanged() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Array values = Enum.GetValues(typeof(DayOfWeek));

            Assert.Equal(7, values.Length);
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void GetNames_IncludesTheAddedMember() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            string[] names = Enum.GetNames(typeof(WeatherKind));

            Assert.Contains("Frozen", names);
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void GetNames_OnAnUnextendedEnum_IsUnchanged() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            string[] names = Enum.GetNames(typeof(DayOfWeek));

            Assert.Equal(7, names.Length);
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void GetName_NamesTheAddedMember() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.Equal("Frozen", Enum.GetName(typeof(WeatherKind), DetourWeather.Frozen));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void GetName_OnAnUnextendedEnum_IsUnchanged() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.Equal("Monday", Enum.GetName(typeof(DayOfWeek), DayOfWeek.Monday));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void IsDefined_AcceptsTheAddedMember() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.True(Enum.IsDefined(typeof(WeatherKind), DetourWeather.Frozen));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void IsDefined_OnAnUnextendedEnum_IsUnchanged() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.True(Enum.IsDefined(typeof(DayOfWeek), DayOfWeek.Monday));
            Assert.False(Enum.IsDefined(typeof(DayOfWeek), (DayOfWeek)99));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void Parse_ResolvesTheAddedMember() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.Equal(DetourWeather.Frozen, (WeatherKind)Enum.Parse(typeof(WeatherKind), "Frozen"));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void Parse_OnAnUnextendedEnum_IsUnchanged() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.Equal(DayOfWeek.Monday, (DayOfWeek)Enum.Parse(typeof(DayOfWeek), "Monday"));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

#if NET
    [Fact]
    public void TryParse_ResolvesTheAddedMember() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.True(Enum.TryParse(typeof(WeatherKind), "Frozen", out object? parsed));
            Assert.Equal(DetourWeather.Frozen, (WeatherKind)parsed!);
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void TryParse_OnAnUnextendedEnum_IsUnchanged() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.True(Enum.TryParse(typeof(DayOfWeek), "Monday", out object? parsed));
            Assert.Equal(DayOfWeek.Monday, (DayOfWeek)parsed!);
            Assert.False(Enum.TryParse(typeof(DayOfWeek), "Nonesuch", out object? _));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }
#endif

    [Fact]
    public void ToString_IsNotCovered() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.Equal("Monday", DayOfWeek.Monday.ToString());
#if NET
            Assert.Equal("3", DetourWeather.Frozen.ToString());
#else
            Assert.Equal("Frozen", DetourWeather.Frozen.ToString());
#endif
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void Format_NamesTheAddedMember() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.Equal("Frozen", Enum.Format(typeof(WeatherKind), DetourWeather.Frozen, "G"));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void Format_OnAnUnextendedEnum_IsUnchanged() {
        ApplyMembers();
        CoreLibEnumDetours.Install();

        try {
            Assert.Equal("Monday", Enum.Format(typeof(DayOfWeek), DayOfWeek.Monday, "G"));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void Install_WithoutFormat_LeavesFormatAlone() {
        ApplyMembers();
        CoreLibEnumDetours.Install(includeFormat: false);

        try {
            Assert.Contains("Frozen", Enum.GetNames(typeof(WeatherKind)));
#if NET
            Assert.Equal("3", Enum.Format(typeof(WeatherKind), DetourWeather.Frozen, "G"));
#endif
        } finally {
            CoreLibEnumDetours.Uninstall();
        }
    }

    [Fact]
    public void Uninstall_RestoresTheOriginalBehavior() {
        ApplyMembers();
        CoreLibEnumDetours.Install();
        CoreLibEnumDetours.Uninstall();

        Assert.Equal(3, Enum.GetValues(typeof(WeatherKind)).Length);
        Assert.DoesNotContain("Frozen", Enum.GetNames(typeof(WeatherKind)));
    }

    private static void ApplyMembers() {
        ExtendedEnumRegistry.ResetForTests();
        ExtendedEnumRegistry.UseStore(new FakeStore());
        ExtendedEnumRegistry.ApplyTo(ExtendedEnumRegistry.ReadDeclaration(typeof(DetourWeather)));
    }
}
