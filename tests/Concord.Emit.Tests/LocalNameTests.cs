using System.Reflection;
using Concord.Emit.Tests.ForeignTargets;
using Xunit;

namespace Concord.Emit.Tests;

public class NameHost {
    // Two int locals, each address-taken by ToString() so Release cannot schedule either onto the
    // stack. Both are the same type, so only the name tells them apart in either configuration.
    public string TwoNamedLocals(int seed) {
        int doubled = seed * 2;
        int tripled = seed * 3;
        return string.Concat(doubled.ToString(), tripled.ToString());
    }

    // One source name over two slots. The types differ, so a Release build cannot collapse them
    // into a shared slot the way it would for two same-typed loop counters.
    public string SameNameTwice(int seed) {
        string text;
        {
            int shared = seed * 2;
            text = shared.ToString();
        }

        {
            long shared = seed * 3L;
            text += shared.ToString();
        }

        return text;
    }
}

public class NameMethods {
    public void ReadDoubled([Local(Name = "doubled")] int value) {
        LocalRecorder.SeenInt = value;
    }

    public void ReadTripled([Local(Name = "tripled")] int value) {
        LocalRecorder.SeenInt = value;
    }

    public void ReadMissing([Local(Name = "nosuchlocal")] int value) {
        LocalRecorder.SeenInt = value;
    }

    public void ReadShared([Local(Name = "shared")] int value) {
        LocalRecorder.SeenInt = value;
    }

    public void ReadForeign([Local(Name = "anything")] int value) {
        LocalRecorder.SeenInt = value;
    }
}

// PathResolver and the name cache behind it are process-wide, so these tests get a collection of
// their own rather than running beside anything else in the assembly.
[CollectionDefinition("LocalNames", DisableParallelization = true)]
public sealed class LocalNamesCollection;

[Collection("LocalNames")]
public sealed class LocalNameTests {
    private static Func<NameHost, int, string> Compose(string host, string injection) {
        MethodBase target = typeof(NameHost).GetMethod(host)!;
        MethodBase method = typeof(NameMethods).GetMethod(injection)!;
        ComposeResult result = WrapperComposer.Compose(
            target, [new Injection(method, new InjectAt.Return(), "test-" + injection, 0)]);

        return result.Wrapper.CreateDelegate<Func<NameHost, int, string>>();
    }

    private static ConcordEmitException ComposeFails(string host, string injection) {
        MethodBase target = typeof(NameHost).GetMethod(host)!;
        MethodBase method = typeof(NameMethods).GetMethod(injection)!;

        return Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(
            target, [new Injection(method, new InjectAt.Return(), "test-" + injection, 0)]));
    }

    [Fact]
    public void Name_BindsTheLocalWithThatSourceName() {
        Func<NameHost, int, string> run = Compose(
            nameof(NameHost.TwoNamedLocals), nameof(NameMethods.ReadDoubled));

        LocalRecorder.SeenInt = 0;
        run(new NameHost(), 5);

        Assert.Equal(10, LocalRecorder.SeenInt);
    }

    [Fact]
    public void Name_DiscriminatesBetweenTwoLocalsOfTheSameType() {
        Func<NameHost, int, string> run = Compose(
            nameof(NameHost.TwoNamedLocals), nameof(NameMethods.ReadTripled));

        LocalRecorder.SeenInt = 0;
        run(new NameHost(), 5);

        Assert.Equal(15, LocalRecorder.SeenInt);
    }

    [Fact]
    public void Name_ThatTheSymbolsDoNotHold_Throws() {
        ConcordEmitException ex = ComposeFails(
            nameof(NameHost.TwoNamedLocals), nameof(NameMethods.ReadMissing));

        Assert.Equal("CONC152", ex.Code);
        Assert.Contains("'doubled'", ex.Message);
        Assert.Contains("'tripled'", ex.Message);
    }

    [Fact]
    public void Name_SpreadAcrossTwoSlots_Throws() {
        ConcordEmitException ex = ComposeFails(
            nameof(NameHost.SameNameTwice), nameof(NameMethods.ReadShared));

        Assert.Equal("CONC163", ex.Code);
        Assert.Contains("2 slots", ex.Message);
        Assert.Contains("IL_", ex.Message);
        Assert.Contains("Ordinal", ex.Message);
    }

    // Cecil opens the dll with FileShare.Read, which blocks writes and deletes. Nothing may still
    // hold that handle once the names are back, or a mod rebuild fails while the game is running.
    // Read against a copy, not the loaded assembly: on Windows the CLR keeps its own loader handle
    // on everything it loaded, so a share-none open of the running dll is refused regardless.
    [Fact]
    public void Name_LeavesTheTargetAssemblyWritable() {
        MethodBase target = typeof(NameHost).GetMethod(nameof(NameHost.TwoNamedLocals))!;
        Module module = target.Module;
        string copy = Path.Combine(Path.GetTempPath(), "concord-localnames-" + Guid.NewGuid().ToString("N") + ".dll");
        File.Copy(module.Assembly.Location, copy);

        try {
            LocalNames.PathResolver = probed => probed == module ? copy : null;
            Assert.NotEmpty(LocalNames.For(target));

            using (FileStream exclusive = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                Assert.True(exclusive.CanWrite);
            }
        } finally {
            LocalNames.PathResolver = null;
            File.Delete(copy);
        }
    }

    [Fact]
    public void Name_WithoutSymbols_Throws() {
        MethodBase target = typeof(ForeignLocalTarget).GetMethod(nameof(ForeignLocalTarget.Work))!;
        MethodBase method = typeof(NameMethods).GetMethod(nameof(NameMethods.ReadForeign))!;
        Module foreign = target.Module;

        LocalNames.PathResolver = module =>
            module == foreign ? "/nonexistent/concord-has-no-symbols-here.dll" : null;
        try {
            ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(
                target, [new Injection(method, new InjectAt.Return(), "test-foreign", 0)]));

            Assert.Equal("CONC151", ex.Code);
            Assert.Contains("/nonexistent/concord-has-no-symbols-here.dll", ex.Message);
        } finally {
            LocalNames.PathResolver = null;
        }
    }
}
