using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;

namespace Concord.Emit.Tests;

public class StateMethods {
    public void WriteSlot(ControlHandle ctl) {
        ctl.SetState(41L);
    }

    public void ReadSlot(ControlHandle ctl) {
        long value = ctl.GetState<long>();
        System.Console.Out.Write(value);
    }
}

public sealed class StateSlotTests {
    private static Instruction FindControlCall(MethodBase method) {
        using DynamicMethodDefinition source = new DynamicMethodDefinition(method);
        foreach (Instruction instruction in source.Definition.Body.Instructions) {
            if (ControlHandleLowering.ClassifyCall(instruction) != ControlHandleLowering.ControlCallKind.None) {
                return instruction;
            }
        }

        throw new InvalidOperationException("No control call found in method body.");
    }

    [Fact]
    public void SetState_ClassifiesAsSetState() {
        MethodBase method = typeof(StateMethods).GetMethod(nameof(StateMethods.WriteSlot))!;
        Instruction call = FindControlCall(method);

        Assert.Equal(ControlHandleLowering.ControlCallKind.SetState, ControlHandleLowering.ClassifyCall(call));
        Assert.Equal(typeof(long), ControlHandleLowering.ResolveStateType(call));
    }

    [Fact]
    public void GetState_ClassifiesAsGetState() {
        MethodBase method = typeof(StateMethods).GetMethod(nameof(StateMethods.ReadSlot))!;
        Instruction call = FindControlCall(method);

        Assert.Equal(ControlHandleLowering.ControlCallKind.GetState, ControlHandleLowering.ClassifyCall(call));
        Assert.Equal(typeof(long), ControlHandleLowering.ResolveStateType(call));
    }
}
