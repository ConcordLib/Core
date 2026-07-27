using System.Reflection;
using System.Reflection.Emit;
using Concord;
using Concord.Emit.Tests.ForeignTargets;
using Xunit;

namespace Concord.Emit.Tests;

public static class PriceTranspilers {
    public static IEnumerable<CodeInstruction> FiveToTen(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.opcode == OpCodes.Ldc_I4_5) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 10);
            } else {
                yield return instruction;
            }
        }
    }

    public static IEnumerable<CodeInstruction> TenToTwenty(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.Is(OpCodes.Ldc_I4, 10)) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 20);
            } else {
                yield return instruction;
            }
        }
    }
}

public static class PricePlusOne {
    public static void Bump(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 1;
    }
}

public static class PriceHeadInjection {
    public static void NoOp() {
    }
}

public static class ForeignCallTranspilers {
    public static IEnumerable<CodeInstruction> CallForeignMethod(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        MethodInfo ping = typeof(ForeignCallTarget).GetMethod(nameof(ForeignCallTarget.Ping))!;
        output.Insert(0, new CodeInstruction(OpCodes.Call, ping));
        return output;
    }

    public static IEnumerable<CodeInstruction> WriteAndReadForeignField(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        FieldInfo counter = typeof(ForeignFieldTarget).GetField(nameof(ForeignFieldTarget.Counter))!;
        CodeInstruction[] preamble = [
            new CodeInstruction(OpCodes.Ldc_I4, 42),
            new CodeInstruction(OpCodes.Stsfld, counter),
            new CodeInstruction(OpCodes.Ldsfld, counter),
            new CodeInstruction(OpCodes.Pop),
        ];
        output.InsertRange(0, preamble);
        return output;
    }

    public static IEnumerable<CodeInstruction> ConstructForeignType(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        ConstructorInfo ctor = typeof(ForeignTypeTarget).GetConstructor(Type.EmptyTypes)!;
        FieldInfo marker = typeof(ForeignTypeTarget).GetField(nameof(ForeignTypeTarget.Marker))!;
        CodeInstruction[] preamble = [
            new CodeInstruction(OpCodes.Newobj, ctor),
            new CodeInstruction(OpCodes.Castclass, typeof(ForeignTypeTarget)),
            new CodeInstruction(OpCodes.Ldfld, marker),
            new CodeInstruction(OpCodes.Pop),
        ];
        output.InsertRange(0, preamble);
        return output;
    }
}

public sealed class TranspilerComposeTests {
    private static Injection Transpiler(string name, int priority = 0, bool final = false) {
        MethodBase method = typeof(PriceTranspilers).GetMethod(name)!;
        return new Injection(method, new InjectAt.Transpiler(final), "test-" + name, priority);
    }

    [Fact]
    public void Transpiler_RewritesOriginalBody() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        ComposeResult result = WrapperComposer.Compose(target, [Transpiler(nameof(PriceTranspilers.FiveToTen))]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(10, run());
    }

    [Fact]
    public void Transpilers_ChainInOrder() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        ComposeResult result = WrapperComposer.Compose(target, [
            Transpiler(nameof(PriceTranspilers.FiveToTen)),
            Transpiler(nameof(PriceTranspilers.TenToTwenty)),
        ]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(20, run());
    }

    [Fact]
    public void Transpiler_ComposesWithDeclarativeReturnInjection() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase bump = typeof(PricePlusOne).GetMethod(nameof(PricePlusOne.Bump))!;
        ComposeResult result = WrapperComposer.Compose(target, [
            Transpiler(nameof(PriceTranspilers.FiveToTen)),
            new Injection(bump, new InjectAt.Return(0), "test-return", 0),
        ]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(11, run());
    }

    [Fact]
    public void NoTranspiler_BodyIsUnchanged() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        ComposeResult result = WrapperComposer.Compose(target, [
            new Injection(typeof(PricePlusOne).GetMethod(nameof(PricePlusOne.Bump))!, new InjectAt.Return(0), "test-return", 0),
        ]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(6, run());
    }

    [Fact]
    public void ComposeDump_WithTranspilerAndDeclarativeInjection_ReflectsRewrittenBody() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase headMethod = typeof(PriceHeadInjection).GetMethod(nameof(PriceHeadInjection.NoOp))!;
        Injection head = new Injection(headMethod, new InjectAt.Head(), "test-head", 0);

        string dump = WrapperComposer.ComposeDump(target, [Transpiler(nameof(PriceTranspilers.FiveToTen)), head]);

        Assert.Contains("10 [Int32]", dump);
    }

    [Fact]
    public void Transpiler_InsertingForeignMethodCall_ResolvesCrossModuleOperand() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase method = typeof(ForeignCallTranspilers).GetMethod(nameof(ForeignCallTranspilers.CallForeignMethod))!;

        ForeignCallTarget.Runs = 0;
        ComposeResult result = WrapperComposer.Compose(target, [new Injection(method, new InjectAt.Transpiler(false), "test", 0)]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(5, run());
        Assert.Equal(1, ForeignCallTarget.Runs);
    }

    [Fact]
    public void Transpiler_InsertingForeignFieldAccess_ResolvesCrossModuleOperand() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase method = typeof(ForeignCallTranspilers).GetMethod(nameof(ForeignCallTranspilers.WriteAndReadForeignField))!;

        ForeignFieldTarget.Counter = 0;
        ComposeResult result = WrapperComposer.Compose(target, [new Injection(method, new InjectAt.Transpiler(false), "test", 0)]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(5, run());
        Assert.Equal(42, ForeignFieldTarget.Counter);
    }

    [Fact]
    public void Transpiler_InsertingForeignTypeConstruction_ResolvesCrossModuleOperand() {
        MethodBase target = typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Priced))!;
        MethodBase method = typeof(ForeignCallTranspilers).GetMethod(nameof(ForeignCallTranspilers.ConstructForeignType))!;

        ForeignTypeTarget.Constructed = 0;
        ComposeResult result = WrapperComposer.Compose(target, [new Injection(method, new InjectAt.Transpiler(false), "test", 0)]);
        Func<int> run = result.Wrapper.CreateDelegate<Func<int>>();

        Assert.Equal(5, run());
        Assert.Equal(1, ForeignTypeTarget.Constructed);
    }
}
