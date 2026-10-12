using System.Reflection.Emit;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public sealed class OutsideCtorProbe {
    [Fact]
    public void ABackendOutsideConcordCanConstructAComposedBody() {
        ComposedInstruction ret = new ComposedInstruction(new CodeInstruction(OpCodes.Ret), 0, null);
        ComposedLocal local = new ComposedLocal(typeof(int), false);
        ComposedClause clause = new ComposedClause(ClauseKind.Finally, 0, 1, 1, 1, null, -1);
        ComposedBody body = new ComposedBody([ret], [local], [clause], true);

        Assert.Single(body.Instructions);
        Assert.Single(body.Locals);
        Assert.Single(body.Clauses);
        Assert.True(body.InitLocals);

        Assert.Equal(0, default(LocalRef).Index);

        ITranspilerContext context = TranspilerContextFactory.For(typeof(OutsideCtorProbe).GetMethod(nameof(ABackendOutsideConcordCanConstructAComposedBody))!);
        Label first = context.DefineLabel();
        Label second = context.DefineLabel();
        Assert.NotEqual(first, second);
        Assert.NotEqual(context.DeclareLocal(typeof(int)).Index, context.DeclareLocal(typeof(int)).Index);

        List<string> written = [];
        Patcher.UseLog(written.Add);
        Patcher.WriteLog("from the backend");
        Patcher.UseLog(null);
        Assert.Equal(["from the backend"], written);

        Patcher.UseLocalNameResolver(_ => "/nowhere");
        Patcher.UseLocalNameResolver(null);

        Assert.True(Patcher.PatchedTargetCount >= 0);
    }
}
