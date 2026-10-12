using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class CatchPositionDiagnosticsTests {
    private const string CatchAttributeSource = """
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
            }

            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class LocalAttribute : Attribute {
                public uint Ordinal { get; set; }

                public int Index { get; set; } = -1;

                public string Name { get; set; }
            }

            public sealed class ControlHandle {
                public void SetState<TState>(TState value) {
                }

                public TState GetState<TState>() => default;
            }

            public sealed class ControlHandle<T> {
                public void SetState<TState>(TState value) {
                }

                public TState GetState<TState>() => default;
            }

            public sealed class LocalHandle<T> {
                public T Value { get; set; }
            }
        }

        """;

    [Fact]
    public async Task Catch_WithExceptionParameter_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            CatchAttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Catch, nameof(Total))]
                private void OnThrow(System.Exception error) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Catch_WithDerivedExceptionParameter_ReportsCONCORD051() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            CatchAttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Catch, nameof(Total))]
                private void OnThrow(System.InvalidOperationException error) { }
            }
            """);

        Diagnostic reported = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.InvalidCatchExceptionDiagnosticId, reported.Id);
        Assert.Contains("error", reported.GetMessage());
    }

    [Theory]
    [InlineData("ref System.Exception error", "")]
    [InlineData("out System.Exception error", "error = null;")]
    [InlineData("in System.Exception error", "")]
    public async Task Catch_WithByRefExceptionParameter_ReportsCONCORD051(string parameter, string body) {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            CatchAttributeSource +
            $$"""

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Catch, nameof(Total))]
                private void OnThrow({{parameter}}) { {{body}} }
            }
            """);

        Diagnostic reported = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.InvalidCatchExceptionDiagnosticId, reported.Id);
        Assert.Contains("exception parameter 'error' by reference", reported.GetMessage());
    }

    [Fact]
    public async Task Catch_WithControlHandle_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            CatchAttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Catch, nameof(Total))]
                private void OnThrow(System.Exception error, Concord.ControlHandle<int> control) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Catch_WithLocalRead_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            CatchAttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Catch, nameof(Total))]
                private void OnThrow(System.Exception error, [Concord.Local] int listed) { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Catch_WithLocalHandleWrite_ReportsCONCORD048() {
        ImmutableArray<Diagnostic> diagnostics = await InjectedMemberAnalyzerTests.GetAnalyzerDiagnosticsAsync(
            CatchAttributeSource +
            """

            public class Shop {
                public int Total(int listed) { return listed + 1; }
            }

            [Concord.Patch]
            public abstract class ShopPatch : Shop {
                [Concord.Inject(Concord.At.Catch, nameof(Total))]
                private void OnThrow(System.Exception error, Concord.LocalHandle<int> listed) { listed.Value = 1; }
            }
            """);

        Diagnostic reported = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.LocalWriteAtReadOnlyPositionDiagnosticId, reported.Id);
        Assert.Contains("At.Catch", reported.GetMessage());
    }
}
