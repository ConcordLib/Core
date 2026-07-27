using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class IlAssertTests {
    [Fact]
    public void BodiesMatch_IdenticalBodies_Passes() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(target);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(target);

        IlAssert.BodiesMatch(a.Definition, b.Definition);
    }

    [Fact]
    public void BodiesMatch_DifferentBodies_Fails() {
        MethodBase add = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Add))!;
        MethodBase bump = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Bump))!;
        using DynamicMethodDefinition a = new DynamicMethodDefinition(add);
        using DynamicMethodDefinition b = new DynamicMethodDefinition(bump);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => IlAssert.BodiesMatch(a.Definition, b.Definition));
    }
}
