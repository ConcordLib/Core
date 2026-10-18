using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests;

using CodeInstruction = HarmonyLib.CodeInstruction;
using Label = System.Reflection.Emit.Label;

public static class TranspilerAdapterTargets
{
    public static Assembly? Observed;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Priced(int unused)
    {
        return 5;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Scaled(int x)
    {
        return x + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Branchy(int x)
    {
        return x;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Doubled(int x)
    {
        return x * 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Squared(int seed)
    {
        int first = seed + 1;
        int second = first * first;
        return second;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Incremented(int seed)
    {
        int first = seed + 1;
        System.Threading.Interlocked.Increment(ref first);
        return first;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int WithCatch(int mode)
    {
        try
        {
            if (mode == 1)
            {
                throw new InvalidOperationException();
            }

            return 5;
        }
        catch (InvalidOperationException)
        {
            return 7;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int SelfReporting(int unused)
    {
        Assembly? seen = Observed;
        Observed = null;
        return seen == typeof(TranspilerAdapterTargets).Assembly ? 1 : 0;
    }
}

public class TranspilerAdapterTranspilers
{
    public static MethodBase? SeenOriginal;

    public static IEnumerable<CodeInstruction> FiveToTen(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction.opcode == OpCodes.Ldc_I4_5 ? new CodeInstruction(OpCodes.Ldc_I4, 10) : instruction;
        }
    }

    public static IEnumerable<CodeInstruction> RetypeShortConstant(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_S)
            {
                instruction.operand = -1;
            }

            yield return instruction;
        }
    }

    public static IEnumerable<CodeInstruction> StashThroughNumberedLocal(IEnumerable<CodeInstruction> instructions, ILGenerator ilg)
    {
        LocalBuilder stash = ilg.DeclareLocal(typeof(string));
        bool stashed = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (!stashed && instruction.opcode == OpCodes.Ldarg_0)
            {
                yield return new CodeInstruction(OpCodes.Ldstr, "stashed");
                yield return new CodeInstruction(OpCodes.Stloc, ((LocalVariableInfo)stash).LocalIndex);
                yield return new CodeInstruction(OpCodes.Ldloc, ((LocalVariableInfo)stash).LocalIndex);
                yield return CodeInstruction.Call(typeof(string), "get_Length");
                yield return new CodeInstruction(OpCodes.Pop);
                stashed = true;
            }

            yield return instruction;
        }
    }

    public static IEnumerable<CodeInstruction> NopOutCallKeepingOperand(IEnumerable<CodeInstruction> instructions)
    {
        yield return new CodeInstruction(OpCodes.Nop, AccessTools.Method(typeof(TranspilerAdapterTargets), nameof(TranspilerAdapterTargets.Doubled)));
        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;
        }
    }

    public static IEnumerable<CodeInstruction> TenToTwenty(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int value && value == 10 ? new CodeInstruction(OpCodes.Ldc_I4, 20) : instruction;
        }
    }

    public static IEnumerable<CodeInstruction> SevenToSeventy(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction.opcode == OpCodes.Ldc_I4_7 ? new CodeInstruction(OpCodes.Ldc_I4, 70) : instruction;
        }
    }

    public static IEnumerable<CodeInstruction> BranchOnZero(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        Label skip = generator.DefineLabel();
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Brtrue, skip);
        yield return new CodeInstruction(OpCodes.Ldc_I4, 42);
        yield return new CodeInstruction(OpCodes.Ret);
        yield return new CodeInstruction(OpCodes.Nop).WithLabels(skip);
        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;
        }
    }

    public static IEnumerable<CodeInstruction> AddHundredViaLocal(ILGenerator generator, IEnumerable<CodeInstruction> instructions)
    {
        LocalBuilder scratch = generator.DeclareLocal(typeof(int));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ret)
            {
                yield return new CodeInstruction(OpCodes.Stloc, scratch);
                yield return new CodeInstruction(OpCodes.Ldloc, scratch);
                yield return new CodeInstruction(OpCodes.Ldc_I4, 100);
                yield return new CodeInstruction(OpCodes.Add);
            }

            yield return instruction;
        }
    }

    public static IEnumerable<CodeInstruction> ReturnFirstLocal(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        SeenOriginal = original;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ret)
            {
                yield return new CodeInstruction(OpCodes.Pop);
                yield return new CodeInstruction(OpCodes.Ldloc_S, (byte)0);
            }

            yield return instruction;
        }
    }

    public static IEnumerable<CodeInstruction> ObserveExecutingAssembly(IEnumerable<CodeInstruction> instructions)
    {
        yield return new CodeInstruction(OpCodes.Call, typeof(Assembly).GetMethod(nameof(Assembly.GetExecutingAssembly)));
        yield return new CodeInstruction(OpCodes.Stsfld, typeof(TranspilerAdapterTargets).GetField(nameof(TranspilerAdapterTargets.Observed)));
        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;
        }
    }

    public static IEnumerable<CodeInstruction> PassThrough(IEnumerable<CodeInstruction> instructions)
    {
        return instructions;
    }

    public static IEnumerable<CodeInstruction> TakesString(IEnumerable<CodeInstruction> instructions, string extra)
    {
        return instructions;
    }

    public IEnumerable<CodeInstruction> NotStatic(IEnumerable<CodeInstruction> instructions)
    {
        return instructions;
    }
}

