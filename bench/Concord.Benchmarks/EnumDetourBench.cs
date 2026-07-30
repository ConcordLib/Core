using BenchmarkDotNet.Attributes;
using Concord.Orchestration;

namespace Concord.Benchmarks;

public enum BenchWeather {
    Clear,
    Rain,
    Storm,
}

[Patch]
public abstract class BenchWeatherExtension : ExtendedEnum<BenchWeather> {
    public static BenchWeather Frozen;
}

public sealed class BenchEnumStore : IEnumValueStore {
    private Dictionary<string, long> saved = new Dictionary<string, long>(StringComparer.Ordinal);

    public bool TryLoad(out IReadOnlyDictionary<string, long> map) {
        map = saved;
        return true;
    }

    public void Save(IReadOnlyDictionary<string, long> map) {
        saved = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, long> entry in map) {
            saved[entry.Key] = entry.Value;
        }
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class EnumDetourBench {
    private readonly DayOfWeek unextended = DayOfWeek.Monday;

    [Params(false, true)]
    public bool DetoursInstalled { get; set; }

    [GlobalSetup]
    public void Setup() {
        if (!DetoursInstalled) {
            return;
        }

        ExtendedEnumRegistry.UseStore(new BenchEnumStore());
        PatchDeclarationScanner.ScanExtendedEnums([typeof(BenchWeatherExtension)]);
        CoreLibEnumDetours.Install();
    }

    [GlobalCleanup]
    public void Cleanup() {
        CoreLibEnumDetours.Uninstall();
    }

    [Benchmark]
    public string? GetNameUnextended() {
        return Enum.GetName(typeof(DayOfWeek), unextended);
    }

    [Benchmark]
    public bool IsDefinedUnextended() {
        return Enum.IsDefined(typeof(DayOfWeek), unextended);
    }

    [Benchmark]
    public string FormatUnextended() {
        return Enum.Format(typeof(DayOfWeek), unextended, "G");
    }
}
