using System.Reflection;
using Concord.Emit;
using Concord.Emit.Tests.NeutralRoundTrip;
using Xunit;

namespace Concord.Emit.Tests;

public static class BodyTransformerTargets {
    public static int HeadObserved;

    public static int Target(int value) {
        return value + 1;
    }

    public static void HeadInjection() {
        HeadObserved++;
    }
}

public class BodyTransformerTests {
    [Fact]
    public void TransformComposesAgainstSuppliedBody() {
        MethodBase target = typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.Target))!;
        NeutralBody supplied = BodyTransformer.FromMethod(target);
        Injection head = new Injection(
            typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.HeadInjection))!,
            new InjectAt.Head(),
            "test",
            0);
        NeutralBody composed = BodyTransformer.Transform(target, supplied, [head]);
        Assert.NotSame(supplied, composed);
        MethodInfo generated = RoundTrip.Generate(composed, target);
        BodyTransformerTargets.HeadObserved = 0;
        object? result = generated.Invoke(null, [41]);
        Assert.Equal(42, result);
        Assert.Equal(1, BodyTransformerTargets.HeadObserved);
    }

    [Fact]
    public void TransformAllocatesFreshLabelsAboveIncomingIds() {
        MethodBase target = typeof(BranchingTargets).GetMethod(nameof(BranchingTargets.SwitchOnValue))!;
        NeutralBody supplied = BodyTransformer.FromMethod(target);
        Injection head = new Injection(
            typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.HeadInjection))!,
            new InjectAt.Head(),
            "test",
            0);
        NeutralBody composed = BodyTransformer.Transform(target, supplied, [head]);

        HashSet<int> attached = new HashSet<int>();
        foreach (NeutralInstruction instruction in composed.Instructions) {
            foreach (int labelId in instruction.Labels) {
                Assert.True(attached.Add(labelId), $"label id {labelId} is attached to two instructions");
            }
        }

        foreach (NeutralInstruction instruction in composed.Instructions) {
            if (instruction.Operand.Kind == NeutralOperandKind.Label) {
                Assert.Contains(instruction.Operand.AsLabelId(), attached);
            } else if (instruction.Operand.Kind == NeutralOperandKind.SwitchLabels) {
                foreach (int labelId in instruction.Operand.AsSwitchLabelIds()) {
                    Assert.Contains(labelId, attached);
                }
            }
        }

        MethodInfo generated = RoundTrip.Generate(composed, target);
        Assert.Equal(30, generated.Invoke(null, [2]));
        Assert.Equal(-1, generated.Invoke(null, [99]));
    }

    [Fact]
    public void TransformRunsComposeValidation() {
        MethodBase target = typeof(BranchingTargets).GetMethod(nameof(BranchingTargets.ClassifyAndDouble))!;
        NeutralBody supplied = BodyTransformer.FromMethod(target);
        Injection around = new Injection(
            typeof(BranchingInjectionMethods).GetMethod(nameof(BranchingInjectionMethods.AroundClassify))!,
            new InjectAt.Around(),
            "test",
            0);
        Injection invoke = new Injection(
            typeof(BodyTransformerTargets).GetMethod(nameof(BodyTransformerTargets.HeadInjection))!,
            new InjectAt.Invoke(typeof(BranchingTargets), nameof(BranchingTargets.SwitchOnValue), At.Head),
            "test",
            1);
        Assert.Throws<ConcordEmitException>(() => BodyTransformer.Transform(target, supplied, [around, invoke]));
    }
}