[Collection("HarmonySerial")]
public sealed class HarmonyTranspilerAdapterTests
{
    private const int High = 600;
    private const int Low = 200;

    [Fact]
    public void Map_TagsTheInjectionAsForeignAndKeepsTheOwner()
    {
        Injection injection = HarmonyTranspilerAdapter.Map(Transpiler(nameof(TranspilerAdapterTranspilers.FiveToTen)), "test.owner");

        Assert.True(injection.ForeignOrigin);
        Assert.Equal("test.owner", injection.Owner);
        Assert.IsType<InjectAt.Transpiler>(injection.At);
        Assert.False(((InjectAt.Transpiler)injection.At).Final);
        Assert.Empty(injection.BeforeOwners);
        Assert.Empty(injection.AfterOwners);
    }

    [Fact]
    public void OpcodeRewrite_MatchesHarmony()
    {
        AssertParity(nameof(TranspilerAdapterTargets.Priced), x => TranspilerAdapterTargets.Priced(x), new[] { 0 }, new[] { "ok:10" },
            (nameof(TranspilerAdapterTranspilers.FiveToTen), High));
    }

    [Fact]
    public void ShortConstantRetypedToInt_MatchesHarmony()
    {
        AssertParity(nameof(TranspilerAdapterTargets.Scaled), x => TranspilerAdapterTargets.Scaled(x), new[] { 0 }, new[] { "ok:-1" },
            (nameof(TranspilerAdapterTranspilers.RetypeShortConstant), High));
    }

    [Fact]
    public void LocalDeclaredOnGeneratorAndUsedBySlotNumber_MatchesHarmony()
    {
        AssertParity(nameof(TranspilerAdapterTargets.Doubled), x => TranspilerAdapterTargets.Doubled(x), new[] { 3 }, new[] { "ok:6" },
            (nameof(TranspilerAdapterTranspilers.StashThroughNumberedLocal), High));
    }

    [Fact]
    public void OpcodeChangedToNopKeepingItsOperand_MatchesHarmony()
    {
        AssertParity(nameof(TranspilerAdapterTargets.Squared), x => TranspilerAdapterTargets.Squared(x), new[] { 3 }, new[] { "ok:16" },
            (nameof(TranspilerAdapterTranspilers.NopOutCallKeepingOperand), High));
    }

    [Fact]
    public void MintedLabelAndBranch_MatchesHarmony()
    {
        AssertParity(nameof(TranspilerAdapterTargets.Branchy), x => TranspilerAdapterTargets.Branchy(x), new[] { 0, 3 }, new[] { "ok:42", "ok:3" },
            (nameof(TranspilerAdapterTranspilers.BranchOnZero), High));
    }

