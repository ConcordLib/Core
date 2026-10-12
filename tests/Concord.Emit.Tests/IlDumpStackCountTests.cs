using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Concord.Emit.Tests;

public static class StelemTarget {
    public static void Fill<T>(T[] array, T value) {
        array[0] = value;
    }

    public static void FillInts(int[] array, int value) {
        array[0] = value;
    }
}

public sealed class IlDumpStackCountTests {
    [Fact]
    public void StelemAnyPopsTheArrayTheIndexAndTheValue() {
        Assert.Equal(3, IlDump.PopCount(Shaped(OpCodes.Stelem_Any)));
        Assert.Equal(0, IlDump.PushCount(Shaped(OpCodes.Stelem_Any)));
    }

    [Fact]
    public void EveryStelemFlavourPopsTheArrayTheIndexAndTheValue() {
        foreach (OpCode opcode in AllOpCodes()) {
            if (!opcode.Name!.StartsWith("stelem", System.StringComparison.Ordinal)) {
                continue;
            }

            Assert.Equal(3, IlDump.PopCount(Shaped(opcode)));
        }
    }

    [Fact]
    public void AGenericStelemBodyWalksToADepthOfThreeAndDrainsToZero() {
        Assert.Equal((3, 0), DepthOf(nameof(StelemTarget.Fill)));
        Assert.Equal((3, 0), DepthOf(nameof(StelemTarget.FillInts)));
    }

    [Fact]
    public void AProductionWalkFindsTheGenericStelemAsTheConsumerOfTheArrayItLoads() {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(typeof(StelemTarget).Assembly.Location);
        MethodDefinition method = assembly.MainModule.GetType(typeof(StelemTarget).FullName).Methods
            .Single(m => m.Name == nameof(StelemTarget.Fill));
        Instruction stelem = method.Body.Instructions.Single(i => i.OpCode == OpCodes.Stelem_Any);
        Instruction loadArray = method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldarg_0);

        Instruction? consumer = BodyCopier.FindStackConsumer(loadArray, 1, out bool blockedByFlow);

