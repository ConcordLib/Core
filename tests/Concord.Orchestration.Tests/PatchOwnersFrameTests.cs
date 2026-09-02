using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class OwnersFrameTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 4;
    }
}

public static class OwnersFrameRevertTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 5;
    }
}

public static class OwnersFrameInjections {
    public static StackFrame? Captured { get; set; }

    // Runs composed into the wrapper, so frame 0 is the wrapper itself: the same view a mod gets when it
    // logs from inside a patched method.
    public static void Capture(ControlHandle<int> ch) {
        Captured = new StackTrace().GetFrame(0);
        ch.ReturnValue += 1;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public class PatchOwnersFrameTests {
    [Fact]
    public void OwnersOfFrame_NamesTheOwner_WhenTheFrameIsTheComposedWrapper() {
        MethodBase target = TargetOf(typeof(OwnersFrameTarget));
        OwnersFrameInjections.Captured = null;

        using IPatchHandle handle = Patcher.PatchInjection(target, Capture("mod.frame"));

        Assert.Equal(5, OwnersFrameTarget.Value());

        StackFrame frame = OwnersFrameInjections.Captured!;
        Assert.Empty(Patcher.OwnersOf(frame.GetMethod()!));
        Assert.Equal(["mod.frame"], Patcher.OwnersOfFrame(frame));
    }

    [Fact]
    public void ResolveOriginal_ReturnsTheTarget_AndForgetsItOnRevert() {
        MethodBase target = TargetOf(typeof(OwnersFrameRevertTarget));
        OwnersFrameInjections.Captured = null;

        IPatchHandle handle = Patcher.PatchInjection(target, Capture("mod.revert"));
        OwnersFrameRevertTarget.Value();
        MethodBase wrapper = OwnersFrameInjections.Captured!.GetMethod()!;

        Assert.Equal(target, MethodIdentity.ResolveOriginal(wrapper));

        handle.Dispose();

        Assert.Null(MethodIdentity.ResolveOriginal(wrapper));
    }

    [Fact]
    public void ResolveOriginal_IsNullForAMethodConcordNeverPatched() {
        Assert.Null(MethodIdentity.ResolveOriginal(TargetOf(typeof(OwnersFrameRevertTarget))));
    }

    private static MethodBase TargetOf(Type type) {
        return type.GetMethod("Value")!;
    }

    private static Injection Capture(string owner) {
        MethodInfo capture = typeof(OwnersFrameInjections).GetMethod(nameof(OwnersFrameInjections.Capture))!;
        return new Injection(capture, new InjectAt.Tail(), owner, 0);
    }
}
