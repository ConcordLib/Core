using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class PatchOrderAttributeTests {
    [Fact]
    public void PatchBefore_TypeOwner_UsesFullTypeName() {
        PatchBeforeAttribute attribute = new PatchBeforeAttribute(typeof(GoodDeclaration));

        Assert.Equal(typeof(GoodDeclaration).FullName, attribute.Owner);
    }

    [Fact]
    public void PatchBefore_StringOwner_KeepsOwner() {
        PatchBeforeAttribute attribute = new PatchBeforeAttribute("Optional.Mod.PricePatch");

        Assert.Equal("Optional.Mod.PricePatch", attribute.Owner);
    }

    [Fact]
    public void PatchAfter_StringOwner_KeepsOwner() {
        PatchAfterAttribute attribute = new PatchAfterAttribute("Optional.Mod.PricePatch");

        Assert.Equal("Optional.Mod.PricePatch", attribute.Owner);
    }

    [Fact]
    public void PatchAfter_TypeOwner_UsesFullTypeName() {
        PatchAfterAttribute attribute = new PatchAfterAttribute(typeof(GoodDeclaration));

        Assert.Equal(typeof(GoodDeclaration).FullName, attribute.Owner);
    }

    [Theory]
    [InlineData(typeof(PatchBeforeAttribute))]
    [InlineData(typeof(PatchAfterAttribute))]
    public void OrderAttribute_AllowsMultipleOnClass(Type attributeType) {
        AttributeUsageAttribute usage = attributeType.GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }
}