    [Fact]
    public void DeclaredLocal_MatchesHarmony()
    {
        AssertParity(nameof(TranspilerAdapterTargets.Doubled), x => TranspilerAdapterTargets.Doubled(x), new[] { 1 }, new[] { "ok:102" },
            (nameof(TranspilerAdapterTranspilers.AddHundredViaLocal), High));
    }

    [Fact]
    public void BareSlotNumberAndOriginalParameter_MatchHarmony()
    {
        MethodInfo target = Method(typeof(TranspilerAdapterTargets), nameof(TranspilerAdapterTargets.Squared));

        AssertParity(nameof(TranspilerAdapterTargets.Squared), x => TranspilerAdapterTargets.Squared(x), new[] { 3 }, new[] { "ok:4" },
            (nameof(TranspilerAdapterTranspilers.ReturnFirstLocal), High));

        Assert.Same(target, TranspilerAdapterTranspilers.SeenOriginal);
    }

    [Fact]
    public void GetExecutingAssemblyInsidePatchedBody_StillObservesTheTargetAssembly()
    {
        TranspilerAdapterTargets.Observed = null;

        AssertParity(nameof(TranspilerAdapterTargets.SelfReporting), x => TranspilerAdapterTargets.SelfReporting(x), new[] { 0 }, new[] { "ok:1" },
            (nameof(TranspilerAdapterTranspilers.ObserveExecutingAssembly), High));
    }

    [Fact]
    public void LocalReachedByCompactAndOperandForms_StaysOneLocal()
    {
        AssertParity(nameof(TranspilerAdapterTargets.Incremented), x => TranspilerAdapterTargets.Incremented(x), new[] { 3 }, new[] { "ok:5" },
            (nameof(TranspilerAdapterTranspilers.PassThrough), High));
    }

    [Fact]
    public void TryCatchBody_SurvivesTheRoundTrip()
    {
        AssertParity(nameof(TranspilerAdapterTargets.WithCatch), x => TranspilerAdapterTargets.WithCatch(x), new[] { 0, 1 }, new[] { "ok:5", "ok:70" },
            (nameof(TranspilerAdapterTranspilers.SevenToSeventy), High));
    }

    [Fact]
    public void TwoTranspilers_RunInHarmonysPriorityOrderNotRegistrationOrder()
    {
        AssertParity(nameof(TranspilerAdapterTargets.Priced), x => TranspilerAdapterTargets.Priced(x), new[] { 0 }, new[] { "ok:20" },
            (nameof(TranspilerAdapterTranspilers.TenToTwenty), Low),
            (nameof(TranspilerAdapterTranspilers.FiveToTen), High));
    }

    [Fact]
    public void TwoForeignTranspilers_WithHarmonyBeforeAfter_MatchHarmonysActualRunOrder()
    {
        MethodInfo target = Method(typeof(TranspilerAdapterTargets), nameof(TranspilerAdapterTargets.Priced));
        string[] baseline = Record(x => TranspilerAdapterTargets.Priced(x), new[] { 0 });

        string ownerA = "test.transpileradapter.beforeafter.a";
        string ownerB = "test.transpileradapter.beforeafter.b";
        HarmonyLib.Harmony harmonyA = new HarmonyLib.Harmony(ownerA);
        HarmonyLib.Harmony harmonyB = new HarmonyLib.Harmony(ownerB);
        string[] viaHarmony;
        IReadOnlyList<Injection> mapped;
        try
        {
            harmonyB.Patch(target, transpiler: new HarmonyMethod(Transpiler(nameof(TranspilerAdapterTranspilers.TenToTwenty))));
            harmonyA.Patch(target, transpiler: new HarmonyMethod(Transpiler(nameof(TranspilerAdapterTranspilers.FiveToTen))) { before = new[] { ownerB } });

            viaHarmony = Record(x => TranspilerAdapterTargets.Priced(x), new[] { 0 });

            Patches info = HarmonyLib.Harmony.GetPatchInfo(target);
            mapped = HarmonyTranspilerAdapter.MapForLiveList(HarmonyPatchSort.SortCategory(target, info.Transpilers));
        }
        finally
        {
            TestUnpatch.Own(harmonyA, ownerA);
            TestUnpatch.Own(harmonyB, ownerB);
        }

        AssertSame("reset vs baseline", baseline, Record(x => TranspilerAdapterTargets.Priced(x), new[] { 0 }));

        List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
        foreach (Injection injection in mapped)
        {
            live.Add((live.Count, injection));
        }

        Injection[] ordered = InjectionOrderer.OrderForComposition(live);
        ComposeResult composed = WrapperComposer.Compose(target, ordered);
        Assert.Empty(composed.Rejected);
        Func<int, int> wrapper = (Func<int, int>)composed.Wrapper.CreateDelegate(typeof(Func<int, int>));
        string[] viaConcord = Record(wrapper, new[] { 0 });

        AssertSame("concord vs harmony", viaHarmony, viaConcord);
    }

