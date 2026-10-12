using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace Concord.Emit.Tests;

public sealed class ComposedBodyIsTheWrapperTests {
    [Fact]
    public void CapturedBody_HoldsInjectedIlTheTargetNeverHad() {
        MethodBase target = typeof(EmitTargets).GetMethod(nameof(EmitTargets.Tracked))!;
        FieldInfo counter = typeof(EmitTargets).GetField(nameof(EmitTargets.Counter))!;
        Assert.False(TargetStoresToAStaticField(target), "the target already stores to a static field, so finding the store would prove nothing");

        ComposeResult result = Compose(target);

        Assert.Null(result.BodyRejection);
        Assert.NotNull(result.Body);
        Assert.Contains(result.Body!.Instructions, instruction => instruction.Instruction.Is(OpCodes.Stsfld, counter));
    }

    private static bool TargetStoresToAStaticField(MethodBase target) {
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        foreach (Instruction instruction in source.Definition.Body.Instructions) {
            if (instruction.OpCode == Mono.Cecil.Cil.OpCodes.Stsfld) {
                return true;
            }
        }

        return false;
    }

    private static ComposeResult Compose(MethodBase target) {
        List<Injection> injections = [
            new Injection(typeof(EmitTargets).GetMethod(nameof(EmitTargets.Bump))!, new InjectAt.Head(), "wrapper-head", 0),
        ];

        WrapperComposer.CaptureComposedBody = true;
        try {
            return WrapperComposer.Compose(target, injections);
        } finally {
            WrapperComposer.CaptureComposedBody = false;
        }
    }
}
