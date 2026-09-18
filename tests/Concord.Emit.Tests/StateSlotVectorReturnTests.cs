using System;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class VectorSlotHost {
    public static Vector3 Build(string text) {
        return new Vector3(text.Length, 2f, 3f);
    }
}

public static class VectorSlotDeclaration {
    public static void Head(ControlHandle<Vector3> ch, string text) {
        ch.SetState(text.Length);
    }

    public static void Tail(ControlHandle<Vector3> ch) {
        SlotRecorder.SeenA = ch.GetState<int>();
    }
}

public sealed class StateSlotVectorReturnTests {
    [Fact]
    public void Compose_TargetReturningFacadeStruct_RoundTripsThroughTheSlot() {
        MethodBase target = typeof(VectorSlotHost).GetMethod(nameof(VectorSlotHost.Build))!;
        Injection head = new Injection(typeof(VectorSlotDeclaration).GetMethod(nameof(VectorSlotDeclaration.Head))!, new InjectAt.Head(), "test", 0);
        Injection tail = new Injection(typeof(VectorSlotDeclaration).GetMethod(nameof(VectorSlotDeclaration.Tail))!, new InjectAt.Tail(), "test", 0);

        SlotRecorder.SeenA = 0;
        ComposeResult result = WrapperComposer.Compose(target, [head, tail]);
        Func<string, Vector3> invoke = result.Wrapper.CreateDelegate<Func<string, Vector3>>();

        Assert.Equal(new Vector3(8f, 2f, 3f), invoke("11,22,33"));
        Assert.Equal(8L, SlotRecorder.SeenA);
    }
}
