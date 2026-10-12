using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;
using Xunit.Abstractions;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace Concord.Emit.Tests;

public static class OriginProbe {
    public static int[] Observed = [];
    public static int Total;

    public static IEnumerable<CodeInstruction> Capture(IEnumerable<CodeInstruction> instructions) {
        return Record(new List<CodeInstruction>(instructions));
    }

    public static IEnumerable<CodeInstruction> CloneEveryInstruction(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> body = new List<CodeInstruction>();
        foreach (CodeInstruction instruction in instructions) {
            body.Add(new CodeInstruction(instruction));
        }

        return Record(body);
    }

    public static IEnumerable<CodeInstruction> PrependNop(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> body = [new CodeInstruction(OpCodes.Nop)];
        body.AddRange(instructions);
        return Record(body);
    }

    public static int Covered() {
        int covered = 0;
        foreach (int offset in Observed) {
            if (offset != InstructionOrigins.None) {
                covered++;
            }
        }

        return covered;
    }

    private static List<CodeInstruction> Record(List<CodeInstruction> body) {
        Observed = InstructionOrigins.Describe(body);
        Total = body.Count;
        return body;
    }
}

public class OriginGenericBox<T> {
    public int Ping(int a) {
        return a + 1;
    }
}

public static class OriginTargets {
    public static int Counter;

    public static void Head(ControlHandle ch, int a, int b) {
        Counter += a + b;
    }

    public static int Around(int a, int b, Operation<int, int, int> original) {
        return original.Invoke(a, b) * 2;
    }

    public static int Medium(int seed) {
        int total = seed;
        for (int i = 0; i < 4; i++) {
            total += i * seed;
            if (total > 100) {
                total -= 7;
            }
        }

        return total;
    }

    public static int Large(int seed) {
        int total = seed;
        for (int i = 0; i < 8; i++) {
            total += i * seed;
            if (total > 100) {
                total -= 7;
            } else if (total < -100) {
                total += 13;
            }

            switch (total % 3) {
                case 0:
                    total ^= i;
                    break;
                case 1:
                    total |= i;
                    break;
                default:
                    total &= ~i;
                    break;
            }
        }

        try {
            total += int.Parse(total.ToString());
        } catch (FormatException) {
            total = -1;
        } finally {
            total += 1;
        }

        return total;
    }
}

public sealed class InstructionOriginTests {
    private readonly ITestOutputHelper output;

    public InstructionOriginTests(ITestOutputHelper output) {
        this.output = output;
    }

    [Fact]
    public void CancelSkipGuard_NamesTheHeadItGuards() {
        IReadOnlyList<ComposedInstruction> body = Captured(Target(), Head());

        Assert.Equal(OpCodes.Ldloc, body[0].Instruction.opcode);
        Assert.Equal(OpCodes.Brtrue, body[1].Instruction.opcode);
        Assert.Equal(HeadMethod(), body[0].OriginInjection);
        Assert.Equal(HeadMethod(), body[1].OriginInjection);
    }

    [Fact]
    public void SharedGenericGuard_NamesTheHeadItGuards() {
        MethodBase target = typeof(OriginGenericBox<string>).GetMethod(nameof(OriginGenericBox<string>.Ping))!;
        Injection guarded = Head();
        guarded.RequestedInstantiation = typeof(OriginGenericBox<string>);
        IReadOnlyList<ComposedInstruction> body = Captured(target, guarded);

        MethodInfo matches = typeof(SharedGenericGuard).GetMethod(nameof(SharedGenericGuard.Matches))!;
        int call = -1;
        for (int i = 0; i < body.Count; i++) {
            if (Equals(body[i].Instruction.operand, matches)) {
                call = i;
                break;
            }
        }

        Assert.True(call >= 3, "no SharedGenericGuard.Matches call in the composed body, so nothing was guarded");
        Assert.Equal(OpCodes.Brfalse, body[call + 1].Instruction.opcode);
        for (int i = call - 3; i <= call + 1; i++) {
            Assert.Equal(HeadMethod(), body[i].OriginInjection);
        }
    }

    [Fact]
    public void Origins_AreNotRecordedUnlessEnabled() {
        Compose(nameof(OriginProbe.Capture), enabled: false);

        Assert.True(OriginProbe.Total > 0);
        Assert.Equal(0, OriginProbe.Covered());
    }

    [Fact]
    public void CopiedSpine_CarriesEveryOriginalOffset() {
        Compose(nameof(OriginProbe.Capture));

        HashSet<int> original = OriginalOffsets(Target());
        HashSet<int> recorded = [];
        foreach (int offset in OriginProbe.Observed) {
            if (offset != InstructionOrigins.None) {
                recorded.Add(offset);
            }
        }

        Assert.True(original.Count > 1 && original.Max() > 0, "every source offset is 0, so a recorded offset means nothing and every frame would map to the first instruction");
        Assert.Equal(original, recorded);
    }

