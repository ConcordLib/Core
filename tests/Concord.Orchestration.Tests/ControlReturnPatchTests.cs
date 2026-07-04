using System.Runtime.CompilerServices;
using Concord.Detour;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class ControlGateState {
    public static bool Closed;
}

public class ControlGateTarget {
    public int Hits;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Bump() {
        Hits++;
    }
}

[Patch(typeof(ControlGateTarget))]
public static class ControlGateDeclaration {
    [Inject(At.Head, nameof(ControlGateTarget.Bump))]
    public static Control BeforeBump() {
        return ControlGateState.Closed ? Control.Cancel : Control.Continue;
    }
}

public class ControlFluentTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int GetValue() {
        return 5;
    }
}

public abstract class ControlFluentPatch : ControlFluentTarget {
    public Control BeforeGetValue(ControlHandle<int> ch) {
        ch.ReturnValue = 42;
        return Control.Cancel;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public class ControlReturnPatchTests {
    [Fact]
    public void AttributeForm_ControlReturn_GatesTargetLive() {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();
        PatchDeclarationScanner.ScanType(typeof(ControlGateDeclaration), applier, props);

        try {
            ControlGateTarget target = new ControlGateTarget();

            ControlGateState.Closed = false;
            target.Bump();
            Assert.Equal(1, target.Hits);

            ControlGateState.Closed = true;
            target.Bump();
            Assert.Equal(1, target.Hits);
        } finally {
            ControlGateState.Closed = false;
            foreach (IDetourHandle handle in applier.Handles) {
                handle.Dispose();
            }
        }
    }

    [Fact]
    public void FluentForm_ControlReturn_CancelsWithValueLive() {
        IPatchHandle handle = Patcher.For<ControlFluentTarget>(nameof(ControlFluentTarget.GetValue))
            .Head(typeof(ControlFluentPatch), nameof(ControlFluentPatch.BeforeGetValue))
            .Apply();
        try {
            Assert.Equal(42, new ControlFluentTarget().GetValue());
        } finally {
            handle.Dispose();
        }

        Assert.Equal(5, new ControlFluentTarget().GetValue());
    }
}