    [Fact]
    public void RefusedSignature_ThrowsConc116NamingTheSignature()
    {
        ConcordEmitException withString = Assert.Throws<ConcordEmitException>(
            () => HarmonyTranspilerAdapter.Map(Transpiler(nameof(TranspilerAdapterTranspilers.TakesString)), "test.owner"));
        Assert.Equal("CONC116", withString.Code);
        Assert.Contains("IEnumerable`1 instructions, String extra", withString.Message);
        Assert.Contains("'extra'", withString.Message);

        ConcordEmitException instance = Assert.Throws<ConcordEmitException>(
            () => HarmonyTranspilerAdapter.Map(Transpiler(nameof(TranspilerAdapterTranspilers.NotStatic)), "test.owner"));
        Assert.Equal("CONC116", instance.Code);
        Assert.Contains("instance method", instance.Message);
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static MethodInfo Transpiler(string name)
    {
        return Method(typeof(TranspilerAdapterTranspilers), name);
    }

    private static void AssertParity(string targetName, Func<int, int> call, int[] inputs, string[] expected, params (string Name, int Priority)[] transpilers)
    {
        MethodInfo target = Method(typeof(TranspilerAdapterTargets), targetName);
        string[] baseline = Record(call, inputs);

        string harmonyId = "test.transpileradapter." + targetName;
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(harmonyId);
        string[] viaHarmony;
        IReadOnlyList<Injection> mapped;
        try
        {
            foreach ((string name, int priority) in transpilers)
            {
                harmony.Patch(target, transpiler: new HarmonyMethod(Transpiler(name)) { priority = priority });
            }

            viaHarmony = Record(call, inputs);

            Patches info = HarmonyLib.Harmony.GetPatchInfo(target);
            mapped = HarmonyTranspilerAdapter.MapForLiveList(HarmonyPatchSort.SortCategory(target, info.Transpilers));
        }
        finally
        {
            TestUnpatch.Own(harmony, harmonyId);
        }

        AssertSame("reset vs baseline", baseline, Record(call, inputs));

        List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();
        foreach (Injection injection in mapped)
        {
            live.Add((live.Count, injection));
        }

        Injection[] ordered = InjectionOrderer.OrderForComposition(live);
        ComposeResult composed = WrapperComposer.Compose(target, ordered);
        Assert.Empty(composed.Rejected);
        Func<int, int> wrapper = (Func<int, int>)composed.Wrapper.CreateDelegate(typeof(Func<int, int>));
        string[] viaConcord = Record(wrapper, inputs);

        AssertSame("harmony vs expected", expected, viaHarmony);
        AssertSame("concord vs harmony", viaHarmony, viaConcord);
    }

    private static void AssertSame(string label, string[] expected, string[] actual)
    {
        string expectedText = string.Join(" ;; ", expected);
        string actualText = string.Join(" ;; ", actual);
        Assert.True(expectedText == actualText, label + "\nexpected: " + expectedText + "\nactual:   " + actualText);
    }

    private static string[] Record(Func<int, int> call, int[] inputs)
    {
        string[] outcomes = new string[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            try
            {
                outcomes[i] = "ok:" + call(inputs[i]);
            }
            catch (Exception error)
            {
                outcomes[i] = "threw:" + error.GetType().Name;
            }
        }

        return outcomes;
    }
}
