using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Xunit;

namespace Concord.Generators.Tests;

public sealed class AddShadowRefactoringTests {
    private const string Source = """
        using Concord;

        namespace Game {
            public class Oven {
                private int heat;
                private void Vent() { }
                public void Bake() { Vent(); heat++; }
            }
        }

        namespace Mod {
            [Patch]
            internal abstract partial class Oven$$Patch : Game.Oven {
            }
        }
        """;

    [Fact]
    public async Task OnPatchClass_OffersAddShadowWithPrivateMembers() {
        (Document _, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(Source, "$$");

        CodeAction addShadow = Assert.Single(actions, a => a.Title.StartsWith("Concord: add shadow"));
        Assert.Contains(addShadow.NestedActions, n => n.Title.Contains("heat"));
        Assert.Contains(addShadow.NestedActions, n => n.Title.Contains("Vent"));
    }

    [Fact]
    public async Task AddShadow_InsertsShadowAttribute() {
        (Document document, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(Source, "$$");

        CodeAction addShadow = actions.Single(a => a.Title.StartsWith("Concord: add shadow"));
        CodeAction heat = addShadow.NestedActions.Single(n => n.Title.Contains("heat"));
        string changed = await RefactoringTestHarness.ApplyAsync(document, heat);

        Assert.Contains("Shadow(\"heat\")", changed);
    }

    [Fact]
    public async Task AddShadow_NonPartialClass_AddsPartialModifier() {
        string source = Source.Replace("abstract partial class", "abstract class");
        (Document document, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(source, "$$");

        CodeAction addShadow = actions.Single(a => a.Title.StartsWith("Concord: add shadow"));
        CodeAction heat = addShadow.NestedActions.Single(n => n.Title.Contains("heat"));
        string changed = await RefactoringTestHarness.ApplyAsync(document, heat);

        Assert.Contains("partial class OvenPatch", changed);
    }
}
