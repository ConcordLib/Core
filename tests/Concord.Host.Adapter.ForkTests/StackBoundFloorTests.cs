using System.Reflection;
using System.Reflection.Emit;
using Concord.Host.Adapter.Tests;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Sliced {
    public static void Target() {
        Called();
    }

    public static void Called() {
    }

    public static void Spliced() {
    }
}

public sealed class StackBoundFloorTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so no re-encoded body can be published.";

    private const int Walked = 5;

    private const int Declared = 6;

    private static readonly MethodInfo Target =
        typeof(PatchBuilder).GetMethod(nameof(PatchBuilder.Slice), [typeof(SliceRange)])!;

    private static readonly FieldInfo Recorded =
        typeof(PatchBuilder).GetField("injections", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private bool published;

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        if (published) {
            ForkBodyWriter.Shared.Restore(Target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void TheWalkedBoundSitsBelowTheMaxstackThisBranchyBodyDeclares() {
        ComposedBody body = Read();

        Assert.Contains(body.Instructions, step => step.Instruction.opcode.FlowControl == FlowControl.Cond_Branch);
        Assert.Equal(Walked, StackBound.Compute(body).Bound);
        Assert.Equal(Declared, Target.GetMethodBody()!.MaxStackSize);
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void TheDeepestPathRunsAtTheWalkedBoundAndIsRefusedOneBelowIt() {
        Assert.Equal(Walked, Publish(new ComputedStack()));

        SliceRange range = Range();
        PatchBuilder deep = Builder();

        Assert.Same(deep, deep.Slice(range));
        Assert.Same(range, Assert.IsType<InjectAt.Invoke>(Latest(deep).At).Slice);

        Assert.Equal(Walked - 1, Publish(new ForkRuntime.FixedStack(Walked - 1)));

        Assert.Throws<InvalidProgramException>(() => Builder().Slice(Range()));
        Assert.Throws<InvalidProgramException>(() => Patcher.For(Target).Slice(Range()));

        ForkBodyWriter.Shared.Restore(Target);
        published = false;

        Assert.Throws<Orchestration.ConcordDeclarationException>(() => Patcher.For(Target).Slice(Range()));
    }

    private static ComposedBody Read() {
        ComposedBody? body = RealBodies.Read(Target, out string? skipped);
        Assert.Null(skipped);
        return body!;
    }

    private static PatchBuilder Builder() {
        return Patcher.For(typeof(Sliced), nameof(Sliced.Target))
            .Invoke(
                typeof(Sliced),
                nameof(Sliced.Called),
                typeof(Sliced).GetMethod(nameof(Sliced.Spliced))!,
                At.Head);
    }

    private static SliceRange Range() {
        return new SliceRange(typeof(Sliced), nameof(Sliced.Called), 1, null, null, 0);
    }

    private static Injection Latest(PatchBuilder builder) {
        return ((List<Injection>)Recorded.GetValue(builder)!)[^1];
    }

    private int Publish(IMaxStackSource stack) {
        EncodedBody encoded = BodyEncoder.Encode(Read(), new WaylithTokenSource(Target.Module), stack);
        ForkBodyWriter.Shared.Write(Target, encoded.Bytes, []);
        published = true;
        return encoded.MaxStack;
    }
}
