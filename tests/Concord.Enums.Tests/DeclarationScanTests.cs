using System.Reflection;
using Concord.Emit;
using Concord.Orchestration;
using Xunit;

namespace Concord.Enums.Tests;

public abstract class ScanWeather : ExtendedEnum<WeatherKind> {
    public static readonly string Unrelated = "ignored";

    public static WeatherKind Frozen;
    public static WeatherKind Ashfall;
}

public abstract class ScanPinned : ExtendedEnum<WeatherKind> {
    public const WeatherKind Fixed = (WeatherKind)32;
}

public abstract class ScanRenamed : ExtendedEnum<WeatherKind> {
    [EnumMember("legacy.weather.frozen")]
    public static WeatherKind FrozenRenamed;
}

[Patch]
public abstract class ScanEnumWithInject : ExtendedEnum<WeatherKind> {
    public static WeatherKind Sleet;

    [Inject(At.Head, "Anything")]
    public void OnAnything(ControlHandle ch) { }
}

public abstract class ScanDuplicateA : ExtendedEnum<WeatherKind> {
    [EnumMember("scan.duplicate.id")]
    public static WeatherKind First;
}

public abstract class ScanDuplicateB : ExtendedEnum<WeatherKind> {
    [EnumMember("scan.duplicate.id")]
    public static WeatherKind Second;
}

public abstract class ScanInstanceField : ExtendedEnum<WeatherKind> {
    public WeatherKind Instance;
}

public sealed class NullPatchApplier : IPatchApplier {
    public void ApplyPatch(MethodBase target, Injection injection) { }
}

public sealed class NullAttachedPropertyRegistry : IAttachedPropertyRegistry {
    public void RegisterAttachedProperty(Type baseType, string name, Type valueType) { }
}

[Collection(RegistryCollection.Name)]
public sealed class DeclarationScanTests {
    [Fact]
    public void StaticEnumFields_BecomeMembers() {
        IReadOnlyList<ExtendedEnumMember> members = ExtendedEnumRegistry.ReadDeclaration(typeof(ScanWeather));

        Assert.Equal(2, members.Count);
        Assert.Contains(members, m => m.Id.EndsWith(".Frozen", StringComparison.Ordinal));
        Assert.Contains(members, m => m.Id.EndsWith(".Ashfall", StringComparison.Ordinal));
    }

    [Fact]
    public void UnrelatedStaticFields_AreIgnored() {
        IReadOnlyList<ExtendedEnumMember> members = ExtendedEnumRegistry.ReadDeclaration(typeof(ScanWeather));

        Assert.DoesNotContain(members, m => m.Field.Name == nameof(ScanWeather.Unrelated));
    }

    [Fact]
    public void ConstInitializer_PinsTheValue() {
        IReadOnlyList<ExtendedEnumMember> members = ExtendedEnumRegistry.ReadDeclaration(typeof(ScanPinned));

        Assert.Single(members);
        Assert.Equal(32L, members[0].PinnedValue);
    }

    [Fact]
    public void EnumMemberAttribute_OverridesTheId() {
        IReadOnlyList<ExtendedEnumMember> members = ExtendedEnumRegistry.ReadDeclaration(typeof(ScanRenamed));

        Assert.Single(members);
        Assert.Equal("legacy.weather.frozen", members[0].Id);
    }

    [Fact]
    public void InstanceFieldTypedAsTheEnum_IsRejected() {
        ConcordEnumException ex = Assert.Throws<ConcordEnumException>(
            () => ExtendedEnumRegistry.ReadDeclaration(typeof(ScanInstanceField)));

        Assert.Equal("CONC139", ex.Code);
    }

    [Fact]
    public void EnumDeclarationCarryingAnInjection_IsRejected() {
        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => PatchDeclarationScanner.ScanType(
                typeof(ScanEnumWithInject),
                new NullPatchApplier(),
                new NullAttachedPropertyRegistry()));

        Assert.Contains("CONC138", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDeclarationsSharingAnId_AreRejected() {
        ExtendedEnumRegistry.RegisterDeclaration(typeof(ScanDuplicateA));

        ConcordEnumException ex = Assert.Throws<ConcordEnumException>(
            () => ExtendedEnumRegistry.RegisterDeclaration(typeof(ScanDuplicateB)));

        Assert.Equal("CONC140", ex.Code);
    }
}
