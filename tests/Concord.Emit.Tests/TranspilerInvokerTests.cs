using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public class TranspilerMethods {
    public static IEnumerable<CodeInstruction> Noop(IEnumerable<CodeInstruction> instructions) {
        return instructions;
    }

    public static IEnumerable<CodeInstruction> WithContext(IEnumerable<CodeInstruction> instructions, ITranspilerContext context) {
        context.DeclareLocal(typeof(int));
        return instructions;
    }

    public static IEnumerable<CodeInstruction> Throws(IEnumerable<CodeInstruction> instructions) {
        throw new InvalidOperationException("author blew up");
    }

    public static IEnumerable<CodeInstruction> YieldThenThrows(IEnumerable<CodeInstruction> instructions) {
        yield return new CodeInstruction(OpCodes.Nop);
        throw new InvalidOperationException("deferred author blew up");
    }

    public static IEnumerable<CodeInstruction> ReturnsNull(IEnumerable<CodeInstruction> instructions) {
        return null!;
    }

    public IEnumerable<CodeInstruction> NotStatic(IEnumerable<CodeInstruction> instructions) {
        return instructions;
    }

    public static void WrongReturnType(IEnumerable<CodeInstruction> instructions) {
    }

    public static IEnumerable<CodeInstruction> WrongParameterCount() {
        return [];
    }
}

public sealed class TranspilerInvokerTests {
    private static TranspilerContext Context() {
        return new TranspilerContext(typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Simple))!);
    }

    [Fact]
    public void Invoke_SingleParameter_PassesThrough() {
        List<CodeInstruction> input = [new CodeInstruction(OpCodes.Nop)];
        MethodBase method = typeof(TranspilerMethods).GetMethod(nameof(TranspilerMethods.Noop))!;

        List<CodeInstruction> output = new List<CodeInstruction>(TranspilerInvoker.Invoke(method, input, Context()));

        Assert.Single(output);
    }

    [Fact]
    public void Invoke_WithContext_ReceivesContext() {
        List<CodeInstruction> input = [new CodeInstruction(OpCodes.Nop)];
        MethodBase method = typeof(TranspilerMethods).GetMethod(nameof(TranspilerMethods.WithContext))!;
        TranspilerContext context = Context();

        List<CodeInstruction> output = new List<CodeInstruction>(TranspilerInvoker.Invoke(method, input, context));

        Assert.Single(output);
        Assert.Single(context.DeclaredLocals);
    }

    [Fact]
    public void Invoke_NonStaticMethod_ThrowsConc116() {
        MethodBase method = typeof(TranspilerMethods).GetMethod(nameof(TranspilerMethods.NotStatic))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => TranspilerInvoker.Invoke(method, [], Context()));
        Assert.Equal("CONC116", ex.Code);
    }

    [Fact]
    public void Invoke_WrongReturnType_ThrowsConc116() {
        MethodBase method = typeof(TranspilerMethods).GetMethod(nameof(TranspilerMethods.WrongReturnType))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => TranspilerInvoker.Invoke(method, [], Context()));
        Assert.Equal("CONC116", ex.Code);
    }

    [Fact]
    public void Invoke_WrongParameterCount_ThrowsConc116() {
        MethodBase method = typeof(TranspilerMethods).GetMethod(nameof(TranspilerMethods.WrongParameterCount))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => TranspilerInvoker.Invoke(method, [], Context()));
        Assert.Equal("CONC116", ex.Code);
    }

    [Fact]
    public void Invoke_AuthorThrows_WrappedAsConc117() {
        MethodBase method = typeof(TranspilerMethods).GetMethod(nameof(TranspilerMethods.Throws))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => new List<CodeInstruction>(TranspilerInvoker.Invoke(method, [], Context())));
        Assert.Equal("CONC117", ex.Code);
        Assert.Contains("author blew up", ex.Message);
    }

    [Fact]
    public void Invoke_YieldBasedIteratorThrowsPartway_WrappedAsConc117() {
        MethodBase method = typeof(TranspilerMethods).GetMethod(nameof(TranspilerMethods.YieldThenThrows))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => new List<CodeInstruction>(TranspilerInvoker.Invoke(method, [], Context())));
        Assert.Equal("CONC117", ex.Code);
        Assert.Contains("deferred author blew up", ex.Message);
    }

    [Fact]
    public void Invoke_ReturnsNull_ThrowsConc117() {
        MethodBase method = typeof(TranspilerMethods).GetMethod(nameof(TranspilerMethods.ReturnsNull))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => TranspilerInvoker.Invoke(method, [], Context()));
        Assert.Equal("CONC117", ex.Code);
    }

    [Fact]
    public void Invoke_SameMethodTwice_ReturnsCachedDelegateResult() {
        List<CodeInstruction> input = [new CodeInstruction(OpCodes.Nop)];
        MethodBase method = typeof(TranspilerMethods).GetMethod(nameof(TranspilerMethods.Noop))!;

        List<CodeInstruction> first = new List<CodeInstruction>(TranspilerInvoker.Invoke(method, input, Context()));
        List<CodeInstruction> second = new List<CodeInstruction>(TranspilerInvoker.Invoke(method, input, Context()));

        Assert.Single(first);
        Assert.Single(second);
    }
}
