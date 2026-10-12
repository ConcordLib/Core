using System.Reflection;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public class PairingStampTests {
    private const string RuntimeBuildKey = "ConcordBuild";

    [Fact]
    public void AdapterStampNamesTheConcordBuildItCompiledAgainst() {
        Assert.Equal(SourceRevision(typeof(Patcher).Assembly), DeclaredRuntimeBuild(typeof(AdapterEntryPoint).Assembly));
    }

    private static string? SourceRevision(Assembly assembly) {
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (version == null) {
            return null;
        }

        int plus = version.IndexOf('+');
        return plus < 0 ? null : version[(plus + 1)..];
    }

    private static string? DeclaredRuntimeBuild(Assembly assembly) {
        foreach (AssemblyMetadataAttribute stamp in assembly.GetCustomAttributes<AssemblyMetadataAttribute>()) {
            if (stamp.Key == RuntimeBuildKey) {
                return stamp.Value;
            }
        }

        return null;
    }
}