    [Fact]
    public void InjectedIl_HasNoOrigin() {
        Compose(nameof(OriginProbe.Capture), injectHead: true);
        int coveredWithInjection = OriginProbe.Covered();
        int totalWithInjection = OriginProbe.Total;

        Compose(nameof(OriginProbe.Capture));

        Assert.True(totalWithInjection > OriginProbe.Total);
        Assert.Equal(OriginProbe.Covered(), coveredWithInjection);
    }

    [Fact]
    public void TranspilerClone_KeepsItsOrigin() {
        Compose(nameof(OriginProbe.CloneEveryInstruction));

        Assert.Equal(OriginalOffsets(Target()).Count, Distinct());
    }

    [Fact]
    public void TranspilerCreatedInstruction_HasNoOrigin() {
        Compose(nameof(OriginProbe.PrependNop));

        Assert.Equal(InstructionOrigins.None, OriginProbe.Observed[0]);
        Assert.True(OriginProbe.Covered() > 0);
    }

    [Fact]
    public void Coverage_AcrossPatchShapes() {
        bool[] flags = [false, true];
        foreach (bool head in flags) {
            foreach (bool around in flags) {
                Compose(nameof(OriginProbe.Capture), injectHead: head, injectAround: around);
                output.WriteLine($"Simple head={head} around={around}: {OriginProbe.Covered()}/{OriginProbe.Total} composed instructions carry an original offset");
                Assert.True(OriginProbe.Covered() > 0);
            }
        }
    }

    [Fact]
    public void Coverage_AcrossBodySizes() {
        string[] names = [
            nameof(TranspilerTargets.Simple),
            nameof(TranspilerTargets.MultiCatch),
            nameof(OriginTargets.Medium),
            nameof(OriginTargets.Large),
        ];

        foreach (string name in names) {
            MethodBase target = typeof(TranspilerTargets).GetMethod(name) ?? typeof(OriginTargets).GetMethod(name)!;
            Compose(nameof(OriginProbe.Capture), target: target);
            output.WriteLine($"{name} ({OriginalOffsets(target).Count} original instructions): {OriginProbe.Covered()}/{OriginProbe.Total} composed instructions carry an original offset");
            Assert.True(OriginProbe.Covered() > 0);
        }
    }

    private static int Distinct() {
        HashSet<int> recorded = [];
        foreach (int offset in OriginProbe.Observed) {
            if (offset != InstructionOrigins.None) {
                recorded.Add(offset);
            }
        }

        return recorded.Count;
    }

    private static MethodBase Target() {
        return typeof(TranspilerTargets).GetMethod(nameof(TranspilerTargets.Simple))!;
    }

    private static MethodBase HeadMethod() {
        return typeof(OriginTargets).GetMethod(nameof(OriginTargets.Head))!;
    }

    private static Injection Head() {
        return new Injection(HeadMethod(), new InjectAt.Head(), "origin-guard", 0);
    }

    private static IReadOnlyList<ComposedInstruction> Captured(MethodBase target, params Injection[] injections) {
        WrapperComposer.CaptureComposedBody = true;
        ComposeResult result;
        try {
            result = WrapperComposer.Compose(target, injections);
        } finally {
            WrapperComposer.CaptureComposedBody = false;
        }

        Assert.True(result.Body is not null, "compose refused a body: " + result.BodyRejection);
        return result.Body!.Instructions;
    }

    private static HashSet<int> OriginalOffsets(MethodBase target) {
        using DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        HashSet<int> offsets = [];
        foreach (Instruction instruction in source.Definition.Body.Instructions) {
            offsets.Add(instruction.Offset);
        }

        return offsets;
    }

    private static void Compose(string probe, bool enabled = true, bool injectHead = false, bool injectAround = false, MethodBase? target = null) {
        List<Injection> injections = [];
        if (injectHead) {
            injections.Add(new Injection(typeof(OriginTargets).GetMethod(nameof(OriginTargets.Head))!, new InjectAt.Head(), "origin-head", 0));
        }

        if (injectAround) {
            injections.Add(new Injection(typeof(OriginTargets).GetMethod(nameof(OriginTargets.Around))!, new InjectAt.Around(), "origin-around", 0));
        }

        injections.Add(new Injection(typeof(OriginProbe).GetMethod(probe)!, new InjectAt.Transpiler(true), "origin-probe", 0));

        OriginProbe.Observed = [];
        OriginProbe.Total = 0;
        InstructionOrigins.Enabled = enabled;
        try {
            WrapperComposer.Compose(target ?? Target(), injections);
        } finally {
            InstructionOrigins.Enabled = false;
        }
    }
}
