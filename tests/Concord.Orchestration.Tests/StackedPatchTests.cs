using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class StackedTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 0;
    }
}

public static class StackedFirstInjection {
    public static void Add10(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 10;
    }
}

public static class StackedSecondInjection {
    public static void Add100(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 100;
    }
}

public static class PriorityTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 5;
    }
}

[Patch(typeof(PriorityTarget))]
public static class PriorityFirstLow {
    [Inject(At.Tail, nameof(PriorityTarget.Value), Priority = 1)]
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 1;
    }
}

[Patch(typeof(PriorityTarget))]
public static class PrioritySecondHigh {
    [Inject(At.Tail, nameof(PriorityTarget.Value), Priority = 2)]
    public static void MultiplyTwo(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue * 2;
    }
}

public static class OrderedTailTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }
}

[Patch(typeof(OrderedTailTarget))]
[PatchBefore(typeof(BeforeMultiplyLow))]
public static class BeforeAddHigh {
    [Inject(At.Tail, nameof(OrderedTailTarget.Value), Priority = 10)]
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }
}

[Patch(typeof(OrderedTailTarget))]
public static class BeforeMultiplyLow {
    [Inject(At.Tail, nameof(OrderedTailTarget.Value), Priority = 0)]
    public static void MultiplyTen(ControlHandle<int> ch) {
        ch.ReturnValue *= 10;
    }
}

[Patch(typeof(OrderedTailTarget))]
[PatchAfter(typeof(AfterMultiplyHigh))]
public static class AfterAddLow {
    [Inject(At.Tail, nameof(OrderedTailTarget.Value), Priority = 0)]
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }
}

[Patch(typeof(OrderedTailTarget))]
public static class AfterMultiplyHigh {
    [Inject(At.Tail, nameof(OrderedTailTarget.Value), Priority = 10)]
    public static void MultiplyTen(ControlHandle<int> ch) {
        ch.ReturnValue *= 10;
    }
}

[Patch(typeof(OrderedTailTarget))]
[PatchBefore(typeof(LateMultiplyLow))]
public static class LateAddHigh {
    [Inject(At.Tail, nameof(OrderedTailTarget.Value), Priority = 10)]
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }
}

[Patch(typeof(OrderedTailTarget))]
public static class LateMultiplyLow {
    [Inject(At.Tail, nameof(OrderedTailTarget.Value), Priority = 0)]
    public static void MultiplyTen(ControlHandle<int> ch) {
        ch.ReturnValue *= 10;
    }
}

public static class EqualPriorityAdd {
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }
}

public static class EqualPriorityMultiply {
    public static void MultiplyTen(ControlHandle<int> ch) {
        ch.ReturnValue *= 10;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class StackedPatchTests {
    [Fact]
    public void TwoInjectionsOnOneTarget_BothRun() {
        MethodBase target = typeof(StackedTarget).GetMethod(nameof(StackedTarget.Value))!;
        MethodBase first = typeof(StackedFirstInjection).GetMethod(nameof(StackedFirstInjection.Add10))!;
        MethodBase second = typeof(StackedSecondInjection).GetMethod(nameof(StackedSecondInjection.Add100))!;

        Assert.Equal(0, StackedTarget.Value());

        IPatchHandle h1 = Patcher.Patch(target, first, At.Tail);
        IPatchHandle h2 = Patcher.Patch(target, second, At.Tail);
        try {
            Assert.Equal(110, StackedTarget.Value());
        } finally {
            h2.Dispose();
            h1.Dispose();
        }

        Assert.Equal(0, StackedTarget.Value());
    }

    [Fact]
    public void OutOfOrderDispose_RestoresOriginal_AndSurvivingPatchRuns() {
        MethodBase target = typeof(StackedTarget).GetMethod(nameof(StackedTarget.Value))!;
        MethodBase first = typeof(StackedFirstInjection).GetMethod(nameof(StackedFirstInjection.Add10))!;
        MethodBase second = typeof(StackedSecondInjection).GetMethod(nameof(StackedSecondInjection.Add100))!;

        IPatchHandle h1 = Patcher.Patch(target, first, At.Tail);
        IPatchHandle h2 = Patcher.Patch(target, second, At.Tail);

        h1.Dispose();
        Assert.Equal(100, StackedTarget.Value());

        h2.Dispose();
        Assert.Equal(0, StackedTarget.Value());
    }

    [Fact]
    public void Priority_DeterminesTailOrder() {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();
        PatchDeclarationScanner.ScanType(typeof(PriorityFirstLow), applier, props);
        PatchDeclarationScanner.ScanType(typeof(PrioritySecondHigh), applier, props);

        try {
            Assert.Equal(12, PriorityTarget.Value());
        } finally {
            foreach (IDetourHandle handle in applier.Handles) {
                handle.Dispose();
            }
        }
    }

    [Fact]
    public void PatchBefore_OverridesPriorityAndScanOrder() {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();
        PatchDeclarationScanner.ScanType(typeof(BeforeMultiplyLow), applier, props);
        PatchDeclarationScanner.ScanType(typeof(BeforeAddHigh), applier, props);

        try {
            Assert.Equal(20, OrderedTailTarget.Value());
        } finally {
            foreach (IDetourHandle handle in applier.Handles.Reverse()) {
                handle.Dispose();
            }
        }
    }

    [Fact]
    public void PatchAfter_OverridesPriority() {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();
        PatchDeclarationScanner.ScanType(typeof(AfterAddLow), applier, props);
        PatchDeclarationScanner.ScanType(typeof(AfterMultiplyHigh), applier, props);

        try {
            Assert.Equal(11, OrderedTailTarget.Value());
        } finally {
            foreach (IDetourHandle handle in applier.Handles.Reverse()) {
                handle.Dispose();
            }
        }
    }

    [Fact]
    public void MissingOwner_ActivatesWhenApplied_AndDisappearsWhenRemoved() {
        CollectingPatchApplier constrained = new CollectingPatchApplier();
        CollectingPatchApplier referenced = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();
        PatchDeclarationScanner.ScanType(typeof(LateAddHigh), constrained, props);

        try {
            Assert.Equal(2, OrderedTailTarget.Value());

            PatchDeclarationScanner.ScanType(typeof(LateMultiplyLow), referenced, props);
            Assert.Equal(20, OrderedTailTarget.Value());

            Assert.Single(referenced.Handles).Dispose();
            Assert.Equal(2, OrderedTailTarget.Value());
        } finally {
            foreach (IDetourHandle handle in referenced.Handles) {
                handle.Dispose();
            }

            foreach (IDetourHandle handle in constrained.Handles) {
                handle.Dispose();
            }
        }
    }

    [Fact]
    public void EqualPriority_LaterAppliedInjectionRunsFirst() {
        MethodBase target = typeof(OrderedTailTarget).GetMethod(nameof(OrderedTailTarget.Value))!;
        MethodBase add = typeof(EqualPriorityAdd).GetMethod(nameof(EqualPriorityAdd.AddOne))!;
        MethodBase multiply = typeof(EqualPriorityMultiply).GetMethod(nameof(EqualPriorityMultiply.MultiplyTen))!;
        IPatchHandle first = Patcher.Patch(target, add, At.Tail);
        IPatchHandle second = Patcher.Patch(target, multiply, At.Tail);

        try {
            Assert.Equal(11, OrderedTailTarget.Value());
        } finally {
            second.Dispose();
            first.Dispose();
        }
    }
}
