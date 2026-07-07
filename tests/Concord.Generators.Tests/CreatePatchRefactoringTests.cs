using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Xunit;

namespace Concord.Generators.Tests;

public sealed class CreatePatchRefactoringTests {
    private const string Source = """
        namespace Game {
            public class Oven {
                public void Bake() { }
            }
        }

        namespace Mod {
            internal static class Kitchen {
                internal static void Run(Game.Oven oven) {
                    oven.Ba$$ke();
                }
            }
        }
        """;

    [Fact]
    public async Task OnMethodReference_OffersCreatePatch() {
        (Document _, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(Source, "$$");

        Assert.Contains(actions, a => a.Title == "Concord: create patch for Bake()");
    }

    [Fact]
    public async Task CreatePatch_AppendsScaffoldedDeclaration() {
        (Document document, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(Source, "$$");

        CodeAction create = actions.Single(a => a.Title == "Concord: create patch for Bake()");
        string changed = await RefactoringTestHarness.ApplyAsync(document, create);

        Assert.Contains("[global::Concord.Patch(typeof(global::Game.Oven))]", changed);
        Assert.Contains("internal abstract partial class OvenPatch", changed);
        Assert.Contains("[global::Concord.Inject(global::Concord.At.Head, \"Bake\")]", changed);
        Assert.Contains("private void BakeHead(global::Concord.ControlHandle ch)", changed);
    }
}
