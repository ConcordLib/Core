using Xunit;

namespace Concord.Emit.Tests;

public sealed class CodeInstructionTests {
    [Fact]
    public void Labels_WithSameId_AreEqual() {
        Label a = LabelFactory.Create(3);
        Label b = LabelFactory.Create(3);
        Label c = LabelFactory.Create(4);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void LocalRef_CarriesItsType() {
        LocalRef local = LocalRefFactory.Create(2, typeof(string));

        Assert.Equal(typeof(string), local.Type);
    }

    [Fact]
    public void ExceptionBlock_CatchBlock_CarriesCatchType() {
        ExceptionBlock block = new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(InvalidOperationException));

        Assert.Equal(ExceptionBlockType.BeginCatchBlock, block.blockType);
        Assert.Equal(typeof(InvalidOperationException), block.catchType);
    }
}
