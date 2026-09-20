using System.Reflection;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Concord.Detour.Tests;

public sealed class AssemblyImageTests {
    [Fact]
    public void WithFreshModuleId_GivesEachLoadItsOwnModuleId() {
        byte[] image = File.ReadAllBytes(typeof(AssemblyImage).Assembly.Location);

        Assembly first = Assembly.Load(AssemblyImage.WithFreshModuleId(image));
        Assembly second = Assembly.Load(AssemblyImage.WithFreshModuleId(image));

        Assert.NotEqual(typeof(AssemblyImage).Module.ModuleVersionId, first.ManifestModule.ModuleVersionId);
        Assert.NotEqual(first.ManifestModule.ModuleVersionId, second.ManifestModule.ModuleVersionId);
        Assert.NotNull(first.GetType(typeof(AssemblyImage).FullName!));
    }

    [Fact]
    public void WithFreshModuleId_KeepsEveryOtherByte() {
        byte[] image = File.ReadAllBytes(typeof(AssemblyImage).Assembly.Location);
        byte[] rewritten = AssemblyImage.WithFreshModuleId(image);

        Assert.Equal(image.Length, rewritten.Length);
        int differing = 0;
        for (int i = 0; i < image.Length; i++) {
            if (image[i] != rewritten[i]) {
                differing++;
            }
        }

        Assert.InRange(differing, 1, 16);
        Assert.Equal(DebugEntries(image), DebugEntries(rewritten));
    }

    [Fact]
    public void WithFreshModuleId_RejectsBytesThatAreNotAnAssembly() {
        Assert.Throws<BadImageFormatException>(() => AssemblyImage.WithFreshModuleId(new byte[512]));
    }

    private static string DebugEntries(byte[] image) {
        using PEReader reader = new PEReader(new MemoryStream(image));
        return string.Join(",", reader.ReadDebugDirectory().Select(entry => entry.Type + ":" + entry.DataSize));
    }
}
