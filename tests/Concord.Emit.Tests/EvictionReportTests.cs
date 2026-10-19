using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class EvictionReportTests {
    [Fact]
    public void TheFirstReportNamesTheModTheMethodAndTheReason() {
        string? report = EvictionReport.Compose("[marker]", Target(), Rejection("owner.names.the.mod"));

        Assert.NotNull(report);
        Assert.StartsWith("[marker] ", report);
        Assert.Contains("owner.names.the.mod", report);
        Assert.Contains(nameof(StreamTargets.Target), report);
        Assert.Contains("a ret inside a fault handler", report);
        Assert.Contains("https://github.com/ConcordLib/Core/issues", report);
    }

    [Fact]
    public void TheSameEvictionReportsOnceSoARebuildDoesNotRepeatIt() {
        Assert.NotNull(EvictionReport.Compose("[marker]", Target(), Rejection("owner.reports.once")));

        Assert.Null(EvictionReport.Compose("[marker]", Target(), Rejection("owner.reports.once")));
    }

    [Fact]
    public void ADifferentCodeOnTheSameTargetIsItsOwnReport() {
        Assert.NotNull(EvictionReport.Compose("[marker]", Target(), Rejection("owner.two.codes", "CONC001")));

        Assert.NotNull(EvictionReport.Compose("[marker]", Target(), Rejection("owner.two.codes", "CONC002")));
    }

    [Fact]
    public void ANullArgumentIsRefusedRatherThanLoggedAsNothing() {
        RejectedInjection rejection = Rejection("owner.null.checks");

        Assert.Throws<ArgumentNullException>(() => EvictionReport.Compose(null!, Target(), rejection));
        Assert.Throws<ArgumentNullException>(() => EvictionReport.Compose("[marker]", null!, rejection));
        Assert.Throws<ArgumentNullException>(() => EvictionReport.Compose("[marker]", Target(), null!));
    }

    private static RejectedInjection Rejection(string owner, string code = "CONC174") {
        return new RejectedInjection(owner, code, "a ret inside a fault handler");
    }

    private static MethodBase Target() {
        return typeof(StreamTargets).GetMethod(nameof(StreamTargets.Target))!;
    }
}
