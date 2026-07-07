using Xunit;

namespace Concord.Emit.Tests;

public sealed class ShadowAttributeTests {
    [Fact]
    public void Ctor_MemberOnly_LeavesParameterTypesNull() {
        ShadowAttribute attribute = new ShadowAttribute("cachedValue");

        Assert.Equal("cachedValue", attribute.Member);
        Assert.Null(attribute.ParameterTypes);
    }

    [Fact]
    public void Ctor_WithParameterTypes_StoresThem() {
        ShadowAttribute attribute = new ShadowAttribute("Recalculate", typeof(int), typeof(string));

        Assert.Equal("Recalculate", attribute.Member);
        Assert.Equal([typeof(int), typeof(string)], attribute.ParameterTypes);
    }

    [Fact]
    public void Usage_IsClassTargetedAndAllowsMultiple() {
        AttributeUsageAttribute usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(ShadowAttribute), typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
    }
}
