using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class BoundLog {
    public static readonly List<string> Entries = [];

    public static void Clear() {
        Entries.Clear();
    }

    public static void Enter(int sectionId) {
        Entries.Add("enter:" + sectionId);
    }

    public static void Note(string text) {
        Entries.Add("note:" + text);
    }
}

public static class BoundTargets {
    public static int Double(int x) {
        return x * 2;
    }

    public static int Triple(int x) {
        return x * 3;
    }
}

public static class BoundInjectionMethods {
    public static void Enter([Bound] int sectionId) {
        BoundLog.Enter(sectionId);
    }

    public static void EnterNamed([Bound] string label) {
        BoundLog.Note(label);
    }
}

public sealed class BoundConstantTests {
    [Fact]
    public void BoundParameter_EmitsTheRegisteredValue() {
        ComposeResult result = Compose(nameof(BoundTargets.Double), nameof(BoundInjectionMethods.Enter), "sectionId", 77);
        BoundLog.Clear();

        Assert.Equal(10, result.Wrapper.Invoke(null, [5]));
        Assert.Equal(["enter:77"], BoundLog.Entries);
    }

    [Fact]
    public void BoundParameter_GivesEachTargetItsOwnValue() {
        ComposeResult first = Compose(nameof(BoundTargets.Double), nameof(BoundInjectionMethods.Enter), "sectionId", 1);
        ComposeResult second = Compose(nameof(BoundTargets.Triple), nameof(BoundInjectionMethods.Enter), "sectionId", 2);
        BoundLog.Clear();

        first.Wrapper.Invoke(null, [5]);
        second.Wrapper.Invoke(null, [5]);

        Assert.Equal(["enter:1", "enter:2"], BoundLog.Entries);
    }

    [Fact]
    public void BoundParameter_SupportsStrings() {
        ComposeResult result = Compose(nameof(BoundTargets.Double), nameof(BoundInjectionMethods.EnterNamed), "label", "jobdriver");
        BoundLog.Clear();

        result.Wrapper.Invoke(null, [5]);

        Assert.Equal(["note:jobdriver"], BoundLog.Entries);
    }

    [Fact]
    public void BoundParameter_WithNoValueSupplied_ThrowsCONC136() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            Compose(nameof(BoundTargets.Double), nameof(BoundInjectionMethods.Enter), null, null));

        Assert.Equal("CONC136", ex.Code);
    }

    [Fact]
    public void BoundParameter_WithAnUnknownName_ThrowsCONC136() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            Compose(nameof(BoundTargets.Double), nameof(BoundInjectionMethods.Enter), "nope", 1));

        Assert.Equal("CONC136", ex.Code);
    }

    [Fact]
    public void BoundParameter_WithAMismatchedType_ThrowsCONC137() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            Compose(nameof(BoundTargets.Double), nameof(BoundInjectionMethods.Enter), "sectionId", "not an int"));

        Assert.Equal("CONC137", ex.Code);
    }

    private static ComposeResult Compose(string targetName, string injectionName, string? boundName, object? boundValue) {
        MethodBase target = typeof(BoundTargets).GetMethod(targetName)!;
        MethodBase injectionMethod = typeof(BoundInjectionMethods).GetMethod(injectionName)!;

        Injection injection = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);
        if (boundName is not null) {
            injection.BoundArguments = new Dictionary<string, object?> { [boundName] = boundValue };
        }

        return WrapperComposer.Compose(target, [injection]);
    }
}
