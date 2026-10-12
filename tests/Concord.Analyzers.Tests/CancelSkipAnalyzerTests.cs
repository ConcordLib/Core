using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class CancelSkipAnalyzerTests {
    private const string CancelAttributeSource = """
        using System;

        namespace Concord {
            public enum At {
                Head,
                Return,
                Tail,
                Around,
                Constant,
                Argument,
                Transpiler,
                TranspilerFinal,
                Finally,
                Local,
                Catch
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PatchAttribute : Attribute {
                public PatchAttribute() {
                }

                public PatchAttribute(Type target) {
                }

                public PatchAttribute(string targetTypeName) {
                }
            }

            [AttributeUsage(AttributeTargets.Method)]
            public sealed class InjectAttribute : Attribute {
                public InjectAttribute(At at, string method, uint by = 0, Type[] parameterTypes = null) {
                }

                public InjectAttribute(string method, Type invokeDeclaringType, string invokeDeclaringMethod, At shift, uint by = 0, Type[] targetParameterTypes = null, Type[] invokeParameterTypes = null, uint arg = 0) {
                }
            }

            public sealed class ControlHandle {
                public void Cancel() {
                }

                public void Cancel(bool skipRemainingHeads) {
                }

                public void SetState<TState>(TState value) {
                }

                public TState GetState<TState>() => default;
            }

            public sealed class ControlHandle<T> {
                public void Cancel() {
                }

                public void Cancel(bool skipRemainingHeads) {
                }

                public void SetState<TState>(TState value) {
                }

                public TState GetState<TState>() => default;
            }
        }

        """;

    [Fact]
    public async Task CancelSkip_AtHead_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await DiagnosticsForAsync(
            "Concord.At.Head, nameof(Total)",
            "control.Cancel(true);");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CancelNoSkip_AtHead_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await DiagnosticsForAsync(
            "Concord.At.Head, nameof(Total)",
            "control.Cancel(false);");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("Concord.At.Tail, nameof(Total)")]
    [InlineData("Concord.At.Return, nameof(Total)")]
    [InlineData("Concord.At.Finally, nameof(Total)")]
    public async Task BareCancel_OutsideHead_ReportsNothing(string position) {
        ImmutableArray<Diagnostic> diagnostics = await DiagnosticsForAsync(position, "control.Cancel();");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task BareCancel_AtCatch_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await DiagnosticsForAsync(
            "Concord.At.Catch, nameof(Total)",
            "control.Cancel();",
            "System.Exception error, ");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("Concord.At.Tail, nameof(Total)", "At.Tail")]
    [InlineData("Concord.At.Return, nameof(Total)", "At.Return")]
    [InlineData("Concord.At.Finally, nameof(Total)", "At.Finally")]
    public async Task CancelSkip_OutsideHead_ReportsCONCORD052(string position, string reported) {
        ImmutableArray<Diagnostic> diagnostics = await DiagnosticsForAsync(position, "control.Cancel(true);");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.CancelSkipPositionDiagnosticId, diagnostic.Id);
        Assert.Contains(reported, diagnostic.GetMessage());
        Assert.Contains("Cancel(bool)", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("Concord.At.Tail, nameof(Total)", "At.Tail")]
    [InlineData("Concord.At.Return, nameof(Total)", "At.Return")]
    [InlineData("Concord.At.Finally, nameof(Total)", "At.Finally")]
    public async Task CancelNoSkip_OutsideHead_ReportsCONCORD052(string position, string reported) {
        ImmutableArray<Diagnostic> diagnostics = await DiagnosticsForAsync(position, "control.Cancel(false);");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.CancelSkipPositionDiagnosticId, diagnostic.Id);
        Assert.Contains(reported, diagnostic.GetMessage());
        Assert.Contains("Cancel(bool)", diagnostic.GetMessage());
    }

    [Fact]
    public async Task CancelSkip_AtCatch_ReportsCONCORD052() {
        ImmutableArray<Diagnostic> diagnostics = await DiagnosticsForAsync(
            "Concord.At.Catch, nameof(Total)",
            "control.Cancel(true);",
            "System.Exception error, ");

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.CancelSkipPositionDiagnosticId, diagnostic.Id);
        Assert.Contains("At.Catch", diagnostic.GetMessage());
    }

    [Fact]
    public async Task CancelSkip_AtCallSiteHead_ReportsCONCORD052() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            CancelAttributeSource +
            """

            public static class Audit {
                public static void Mark() { }
            }

            public class Shop {
                public void Total(int listed) { Audit.Mark(); }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(nameof(Total), typeof(Audit), nameof(Audit.Mark), Concord.At.Head)]
                private void BeforeMark(Concord.ControlHandle control) { control.Cancel(true); }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.CancelSkipPositionDiagnosticId, diagnostic.Id);
        Assert.Contains("At.Invoke/At.Head", diagnostic.GetMessage());
    }

    [Fact]
    public async Task CancelOnForeignType_OutsideHead_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            CancelAttributeSource +
            """

            public sealed class Token {
                public void Cancel(bool force) { }
            }

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Tail, nameof(Total))]
                private void AfterTotal() { new Token().Cancel(true); }
            }
            """);

        Assert.Empty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> DiagnosticsForAsync(
        string position, string body, string leadingParameters = "") {
        return InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            CancelAttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(POSITION)]
                private void OnTotal(PARAMETERSConcord.ControlHandle<int> control) { BODY }
            }
            """
                .Replace("POSITION", position)
                .Replace("PARAMETERS", leadingParameters)
                .Replace("BODY", body));
    }
}
