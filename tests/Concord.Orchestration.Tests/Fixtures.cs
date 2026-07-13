using System.Reflection;
using Concord.Detour;
using Concord.Emit;

namespace Concord.Orchestration.Tests;

public class GameBase {
    public virtual void Step() { }

    public int Score => 7;
}

public sealed class SealedGame {
    public int Value() {
        return 1;
    }
}

public class OtherBase {
    public virtual void Run() { }
}

[Patch]
public abstract class GoodDeclaration : GameBase {
    public int counter;

    [Inject(At.Head, nameof(Step))]
    public void OnStep(ControlHandle ch) { }
}

[Patch]
public abstract class PropertyTargetDeclaration : GameBase {
    [Inject(At.Tail, nameof(Score))]
    public void OnScore(ControlHandle<int> ch) { }
}

[Patch]
public abstract class FieldOnlyDeclaration : GameBase {
    public int marker;
}

#pragma warning disable CS0649
[Patch]
public abstract class InjectFieldDeclaration : GameBase {
    [InjectField("targetCounter")]
    public int injectedCounter;

    public int attachedCounter;
}
#pragma warning restore CS0649

[Patch]
public abstract class StaticFieldDeclaration : GameBase {
    private static readonly HashSet<string> Registered = [];

    public int marker;
}

public abstract class UnattributedDeclaration : GameBase {
    public int counter;

    [Inject(At.Head, nameof(Step))]
    public void OnStep(ControlHandle ch) { }
}

[Patch(typeof(SealedGame))]
public static class SealedTargetDeclaration {
    [Inject(At.Head, nameof(SealedGame.Value))]
    public static void OnValue(ControlHandle<int> ch) { }
}

[Patch(typeof(GameBase))]
public abstract class MismatchDeclaration : OtherBase { }

[Patch]
public abstract class NoGameBaseDeclaration {
    public int counter;
}

public sealed record PatchCall(MethodBase Target, Injection Injection);

public sealed class FakePatchApplier : IPatchApplier {
    public List<PatchCall> Calls { get; } = [];

    public void ApplyPatch(MethodBase target, Injection injection) {
        Calls.Add(new PatchCall(target, injection));
    }
}

public sealed record PropCall(Type BaseType, string Name, Type ValueType);

public sealed class FakeAttachedPropertyRegistry : IAttachedPropertyRegistry {
    public List<PropCall> Calls { get; } = [];

    public void RegisterAttachedProperty(Type baseType, string name, Type valueType) {
        Calls.Add(new PropCall(baseType, name, valueType));
    }
}

public sealed class FakeDetourHandle : IDetourHandle {
    private bool _isApplied = true;

    public MethodBase Original { get; }
    public bool IsApplied => _isApplied;

    public FakeDetourHandle(MethodBase original) {
        Original = original;
    }

    public void Dispose() {
        _isApplied = false;
    }
}

public sealed class RecordingApplier : IPatchApplier {
    public List<IDetourHandle> AppliedHandles { get; } = [];

    public void ApplyPatch(MethodBase target, Injection injection) {
        FakeDetourHandle handle = new FakeDetourHandle(target);
        AppliedHandles.Add(handle);
    }
}

public class HalfFailingTarget {
    public virtual void FirstMethod() { }
    public virtual void SecondMethod() { }
}

[Patch(typeof(HalfFailingTarget))]
public abstract class HalfFailingDeclaration : HalfFailingTarget {
    [Inject(At.Head, nameof(HalfFailingTarget.FirstMethod))]
    public void OnFirstValid(ControlHandle ch) { }

    [Inject(At.Head, "NonExistentMethod")]
    public void OnSecondInvalid(ControlHandle ch) { }
}
