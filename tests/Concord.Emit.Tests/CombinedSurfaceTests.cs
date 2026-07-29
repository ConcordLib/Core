using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class CombinedLog {
    public static void Begin() { }

    public static void End() { }
}

public sealed class CombinedOrder {
    public CombinedOrder(int id, string tag) {
        Id = id;
        Tag = tag;
    }

    public int Id { get; }

    public string Tag { get; }
}

public class CombinedHost {
    public int Run(int seed) {
        CombinedOrder outside = new CombinedOrder(seed, "outside");
        CombinedLog.Begin();
        CombinedOrder inside = new CombinedOrder(seed + 1, "inside");
        CombinedLog.End();
        return outside.Id + inside.Id;
    }
}

public static class CombinedRecorder {
    public static int Hits;

    public static string CapturedTag = string.Empty;

    public static string SlotTag = string.Empty;

    public static int CapturedId;

    public static long SlotSeed;
}

public static class CombinedCaptureIntoStateDeclaration {
    public static void AtConstruction(ControlHandle ch, [Capture(2)] string tag) {
        CombinedRecorder.Hits++;
        CombinedRecorder.CapturedTag = tag;
        ch.SetState(tag);
    }

    public static void AtTail(ControlHandle<int> ch) {
        CombinedRecorder.SlotTag = ch.GetState<string>();
    }
}

public static class CombinedStateOnlyDeclaration {
    public static void AtHead(ControlHandle<int> ch, int seed) {
        ch.SetState(seed + 100L);
    }

    public static void AtTail(ControlHandle<int> ch) {
        CombinedRecorder.SlotSeed = ch.GetState<long>();
    }
}

public static class CombinedCaptureOnlyDeclaration {
    public static void AtConstruction([Capture(1)] int id) {
        CombinedRecorder.CapturedId = id;
    }
}

public sealed class CombinedSurfaceTests {
    [Fact]
    public void SliceBoundedConstructionCapture_FeedsTheDeclarationsStateSlot() {
        MethodBase target = typeof(CombinedHost).GetMethod(nameof(CombinedHost.Run))!;
        SliceRange range = new SliceRange(typeof(CombinedLog), nameof(CombinedLog.Begin), 1, typeof(CombinedLog), nameof(CombinedLog.End), 1);
        InjectAt.NewObj at = new InjectAt.NewObj(typeof(CombinedOrder), At.Tail, 1) { Slice = range };
        Injection construction = new Injection(
            typeof(CombinedCaptureIntoStateDeclaration).GetMethod(nameof(CombinedCaptureIntoStateDeclaration.AtConstruction))!,
            at,
            "combined",
            0);
        Injection tail = new Injection(
            typeof(CombinedCaptureIntoStateDeclaration).GetMethod(nameof(CombinedCaptureIntoStateDeclaration.AtTail))!,
            new InjectAt.Tail(),
            "combined",
            1);

        ComposeResult result = WrapperComposer.Compose(target, [construction, tail]);
        Func<CombinedHost, int, int> run = result.Wrapper.CreateDelegate<Func<CombinedHost, int, int>>();

        CombinedRecorder.Hits = 0;
        CombinedRecorder.CapturedTag = string.Empty;
        CombinedRecorder.SlotTag = string.Empty;
        int returned = run(new CombinedHost(), 5);

        Assert.Equal(11, returned);
        Assert.Equal(1, CombinedRecorder.Hits);
        Assert.Equal("inside", CombinedRecorder.CapturedTag);
        Assert.Equal("inside", CombinedRecorder.SlotTag);
    }

    [Fact]
    public void AStateDeclarationAndACaptureDeclaration_ShareATargetWithoutInterfering() {
        MethodBase target = typeof(CombinedHost).GetMethod(nameof(CombinedHost.Run))!;
        Injection head = new Injection(
            typeof(CombinedStateOnlyDeclaration).GetMethod(nameof(CombinedStateOnlyDeclaration.AtHead))!,
            new InjectAt.Head(),
            "state",
            0);
        Injection construction = new Injection(
            typeof(CombinedCaptureOnlyDeclaration).GetMethod(nameof(CombinedCaptureOnlyDeclaration.AtConstruction))!,
            new InjectAt.NewObj(typeof(CombinedOrder), At.Tail, 1),
            "capture",
            1);
        Injection tail = new Injection(
            typeof(CombinedStateOnlyDeclaration).GetMethod(nameof(CombinedStateOnlyDeclaration.AtTail))!,
            new InjectAt.Tail(),
            "state",
            2);

        ComposeResult result = WrapperComposer.Compose(target, [head, construction, tail]);
        Func<CombinedHost, int, int> run = result.Wrapper.CreateDelegate<Func<CombinedHost, int, int>>();

        CombinedRecorder.CapturedId = 0;
        CombinedRecorder.SlotSeed = 0L;
        int returned = run(new CombinedHost(), 5);

        Assert.Equal(11, returned);
        Assert.Equal(5, CombinedRecorder.CapturedId);
        Assert.Equal(105L, CombinedRecorder.SlotSeed);
    }
}