        Assert.Same(stelem, consumer);
        Assert.False(blockedByFlow);
    }

    private static (int Deepest, int Final) DepthOf(string name) {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(typeof(StelemTarget).Assembly.Location);
        MethodDefinition method = assembly.MainModule.GetType(typeof(StelemTarget).FullName).Methods.Single(m => m.Name == name);
        int depth = 0;
        int deepest = 0;
        foreach (Instruction instruction in method.Body.Instructions) {
            depth = depth - IlDump.PopCount(instruction, method) + IlDump.PushCount(instruction);
            if (depth > deepest) {
                deepest = depth;
            }
        }

        return (deepest, depth);
    }

    [Fact]
    public void EveryPopBehaviourTheEnumDefinesCountsTheSlotsItsNameLists() {
        Dictionary<StackBehaviour, OpCode> declared = OneOpcodePerPopBehaviour();

        Assert.Equal(
            Members("Pop", StackBehaviour.Varpop, StackBehaviour.PopAll),
            Covered(declared, StackBehaviour.Varpop, StackBehaviour.PopAll));

        StringBuilder wrong = new StringBuilder();
        foreach (KeyValuePair<StackBehaviour, OpCode> entry in declared) {
            if (entry.Key == StackBehaviour.Varpop || entry.Key == StackBehaviour.PopAll) {
                continue;
            }

            int expected = SlotsInName(entry.Key.ToString(), "pop");
            int actual = IlDump.PopCount(Shaped(entry.Value));
            if (actual != expected) {
                wrong.Append(entry.Key).Append(" (").Append(entry.Value.Name).Append("): expected ")
                    .Append(expected).Append(", got ").Append(actual).Append('\n');
            }
        }

        Assert.Equal(string.Empty, wrong.ToString());
    }

    [Fact]
    public void EveryPushBehaviourTheEnumDefinesCountsTheSlotsItsNameLists() {
        Dictionary<StackBehaviour, OpCode> declared = OneOpcodePerPushBehaviour();

        Assert.Equal(Members("Push", StackBehaviour.Varpush), Covered(declared, StackBehaviour.Varpush));

        StringBuilder wrong = new StringBuilder();
        foreach (KeyValuePair<StackBehaviour, OpCode> entry in declared) {
            if (entry.Key == StackBehaviour.Varpush) {
                continue;
            }

            int expected = SlotsInName(entry.Key.ToString(), "push");
            int actual = IlDump.PushCount(Shaped(entry.Value));
            if (actual != expected) {
                wrong.Append(entry.Key).Append(" (").Append(entry.Value.Name).Append("): expected ")
                    .Append(expected).Append(", got ").Append(actual).Append('\n');
            }
        }

        Assert.Equal(string.Empty, wrong.ToString());
    }

    private static string Members(string prefix, params StackBehaviour[] excluded) {
        List<string> names = new List<string>();
        foreach (string name in System.Enum.GetNames(typeof(StackBehaviour))) {
            StackBehaviour value = (StackBehaviour)System.Enum.Parse(typeof(StackBehaviour), name);
            if (name.StartsWith(prefix, System.StringComparison.Ordinal) && !excluded.Contains(value)) {
                names.Add(name);
            }
        }

        names.Sort(System.StringComparer.Ordinal);
        return string.Join(",", names);
    }

    private static string Covered(Dictionary<StackBehaviour, OpCode> declared, params StackBehaviour[] excluded) {
        List<string> names = new List<string>();
        foreach (StackBehaviour behaviour in declared.Keys) {
            if (!excluded.Contains(behaviour)) {
                names.Add(behaviour.ToString());
            }
        }

        names.Sort(System.StringComparer.Ordinal);
        return string.Join(",", names);
    }

    private static Instruction Shaped(OpCode opcode) {
        Instruction instruction = Instruction.Create(OpCodes.Nop);
        instruction.OpCode = opcode;
        return instruction;
    }

    private static int SlotsInName(string name, string verb) {
        if (string.Equals(name, verb + "0", System.StringComparison.OrdinalIgnoreCase)) {
            return 0;
        }

        int slots = 0;
        foreach (string segment in name.Split('_')) {
            if (segment.StartsWith(verb, System.StringComparison.OrdinalIgnoreCase)) {
                slots++;
            }
        }

        return slots;
    }

    private static Dictionary<StackBehaviour, OpCode> OneOpcodePerPopBehaviour() {
        Dictionary<StackBehaviour, OpCode> map = new Dictionary<StackBehaviour, OpCode>();
        foreach (OpCode opcode in AllOpCodes()) {
#if NET472
            if (!map.ContainsKey(opcode.StackBehaviourPop)) {
                map.Add(opcode.StackBehaviourPop, opcode);
            }
#else
            map.TryAdd(opcode.StackBehaviourPop, opcode);
#endif
        }

        return map;
    }

    private static Dictionary<StackBehaviour, OpCode> OneOpcodePerPushBehaviour() {
        Dictionary<StackBehaviour, OpCode> map = new Dictionary<StackBehaviour, OpCode>();
        foreach (OpCode opcode in AllOpCodes()) {
#if NET472
            if (!map.ContainsKey(opcode.StackBehaviourPush)) {
                map.Add(opcode.StackBehaviourPush, opcode);
            }
#else
            map.TryAdd(opcode.StackBehaviourPush, opcode);
#endif
        }

        return map;
    }

    private static List<OpCode> AllOpCodes() {
        List<OpCode> all = new List<OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (field.FieldType == typeof(OpCode)) {
                all.Add((OpCode)field.GetValue(null)!);
            }
        }

        Assert.True(all.Count > 200, "expected Cecil to define over 200 opcodes, saw " + all.Count);
        return all;
    }
}
