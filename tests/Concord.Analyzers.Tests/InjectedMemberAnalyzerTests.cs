using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Concord.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace Concord.Analyzers.Tests;

public sealed class InjectedMemberAnalyzerTests {
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> BasicReferences = ImmutableArray.Create<MetadataReference>(
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));

    [Fact]
    public async Task InferredTarget_AllowsMatchingInjectedMembers() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public class Target {
                private int fuel;
                private int Mood { get; set; }
                private int Recalculate(int add) => fuel + add;
            }

            [Concord.Patch]
            public abstract class Patch : Target {
                [Concord.InjectField("fuel")]
                private int fieldDeclaration;

                [Concord.InjectProperty("Mood")]
                protected abstract int MoodDeclaration { get; set; }

                [Concord.InjectMethod("Recalculate")]
                protected abstract int RecalculateDeclaration(int add);
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ExplicitTypeTarget_AllowsMatchingInjectedMembers() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private int fuel;
                private int Mood { get; set; }
                private int Recalculate(int add) => fuel + add;
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.InjectField("fuel")]
                private int fieldDeclaration;

                [Concord.InjectProperty("Mood")]
                protected abstract int MoodDeclaration { get; set; }

                [Concord.InjectMethod("Recalculate")]
                protected abstract int RecalculateDeclaration(int add);
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task StringTarget_ResolvesSourceTarget() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            namespace Game {
                public sealed class Target {
                    private int configCopy;
                }
            }

            [Concord.Patch("Game.Target")]
            public abstract class Patch {
                [Concord.InjectField("configCopy")]
                private string fieldDeclaration;
            }
            """);

        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == InjectedMemberAnalyzer.PreferTypeofPatchTargetDiagnosticId);
        Diagnostic mismatch = Assert.Single(diagnostics, diagnostic => diagnostic.Id == InjectedMemberAnalyzer.MismatchedMemberDiagnosticId);
        Assert.Contains("Game.Target", mismatch.GetMessage());
    }

    [Fact]
    public async Task StringTarget_ResolvesAssemblyQualifiedReferenceTarget() {
        MetadataReference targetReference = CreateReference(
            "GameAssembly",
            """
            namespace Game {
                public sealed class Target {
                    private int configCopy;
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            [Concord.Patch("Game.Target, GameAssembly")]
            public abstract class Patch {
                [Concord.InjectField("configCopy")]
                private int fieldDeclaration;
            }
            """,
            targetReference);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.PreferTypeofPatchTargetDiagnosticId, diagnostic.Id);
        Assert.Contains("typeof(Game.Target)", diagnostic.GetMessage());
    }

    [Fact]
    public async Task StringTarget_UnnameableReferenceTarget_DoesNotReportTypeofSuggestion() {
        MetadataReference targetReference = CreateReference(
            "GameAssembly",
            """
            namespace Game {
                internal sealed class Target {
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            [Concord.Patch("Game.Target, GameAssembly")]
            public abstract class Patch {
            }
            """,
            targetReference);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ExplicitTypeTarget_ReportsMissingFieldOnReferencedAssemblyTarget() {
        MetadataReference targetReference = CreateReference(
            "GameAssembly",
            """
            namespace Game {
                public sealed class Target {
                    private int configCopy;
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            [Concord.Patch(typeof(Game.Target))]
            public abstract class Patch {
                [Concord.InjectField("configCopy2")]
                private int configCopy;
            }
            """,
            targetReference);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MissingMemberDiagnosticId, diagnostic.Id);
        Assert.Contains("configCopy2", diagnostic.GetMessage());
        Assert.Contains("Game.Target", diagnostic.GetMessage());
    }

    [Fact]
    public async Task StringTarget_UnresolvedTarget_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            [Concord.Patch("Game.Target")]
            public abstract class Patch {
                [Concord.InjectField("missing")]
                private int fieldDeclaration;
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.UnresolvedPatchTargetDiagnosticId, diagnostic.Id);
        Assert.Contains("Game.Target", diagnostic.GetMessage());
    }

    [Fact]
    public async Task InjectField_MissingTargetField_ReportsDiagnostic() {
        const string source = AttributeSource +
        """

        public sealed class Target {
        }

        [Concord.Patch(typeof(Target))]
        public abstract class Patch {
            [Concord.InjectField("configCopy")]
            private object configCopy;
        }
        """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MissingMemberDiagnosticId, diagnostic.Id);
        Assert.Contains("configCopy", diagnostic.GetMessage());
        AssertDiagnosticOnAttribute(source, diagnostic, "Concord.InjectField");
    }

    [Fact]
    public async Task InjectField_TypeMismatch_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private int configCopy;
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.InjectField("configCopy")]
                private string configCopy;
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MismatchedMemberDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task InjectProperty_MissingTargetProperty_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.InjectProperty("Mood")]
                protected abstract int MoodDeclaration { get; set; }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MissingMemberDiagnosticId, diagnostic.Id);
        Assert.Contains("Mood", diagnostic.GetMessage());
    }

    [Fact]
    public async Task InjectProperty_TypeMismatch_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private string Mood { get; set; }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.InjectProperty("Mood")]
                protected abstract int MoodDeclaration { get; set; }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MismatchedMemberDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task InjectMethod_MissingTargetMethod_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private int Recalculate(string add) => 0;
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.InjectMethod("Recalculate")]
                protected abstract int RecalculateDeclaration(int add);
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MissingMemberDiagnosticId, diagnostic.Id);
        Assert.Contains("Recalculate", diagnostic.GetMessage());
    }

    [Fact]
    public async Task InjectMethod_ReturnTypeMismatch_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private string Recalculate(int add) => "";
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.InjectMethod("Recalculate")]
                protected abstract int RecalculateDeclaration(int add);
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MismatchedMemberDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task InjectField_PublicTargetFieldStringLiteral_ReportsNameofSuggestion() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                public int configCopy;
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.InjectField("configCopy")]
                private int configCopy;
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.PreferNameofMemberTargetDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task ExplicitTypeTarget_ExtendableTarget_ReportsInheritedTargetSuggestion() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public class Target {
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.PreferInheritedPatchTargetDiagnosticId, diagnostic.Id);
        Assert.Contains("[Patch]", diagnostic.GetMessage());
    }

    [Fact]
    public async Task ExplicitTypeTarget_ExtendableRecordTarget_ReportsInheritedTargetSuggestion() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public record Target(int Value);

            [Concord.Patch(typeof(Target))]
            public abstract record Patch(int Value) : Target(Value) {
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.PreferInheritedPatchTargetDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task ExplicitTypeTarget_PrivateConstructorTarget_ReportsNoDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public class Target {
                private Target() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ExplicitTypeTarget_ProtectedConstructorTarget_ReportsInheritedTargetSuggestion() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public class Target {
                protected Target() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.PreferInheritedPatchTargetDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task Inject_TargetMethodMissing_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Existing() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Missing")]
                private void Before() {
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.MissingInjectionTargetDiagnosticId, diagnostic.Id);
        Assert.Contains("Missing", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Inject_PublicTargetMethodStringLiteral_ReportsNameofSuggestion() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                public void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick")]
                private void Before(Concord.ControlHandle ch) {
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.PreferNameofMemberTargetDiagnosticId, diagnostic.Id);
        Assert.Contains("nameof", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Inject_NameofTargetMethod_DoesNotReportNameofSuggestion() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                public void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, nameof(Target.Tick))]
                private void Before(Concord.ControlHandle ch) {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Inject_PrivateTargetMethodStringLiteral_DoesNotReportNameofSuggestion() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick")]
                private void Before(Concord.ControlHandle ch) {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Inject_AmbiguousTargetMethod_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick(int amount) {
                }

                private void Tick(string label) {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick")]
                private void Before(Concord.ControlHandle ch) {
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.AmbiguousInjectionTargetDiagnosticId, diagnostic.Id);
        Assert.Contains("parameterTypes", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Inject_ParameterTypesDisambiguatesOverload() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick(int amount) {
                }

                private void Tick(string label) {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick", parameterTypes: new[] { typeof(int) })]
                private void Before(int amount) {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Inject_ParameterTypesEmpty_DoesNotCountOverriddenBaseMethodAsAmbiguous() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public class Bill {
                public virtual bool ShouldDoNow() => false;
            }

            public sealed class Bill_Production : Bill {
                public override bool ShouldDoNow() => true;
            }

            [Concord.Patch(typeof(Bill_Production))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, nameof(Bill_Production.ShouldDoNow), parameterTypes: new Type[0])]
                private void PrefixShouldDoNow(Concord.ControlHandle<bool> ch) {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Inject_ConstructorWithoutParameterTypes_UsesParameterlessConstructor() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private Target() {
                }

                private Target(int amount) {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head)]
                private void OnConstruct() {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Inject_ParameterMismatch_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick(int amount) {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick")]
                private void Before(string amount) {
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.InvalidInjectionSignatureDiagnosticId, diagnostic.Id);
        Assert.Contains("parameters", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Inject_StaticTargetWithInstanceInjectionMethod_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private static void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick")]
                private void Before() {
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.StaticInstanceMismatchDiagnosticId, diagnostic.Id);
        Assert.Contains("static", diagnostic.GetMessage());
    }

    [Fact]
    public async Task PlainField_MatchesTargetField_ReportsInjectFieldSuggestion() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private int configCopy;
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                private int configCopy;
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.AttachedFieldCouldBeInjectFieldDiagnosticId, diagnostic.Id);
        Assert.Contains("InjectField", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Inject_DuplicateTargetAndPosition_ReportsDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick")]
                private void BeforeA() {
                }

                [Concord.Inject(Concord.At.Head, "Tick")]
                private void BeforeB() {
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.DuplicateInjectionDiagnosticId, diagnostic.Id);
        Assert.Contains("duplicates", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Inject_GenericInjectionMethod_ReportsUnsupportedDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick")]
                private void Before<T>() {
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.UnsupportedDeclarationShapeDiagnosticId, diagnostic.Id);
        Assert.Contains("generic", diagnostic.GetMessage());
    }

    [Fact]
    public async Task InjectInstance_WithSetter_ReportsUnsupportedDiagnostic() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.InjectInstance]
                protected Target Instance { get; set; }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.UnsupportedDeclarationShapeDiagnosticId, diagnostic.Id);
        Assert.Contains("get-only", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Inject_ControlReturnOnHead_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick")]
                private Concord.Control Before() {
                    return Concord.Control.Continue;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Inject_ControlReturnOnTail_ReportsControlReturnPosition() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Tail, "Tick")]
                private Concord.Control After() {
                    return Concord.Control.Continue;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.ControlReturnPositionDiagnosticId, diagnostic.Id);
        Assert.Contains("tail", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inject_ControlReturnOnAround_ReportsControlReturnPosition() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Around, "Tick")]
                private Concord.Control Wrap() {
                    return Concord.Control.Continue;
                }
            }
            """);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(InjectedMemberAnalyzer.ControlReturnPositionDiagnosticId, diagnostic.Id);
    }

    [Fact]
    public async Task Inject_ControlReturnOnInvokeForm_ReportsControlReturnPosition() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public static class Helper {
                public static void Step() {
                }
            }

            public sealed class Target {
                private void Tick() {
                    Helper.Step();
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject("Tick", typeof(Helper), "Step", Concord.At.Head)]
                private Concord.Control BeforeStep() {
                    return Concord.Control.Continue;
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == InjectedMemberAnalyzer.ControlReturnPositionDiagnosticId);
    }

    [Fact]
    public async Task NonInjectionMethod_ControlReturn_ReportsNothing() {
        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(
            AttributeSource +
            """

            public sealed class Target {
                private void Tick() {
                }
            }

            [Concord.Patch(typeof(Target))]
            public abstract class Patch {
                [Concord.Inject(Concord.At.Head, "Tick")]
                private void Before() {
                }

                private Concord.Control Helper() {
                    return Concord.Control.Cancel;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    private const string AttributeSource = """
    using System;

    namespace Concord {
        public enum At {
            Head,
            Return,
            Tail,
            Around
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

            public InjectAttribute(At at, Type[] parameterTypes = null) {
            }

            public InjectAttribute(string method, Type invokeDeclaringType, string invokeDeclaringMethod, At shift, uint by = 0, Type[] targetParameterTypes = null, Type[] invokeParameterTypes = null) {
            }
        }

        [AttributeUsage(AttributeTargets.Property)]
        public sealed class InjectInstanceAttribute : Attribute {
        }

        [AttributeUsage(AttributeTargets.Field)]
        public sealed class InjectFieldAttribute : Attribute {
            public InjectFieldAttribute() {
            }

            public InjectFieldAttribute(string targetName) {
            }
        }

        [AttributeUsage(AttributeTargets.Property)]
        public sealed class InjectPropertyAttribute : Attribute {
            public InjectPropertyAttribute() {
            }

            public InjectPropertyAttribute(string targetName) {
            }
        }

        [AttributeUsage(AttributeTargets.Method)]
        public sealed class InjectMethodAttribute : Attribute {
            public InjectMethodAttribute() {
            }

            public InjectMethodAttribute(string targetName) {
            }
        }

        public sealed class ControlHandle {
        }

        public sealed class ControlHandle<T> {
        }

        public enum Control {
            Continue = 0,
            Cancel = 1,
        }

        public sealed class Operation {
        }

        public sealed class Operation<T> {
        }
    }
    """;

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        params MetadataReference[] references) {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "InjectedMemberConsumer",
            new[] {
                CSharpSyntaxTree.ParseText(source, ParseOptions),
            },
            BasicReferences.AddRange(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new InjectedMemberAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static MetadataReference CreateReference(string assemblyName, string source) {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] {
                CSharpSyntaxTree.ParseText(source, ParseOptions),
            },
            BasicReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        string path = Path.Combine(Path.GetTempPath(), "Concord.Analyzers.Tests." + Guid.NewGuid().ToString("N") + ".dll");
        EmitResult emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));

        return MetadataReference.CreateFromFile(path);
    }

    private static void AssertDiagnosticOnAttribute(string source, Diagnostic diagnostic, string expectedText) {
        var span = diagnostic.Location.SourceSpan;
        string diagnosticSource = source.Substring(span.Start, span.Length);
        Assert.Contains(expectedText, diagnosticSource);
    }
}
