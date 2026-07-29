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

public class SlotHost {
    public int Total(int seed) {
        return seed * 2;
    }
}

public static class SlotRecorder {
    public static long SeenA;
    public static string SeenB = string.Empty;
}

public static class SlotDeclarationA {
    // The write is conditional so an invocation that skips it observes whatever the slot holds on
    // entry. That is what makes the cross-invocation leak test able to fail.
    public static void Head(ControlHandle<int> ch, int seed) {
        if (seed > 0) {
            ch.SetState(seed + 100L);
        }
    }

    public static void Tail(ControlHandle<int> ch) {
        SlotRecorder.SeenA = ch.GetState<long>();
    }
}

public static class SlotDeclarationB {
    public static void Head(ControlHandle<int> ch) {
        ch.SetState("from-b");
    }

    public static void Tail(ControlHandle<int> ch) {
        SlotRecorder.SeenB = ch.GetState<string>();
    }
}

public static class SlotConflictDeclaration {
    public static void Head(ControlHandle<int> ch) {
        ch.SetState(1L);
    }

    public static void Tail(ControlHandle<int> ch) {
        ch.SetState("mismatched");
    }
}

public static class SlotReaderOnlyDeclaration {
    public static void Tail(ControlHandle<int> ch) {
        SlotRecorder.SeenA = ch.GetState<long>();
    }
}

public static class SlotAroundHost {
    public static int Compute() {
        return 3;
    }
}

public static class SlotAroundDeclaration {
    public static void Head(ControlHandle<int> ch) {
        ch.SetState(77L);
    }

    public static int Around(Operation<int> original) {
        return original.Invoke();
    }

    public static void Tail(ControlHandle<int> ch) {
        SlotRecorder.SeenA = ch.GetState<long>();
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

    [Fact]
    public void Compose_HeadWritesTailReads_RoundTripsThroughTheSlot() {
        MethodBase target = typeof(SlotHost).GetMethod(nameof(SlotHost.Total))!;
        Injection head = new Injection(typeof(SlotDeclarationA).GetMethod(nameof(SlotDeclarationA.Head))!, new InjectAt.Head(), "test", 0);
        Injection tail = new Injection(typeof(SlotDeclarationA).GetMethod(nameof(SlotDeclarationA.Tail))!, new InjectAt.Tail(), "test", 0);

        SlotRecorder.SeenA = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head, tail]);
        Func<SlotHost, int, int> invoke = result.Wrapper.CreateDelegate<Func<SlotHost, int, int>>();

