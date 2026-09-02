using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class OwnersOrderTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }
}

public static class OwnersDisposeTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 2;
    }
}

public static class OwnersUnpatchedTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 3;
    }
}

public static class OwnersInjections {
    public static void Bump(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public class PatchOwnersTests {
    [Fact]
    public void OwnersOf_UnpatchedMethod_IsEmpty() {
        Assert.Empty(Patcher.OwnersOf(TargetOf(typeof(OwnersUnpatchedTarget))));
    }

    [Fact]
    public void OwnersOf_ListsEachOwnerOnceInRunOrder() {
        MethodBase target = TargetOf(typeof(OwnersOrderTarget));

        using IPatchHandle early = Patcher.PatchInjection(target, Bump("mod.early", 0));
        using IPatchHandle late = Patcher.PatchInjection(target, Bump("mod.late", 10));
        using IPatchHandle alsoEarly = Patcher.PatchInjection(target, Bump("mod.early", 1));

        Assert.Equal(["mod.early", "mod.late"], Patcher.OwnersOf(target));
    }

    [Fact]
    public void OwnersOf_DropsAnOwnerWhenItsHandleIsDisposed() {
        MethodBase target = TargetOf(typeof(OwnersDisposeTarget));

        using IPatchHandle kept = Patcher.PatchInjection(target, Bump("mod.kept", 0));
        IPatchHandle removed = Patcher.PatchInjection(target, Bump("mod.removed", 1));
        Assert.Equal(["mod.kept", "mod.removed"], Patcher.OwnersOf(target));

        removed.Dispose();

        Assert.Equal(["mod.kept"], Patcher.OwnersOf(target));
    }

    private static MethodBase TargetOf(Type type) {
        return type.GetMethod("Value")!;
    }

    private static Injection Bump(string owner, int priority) {
        MethodInfo bump = typeof(OwnersInjections).GetMethod(nameof(OwnersInjections.Bump))!;
        return new Injection(bump, new InjectAt.Tail(), owner, priority);
    }
}
