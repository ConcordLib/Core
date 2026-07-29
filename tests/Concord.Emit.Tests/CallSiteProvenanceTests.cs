using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;

namespace Concord.Emit.Tests;

public static class ProvRules {
    public static int Blend(int first, int second, int third) => first + second + third;
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

    public int Combine(int left, int right) => left * right;

    private static int Helper(int value) => value - 1;
}

public sealed class CallSiteProvenanceTests {
    private static MethodDefinition BodyOf(string methodName) {
        MethodBase target = typeof(ProvHost).GetMethod(methodName)!;
        return new DynamicMethodDefinition(target).Definition;
    }

    private static (List<Instruction> Spine, int CallIndex) FindCall(string methodName, Type declaringType, string calleeName) {
        List<Instruction> spine = [.. BodyOf(methodName).Body.Instructions];
        List<Instruction> matches = CallSiteQuery.Match(spine, declaringType, calleeName, null, false, false);
        return (spine, spine.IndexOf(Assert.Single(matches)));
    }

    private static (List<Instruction> Spine, int CallIndex) FindBlendCall(string methodName) {
        return FindCall(methodName, typeof(ProvRules), nameof(ProvRules.Blend));
    }

    [Fact]
    public void EachArgument_HasADistinctBoundary() {
        (List<Instruction> spine, int callIndex) = FindBlendCall(nameof(ProvHost.Simple));

        int first = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 3, false, BodyOf(nameof(ProvHost.Simple)));
        int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 3, false, BodyOf(nameof(ProvHost.Simple)));
        int third = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 2, 3, false, BodyOf(nameof(ProvHost.Simple)));

        Assert.Equal(callIndex - 3, first);
        Assert.Equal(callIndex - 2, second);
        Assert.Equal(callIndex - 1, third);
        Assert.Equal(OpCodes.Ldloc_0, spine[first].OpCode);
        Assert.Equal(OpCodes.Ldloc_1, spine[second].OpCode);
    }

    [Fact]
    public void ComputedArgument_StillResolves() {
        (List<Instruction> spine, int callIndex) = FindBlendCall(nameof(ProvHost.Computed));

        int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 3, false, BodyOf(nameof(ProvHost.Computed)));

        Assert.Equal(callIndex - 2, second);
        Assert.Equal(OpCodes.Call, spine[second].OpCode);
    }

    [Fact]
    public void InstanceCall_ResolvesArgumentsAboveTheReceiver() {
        (List<Instruction> spine, int callIndex) = FindCall(nameof(ProvHost.Scaled), typeof(ProvHost), nameof(ProvHost.Combine));

        int first = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 2, true, BodyOf(nameof(ProvHost.Scaled)));
        int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 2, true, BodyOf(nameof(ProvHost.Scaled)));

        Assert.Equal(callIndex - 4, first);
        Assert.Equal(callIndex - 1, second);
        Assert.Equal(OpCodes.Ldloc_0, spine[first].OpCode);
        Assert.Equal(OpCodes.Mul, spine[second].OpCode);
        Assert.Equal(OpCodes.Ldarg_0, spine[first - 1].OpCode);
    }

    [Fact]
    public void BranchTargetInsideArguments_ReturnsMinusOne() {
        (List<Instruction> spine, int callIndex) = FindBlendCall(nameof(ProvHost.Branched));

        int first = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 0, 3, false, BodyOf(nameof(ProvHost.Branched)));
        int second = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 1, 3, false, BodyOf(nameof(ProvHost.Branched)));
        int third = CallSiteProvenance.FindArgumentPushEnd(spine, callIndex, 2, 3, false, BodyOf(nameof(ProvHost.Branched)));

        Assert.Equal(-1, first);
        Assert.Equal(-1, second);
        Assert.Equal(callIndex - 1, third);
    }

    [Fact]
    public void UndeterminableBoundary_ReturnsMinusOne() {
        (List<Instruction> spine, _) = FindBlendCall(nameof(ProvHost.Simple));

        Assert.Equal(-1, CallSiteProvenance.FindArgumentPushEnd(spine, 0, 0, 3, false, BodyOf(nameof(ProvHost.Simple))));
    }
}