        Assert.Equal(10, invoke(new SlotHost(), 5));
        Assert.Equal(105L, SlotRecorder.SeenA);
    }

    [Fact]
    public void Compose_TwoDeclarations_KeepIndependentSlots() {
        MethodBase target = typeof(SlotHost).GetMethod(nameof(SlotHost.Total))!;
        Injection headA = new Injection(typeof(SlotDeclarationA).GetMethod(nameof(SlotDeclarationA.Head))!, new InjectAt.Head(), "a", 0);
        Injection headB = new Injection(typeof(SlotDeclarationB).GetMethod(nameof(SlotDeclarationB.Head))!, new InjectAt.Head(), "b", 0);
        Injection tailA = new Injection(typeof(SlotDeclarationA).GetMethod(nameof(SlotDeclarationA.Tail))!, new InjectAt.Tail(), "a", 0);
        Injection tailB = new Injection(typeof(SlotDeclarationB).GetMethod(nameof(SlotDeclarationB.Tail))!, new InjectAt.Tail(), "b", 0);

        SlotRecorder.SeenA = 0;
        SlotRecorder.SeenB = string.Empty;
        ComposeResult result = WrapperComposer.Compose(target, [headA, headB, tailA, tailB]);
        Func<SlotHost, int, int> invoke = result.Wrapper.CreateDelegate<Func<SlotHost, int, int>>();

        Assert.Equal(14, invoke(new SlotHost(), 7));
        Assert.Equal(107L, SlotRecorder.SeenA);
        Assert.Equal("from-b", SlotRecorder.SeenB);
    }

    [Fact]
    public void Compose_OneDeclarationWithTwoStateTypes_Throws() {
        MethodBase target = typeof(SlotHost).GetMethod(nameof(SlotHost.Total))!;
        Injection head = new Injection(typeof(SlotConflictDeclaration).GetMethod(nameof(SlotConflictDeclaration.Head))!, new InjectAt.Head(), "test", 0);
        Injection tail = new Injection(typeof(SlotConflictDeclaration).GetMethod(nameof(SlotConflictDeclaration.Tail))!, new InjectAt.Tail(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [head, tail]));

        Assert.Equal("CONC127", ex.Code);
    }

    [Fact]
    public void Compose_ReadWithoutWrite_YieldsTheStateTypeDefault() {
        MethodBase target = typeof(SlotHost).GetMethod(nameof(SlotHost.Total))!;
        Injection tail = new Injection(typeof(SlotReaderOnlyDeclaration).GetMethod(nameof(SlotReaderOnlyDeclaration.Tail))!, new InjectAt.Tail(), "test", 0);

        SlotRecorder.SeenA = 999L;
        ComposeResult result = WrapperComposer.Compose(target, [tail]);
        Func<SlotHost, int, int> invoke = result.Wrapper.CreateDelegate<Func<SlotHost, int, int>>();

        Assert.Equal(10, invoke(new SlotHost(), 5));
        Assert.Equal(0L, SlotRecorder.SeenA);
    }

    // Locks the observable behaviour: a Head write is still readable from a Tail spliced into an
    // Around spine copy. Note this does NOT guard the state entry in CollectProtocolLocals —
    // deleting that block leaves this green, because SpineTemplate.Capture only clones locals the
    // SPINE references and the spine is the original target body, which has no state calls.
    [Fact]
    public void Compose_HeadWrite_ReachesTailSplicedIntoAnAroundSpineCopy() {
        MethodBase target = typeof(SlotAroundHost).GetMethod(nameof(SlotAroundHost.Compute))!;
        Injection head = new Injection(typeof(SlotAroundDeclaration).GetMethod(nameof(SlotAroundDeclaration.Head))!, new InjectAt.Head(), "test", 0);
        Injection around = new Injection(typeof(SlotAroundDeclaration).GetMethod(nameof(SlotAroundDeclaration.Around))!, new InjectAt.Around(), "test", 1);
        Injection tail = new Injection(typeof(SlotAroundDeclaration).GetMethod(nameof(SlotAroundDeclaration.Tail))!, new InjectAt.Tail(), "test", 2);

        SlotRecorder.SeenA = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head, around, tail]);
        Func<int> invoke = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(3, invoke());
        Assert.Equal(77L, SlotRecorder.SeenA);
    }

    [Fact]
    public void Compose_StateSlot_DoesNotLeakAcrossWrapperInvocations() {
        MethodBase target = typeof(SlotHost).GetMethod(nameof(SlotHost.Total))!;
        Injection head = new Injection(typeof(SlotDeclarationA).GetMethod(nameof(SlotDeclarationA.Head))!, new InjectAt.Head(), "test", 0);
        Injection tail = new Injection(typeof(SlotDeclarationA).GetMethod(nameof(SlotDeclarationA.Tail))!, new InjectAt.Tail(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [head, tail]);
        Func<SlotHost, int, int> invoke = result.Wrapper.CreateDelegate<Func<SlotHost, int, int>>();
        SlotHost host = new SlotHost();

        invoke(host, 5);
        Assert.Equal(105L, SlotRecorder.SeenA);

        // seed <= 0 skips the Head write, so the Tail reads the slot as the wrapper entered it.
        // A slot that survived the previous call would read back 105L here.
        invoke(host, -1);
        Assert.Equal(0L, SlotRecorder.SeenA);
    }
}
