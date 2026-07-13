using Xunit;

namespace Concord.Emit.Tests;

public sealed class OperationFamilyTests {
    [Theory]
    [InlineData(typeof(Operation))]
    [InlineData(typeof(Operation<int>))]
    [InlineData(typeof(Operation<int, int>))]
    [InlineData(typeof(Operation<int, string, bool>))]
    [InlineData(typeof(Operation<int, int, int, int>))]
    [InlineData(typeof(VoidOperation<int>))]
    [InlineData(typeof(VoidOperation<int, string>))]
    public void IsOperationType_RecognizesEveryFamilyMember(System.Type type) {
        Assert.True(ControlHandleLowering.IsOperationType(type));
    }
}
