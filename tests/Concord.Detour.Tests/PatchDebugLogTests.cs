using System.Reflection;
using Xunit;

namespace Concord.Detour.Tests;

public sealed class PatchDebugLogTests {
    [Fact]
    public void Append_WritesTargetAndIl() {
        string directory = Path.Combine(Path.GetTempPath(), "ConcordPatchDebug_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "Concord.PatchDebug.log");
        MethodBase target = typeof(Targets).GetMethod(nameof(Targets.OriginalC))!;

        try {
            Directory.CreateDirectory(directory);

            PatchDebugLog.Append(path, target, "000  ret");

            string contents = File.ReadAllText(path);
            Assert.Contains("Concord.Detour.Tests.Targets.OriginalC", contents);
            Assert.Contains("000  ret", contents);
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, true);
            }
        }
    }
}
