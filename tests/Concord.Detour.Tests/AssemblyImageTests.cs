using System.Reflection;
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

        int first = -1;
        int last = -1;
        for (int i = 0; i < image.Length; i++) {
            if (image[i] != rewritten[i]) {
                first = first < 0 ? i : first;
                last = i;
            }
        }

        Assert.True(first >= 0, "the module id was not rewritten");
        Assert.InRange(last - first + 1, 1, 16);
    }

    [Fact]
    public void WithFreshModuleId_RejectsBytesThatAreNotAnAssembly() {
        Assert.Throws<BadImageFormatException>(() => AssemblyImage.WithFreshModuleId(new byte[512]));
    }
}
