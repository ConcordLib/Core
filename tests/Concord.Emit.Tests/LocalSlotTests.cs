using System.Reflection;
using System.Reflection.Emit;
using Concord;
using Xunit;

namespace Concord.Emit.Tests;

public static class LocalSlotTranspilers {
    // Harmony spelling: address an existing local with a bare slot number. TwoLocals stores
    // seed+1 into slot 0, so overwriting slot 1 with slot 0 makes the method return seed+1.
    public static IEnumerable<CodeInstruction> RawSlotCopiesFirstLocalOverSecond(
        IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            yield return instruction;
        }

        yield break;
    }

    // Drop whatever the body was about to return and return local 0 instead. The slot is written
    // as a bare int, exactly as a migrated Harmony body would.
    public static IEnumerable<CodeInstruction> RawSlotRewritesReturn(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        int ret = output.FindLastIndex(i => i.opcode == OpCodes.Ret);

        output.Insert(ret, new CodeInstruction(OpCodes.Pop));
        output.Insert(ret + 1, new CodeInstruction(OpCodes.Ldloc_S, 0));

        return output;
    }

    public static IEnumerable<CodeInstruction> ContextGetLocalRewritesReturn(
        IEnumerable<CodeInstruction> instructions,
        ITranspilerContext context) {
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        int ret = output.FindLastIndex(i => i.opcode == OpCodes.Ret);

        output.Insert(ret, new CodeInstruction(OpCodes.Pop));
        output.Insert(ret + 1, new CodeInstruction(OpCodes.Ldloc_S, context.GetLocal(0)));

        return output;
    }

    public static IEnumerable<CodeInstruction> RawSlotOutOfRange(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        int ret = output.FindLastIndex(i => i.opcode == OpCodes.Ret);
        output.Insert(ret, new CodeInstruction(OpCodes.Ldloc_S, 99));
        output.Insert(ret + 1, new CodeInstruction(OpCodes.Pop));

        return output;
    }

    public static IEnumerable<CodeInstruction> ContextGetLocalOutOfRange(
        IEnumerable<CodeInstruction> instructions,
        ITranspilerContext context) {
        context.GetLocal(99);
        return instructions;
    }

    // Declaring a local appends to the body's table; it must not shift the slots GetLocal
    // resolves for locals the body already had.
    public static IEnumerable<CodeInstruction> DeclaringALocalDoesNotShiftExistingSlots(
        IEnumerable<CodeInstruction> instructions,
        ITranspilerContext context) {
        LocalRef scratch = context.DeclareLocal(typeof(int));
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        int ret = output.FindLastIndex(i => i.opcode == OpCodes.Ret);

        output.Insert(ret, new CodeInstruction(OpCodes.Pop));
        output.Insert(ret + 1, new CodeInstruction(OpCodes.Ldc_I4, 77));
        output.Insert(ret + 2, new CodeInstruction(OpCodes.Stloc_S, scratch));
        output.Insert(ret + 3, new CodeInstruction(OpCodes.Ldloc_S, context.GetLocal(0)));

        return output;
    }
}

public sealed class LocalSlotTests {
    private static Injection Transpiler(string name) {
        MethodBase method = typeof(LocalSlotTranspilers).GetMethod(name)!;
        return new Injection(method, new InjectAt.Transpiler(false), "test-" + name, 0);
    }

    private static Func<int, int> ComposeTwoLocals(string transpiler) {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.TwoLocals))!;
        ComposeResult result = WrapperComposer.Compose(target, [Transpiler(transpiler)]);
        return result.Wrapper.CreateDelegate<Func<int, int>>();
    }

    [Fact]
    public void TwoLocals_IsUnchangedWithoutATranspiler() {
        Func<int, int> run = ComposeTwoLocals(nameof(LocalSlotTranspilers.RawSlotCopiesFirstLocalOverSecond));

        Assert.Equal(8, run(5));
    }

    [Fact]
    public void RawIntOperand_OnALocalOpcode_ResolvesToTheLocalSlot() {
        Func<int, int> run = ComposeTwoLocals(nameof(LocalSlotTranspilers.RawSlotRewritesReturn));

        // second := first, so the result is seed+1 rather than seed+3.
        Assert.Equal(6, run(5));
    }

    [Fact]
    public void ContextGetLocal_ResolvesTheSameSlotsAsARawInt() {
        Func<int, int> run = ComposeTwoLocals(nameof(LocalSlotTranspilers.ContextGetLocalRewritesReturn));

        Assert.Equal(6, run(5));
    }

    [Fact]
    public void DeclaringALocal_DoesNotShiftExistingSlots() {
        Func<int, int> run = ComposeTwoLocals(
            nameof(LocalSlotTranspilers.DeclaringALocalDoesNotShiftExistingSlots));

        // Still local 0 (seed+1), not the freshly declared slot holding 77.
        Assert.Equal(6, run(5));
    }

    [Fact]
    public void RawIntOperand_OutOfRange_Throws() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.TwoLocals))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [Transpiler(nameof(LocalSlotTranspilers.RawSlotOutOfRange))]));

        Assert.Equal("CONC118", ex.Code);
    }

    [Fact]
    public void ContextGetLocal_OutOfRange_Throws() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.TwoLocals))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [Transpiler(nameof(LocalSlotTranspilers.ContextGetLocalOutOfRange))]));

        Assert.Equal("CONC121", ex.Code);
    }
}
