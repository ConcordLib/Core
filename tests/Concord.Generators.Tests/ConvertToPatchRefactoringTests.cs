using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Xunit;

namespace Concord.Generators.Tests;

public sealed class ConvertToPatchRefactoringTests {
    private const string Source = """
        namespace Game {
            public class Oven {
                public void Bake() { }
            }
        }

        namespace Mod {
            internal class Oven$$Helper : Game.Oven {
            }
        }
        """;

    [Fact]
    public async Task OnNonPatchClassWithBase_OffersConvert() {
        (Document _, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(Source, "$$");

        Assert.Single(actions, a => a.Title == "Concord: convert to patch declaration");
    }

    [Fact]
    public async Task Convert_AddsPatchAttributeAbstractAndPartial() {
        (Document document, ImmutableArray<CodeAction> actions) =
            await RefactoringTestHarness.ComputeActions(Source, "$$");

        CodeAction convert = actions.Single(a => a.Title == "Concord: convert to patch declaration");
        string changed = await RefactoringTestHarness.ApplyAsync(document, convert);

        Assert.Contains("Patch", changed);
        Assert.Contains("abstract partial class OvenHelper", changed);
    }
}
