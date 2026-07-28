using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public static class BrokenTranspilers {
    public static IEnumerable<CodeInstruction> DanglingLabel(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        output.Insert(0, new CodeInstruction(OpCodes.Br, LabelFactory.Create(9999)));
        return output;
    }

    public static IEnumerable<CodeInstruction> UnbalancedBlock(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        output[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        return output;
    }
}

public sealed class TranspilerFailureTests {
    private static ComposeResult ComposeWith(string name) {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase method = typeof(BrokenTranspilers).GetMethod(name)!;
        return WrapperComposer.Compose(target, [new Injection(method, new InjectAt.Transpiler(false), "test", 0)]);
    }

    [Fact]
    public void DanglingLabel_ThrowsConc118() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => ComposeWith(nameof(BrokenTranspilers.DanglingLabel)));
        Assert.Equal("CONC118", ex.Code);
    }

    [Fact]
    public void UnbalancedBlock_ThrowsConc118() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => ComposeWith(nameof(BrokenTranspilers.UnbalancedBlock)));
        Assert.Equal("CONC118", ex.Code);
    }

    [Fact]
    public void Message_ContainsDiagnosticCode() {
        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => ComposeWith(nameof(BrokenTranspilers.DanglingLabel)));
        Assert.StartsWith("CONC118: ", ex.Message);
    }
}
