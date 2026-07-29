using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;

namespace Concord.Emit.Tests;

public static class ProvRules {
    public static int Blend(int first, int second, int third) => first + second + third;
}

public sealed class ProvPair {
    public ProvPair(int left, int right) {
        Sum = left + right;
    }

    public int Sum { get; }
}

public class ProvHost {
    public int Simple(int seed) {
        int a = seed + 1;
        int b = seed + 2;
        return ProvRules.Blend(a, b, 7);
    }

    public int Computed(int seed) {
        return ProvRules.Blend(seed * 2, Helper(seed), 7);
    }

    public int Scaled(int seed) {
        int a = seed + 1;
        return Combine(a, seed * 2);
    }

    public int Branched(int seed, bool flag) {
        int a = seed + 1;
        return ProvRules.Blend(a, flag ? 1 : 2, 7);
    }

    public unsafe int ViaPointer(int seed) {
        delegate*<int, int> fp = &Helper;
        return ProvRules.Blend(seed, fp(seed), 7);
    }

    public int Made(int seed) {
        int a = seed + 1;
        return new ProvPair(a, 7).Sum;
    }

    public int Combine(int left, int right) => left * right;

    private static int Helper(int value) => value - 1;
}

public sealed class CallSiteProvenanceTests {
    private static (DynamicMethodDefinition Source, List<Instruction> Spine, int CallIndex) FindCall(
        string methodName,
        Type declaringType,
        string calleeName,
        bool matchNewObj) {
        MethodBase target = typeof(ProvHost).GetMethod(methodName)!;
        DynamicMethodDefinition source = new DynamicMethodDefinition(target);
        List<Instruction> spine = [.. source.Definition.Body.Instructions];
        List<Instruction> matches = CallSiteQuery.Match(spine, declaringType, calleeName, null, false, matchNewObj);
        return (source, spine, spine.IndexOf(Assert.Single(matches)));
    }

    private static (DynamicMethodDefinition Source, List<Instruction> Spine, int CallIndex) FindBlendCall(string methodName) {
        return FindCall(methodName, typeof(ProvRules), nameof(ProvRules.Blend), false);
    }

    [Fact]
    public void EachArgument_HasADistinctBoundary() {
        (DynamicMethodDefinition source, List<Instruction> spine, int callIndex) = FindBlendCall(nameof(ProvHost.Simple));
        using (source) {
            int first = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 3, false, source.Definition);
            int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 3, false, source.Definition);
            int third = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 2, 3, false, source.Definition);

            Assert.Equal(callIndex - 3, first);
            Assert.Equal(callIndex - 2, second);
            Assert.Equal(callIndex - 1, third);
            Assert.Equal(OpCodes.Ldloc_0, spine[first].OpCode);
            Assert.Equal(OpCodes.Ldloc_1, spine[second].OpCode);
        }
    }

    [Fact]
    public void ComputedArgument_StillResolves() {
        (DynamicMethodDefinition source, List<Instruction> spine, int callIndex) = FindBlendCall(nameof(ProvHost.Computed));
        using (source) {
            int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 3, false, source.Definition);

            Assert.Equal(callIndex - 2, second);
            Assert.Equal(OpCodes.Call, spine[second].OpCode);
        }
    }

    [Fact]
    public void InstanceCall_ResolvesArgumentsAboveTheReceiver() {
        (DynamicMethodDefinition source, List<Instruction> spine, int callIndex) =
            FindCall(nameof(ProvHost.Scaled), typeof(ProvHost), nameof(ProvHost.Combine), false);
        using (source) {
            int first = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 2, true, source.Definition);
            int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 2, true, source.Definition);

            Assert.Equal(callIndex - 4, first);
            Assert.Equal(callIndex - 1, second);
            Assert.Equal(OpCodes.Ldloc_0, spine[first].OpCode);
            Assert.Equal(OpCodes.Mul, spine[second].OpCode);
            Assert.Equal(OpCodes.Ldarg_0, spine[first - 1].OpCode);
        }
    }

    [SkipOnMonoFact]
    public void FunctionPointerArgument_DoesNotResolveToThePointerPush() {
        (DynamicMethodDefinition source, List<Instruction> spine, int callIndex) = FindBlendCall(nameof(ProvHost.ViaPointer));
        using (source) {
            int first = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 3, false, source.Definition);
            int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 3, false, source.Definition);

            Assert.Equal(callIndex - 2, second);
            Assert.Equal(OpCodes.Calli, spine[second].OpCode);
            Assert.Equal(callIndex - 5, first);
            Assert.Equal(OpCodes.Ldloc_1, spine[callIndex - 3].OpCode);
            Assert.NotEqual(callIndex - 3, first);
        }
    }

    [Fact]
    public void NewObjSite_CountsArgumentsWithoutAReceiver() {
        (DynamicMethodDefinition source, List<Instruction> spine, int callIndex) =
            FindCall(nameof(ProvHost.Made), typeof(ProvPair), ".ctor", true);
        using (source) {
            int first = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 2, true, source.Definition);
            int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 2, true, source.Definition);

            Assert.Equal(callIndex - 2, first);
            Assert.Equal(callIndex - 1, second);
            Assert.Equal(OpCodes.Ldloc_0, spine[first].OpCode);
        }
    }

    [Fact]
    public void ArityDisagreeingWithTheCall_ReturnsMinusOne() {
        (DynamicMethodDefinition source, List<Instruction> spine, int callIndex) = FindBlendCall(nameof(ProvHost.Simple));
        using (source) {
            Assert.Equal(-1, CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 2, false, source.Definition));
            Assert.Equal(-1, CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 4, false, source.Definition));
            Assert.Equal(-1, CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 3, true, source.Definition));
        }
    }

    [Fact]
    public void BranchTargetInsideArguments_ReturnsMinusOne() {
        (DynamicMethodDefinition source, List<Instruction> spine, int callIndex) = FindBlendCall(nameof(ProvHost.Branched));
        using (source) {
            int first = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 3, false, source.Definition);
            int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 3, false, source.Definition);
            int third = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 2, 3, false, source.Definition);

            Assert.Equal(-1, first);
            Assert.Equal(-1, second);
            Assert.Equal(callIndex - 1, third);
        }
    }

    [Fact]
    public void UndeterminableBoundary_ReturnsMinusOne() {
        (DynamicMethodDefinition source, List<Instruction> spine, _) = FindBlendCall(nameof(ProvHost.Simple));
        using (source) {
            Assert.Equal(-1, CallSiteProvenance.FindArgumentPushEnd(spine, 0, 0, 3, false, source.Definition));
        }
    }
}
