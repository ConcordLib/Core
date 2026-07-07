using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Xunit;

namespace Concord.Generators.Tests;

public sealed class AddInjectionRefactoringTests {
    // Two namespaces in one file: block-scoped is required (only one file-scoped allowed).
    private const string Source = """
        using Concord;

        namespace Game {
            public class Oven {
                public void Bake() { }
                public int Temperature() { return 200; }
            }
        }

        namespace Mod {
            [Patch]
            internal abstract partial class Oven$$Patch : Game.Oven {
            }
        }
        """;

    [Fact]
    public async Task OnPatchClass_OffersAddInjectionWithNestedTargets() {
        (Document _, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(Source, "$$");

        CodeAction addInjection = Assert.Single(actions, a => a.Title.StartsWith("Concord: add injection"));
        Assert.Contains(addInjection.NestedActions, n => n.Title.Contains("Bake"));
        Assert.Contains(addInjection.NestedActions, n => n.Title.Contains("Temperature"));
    }

    [Fact]
    public async Task AddInjection_VoidTarget_InsertsControlHandleStub() {
        (Document document, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(Source, "$$");

        CodeAction addInjection = actions.Single(a => a.Title.StartsWith("Concord: add injection"));
        CodeAction bake = addInjection.NestedActions.Single(n => n.Title.Contains("Bake"));
        string changed = await RefactoringTestHarness.ApplyAsync(document, bake);

        Assert.Contains("[global::Concord.Inject(global::Concord.At.Head, \"Bake\")]", changed);
        Assert.Contains("private void BakeHead(global::Concord.ControlHandle ch)", changed);
    }

    [Fact]
    public async Task AddInjection_ValueTarget_UsesTypedControlHandle() {
        (Document document, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(Source, "$$");

        CodeAction addInjection = actions.Single(a => a.Title.StartsWith("Concord: add injection"));
        CodeAction temperature = addInjection.NestedActions.Single(n => n.Title.Contains("Temperature"));
        string changed = await RefactoringTestHarness.ApplyAsync(document, temperature);

        Assert.Contains("private void TemperatureHead(global::Concord.ControlHandle<int> ch)", changed);
    }

    [Fact]
    public async Task OnClassWithoutGameBase_OffersNothing() {
        (Document _, ImmutableArray<CodeAction> actions) = await RefactoringTestHarness.ComputeActions(
            """
            internal class Plain$$Thing {
            }
            """,
            "$$");

        Assert.Empty(actions);
    }
}
