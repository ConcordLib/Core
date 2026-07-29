// These declarations exist to compile documented injection-surface usage with Concord.Analyzers
// enabled. TreatWarningsAsErrors turns any diagnostic the analyzer reports on them into a build
// failure, which is the point: the analyzer's own suite compiles against a hand-written replica of
// the attributes, so only a build like this one catches the analyzer rejecting correct code.
// EndToEndTests applies this whole assembly, so every declaration here must also compose.
namespace Concord.Generators.Tests.Consumer;

/// <summary>Anchors the [Slice] fixture bounds its search between.</summary>
public static class SurfaceFence {
    /// <summary>Opens the sliced region.</summary>
    public static void Open() { }

    /// <summary>Closes the sliced region.</summary>
    public static void Close() { }
}

/// <summary>The call the invoke fixtures match, whose arguments the [Capture] fixture binds.</summary>
public static class SurfaceLedger {
    /// <summary>Records an amount under a label.</summary>
    /// <param name="amount">The amount recorded.</param>
    /// <param name="label">The label recorded against.</param>
    /// <returns><paramref name="amount" />, unchanged.</returns>
    public static int Record(int amount, string label) {
        return amount;
    }
}

/// <summary>The construction the [InjectNew] fixture targets.</summary>
public sealed class SurfaceReceipt {
    /// <summary>Records the amount and label the receipt was issued for.</summary>
    /// <param name="amount">The recorded amount.</param>
    /// <param name="label">The recorded label.</param>
    public SurfaceReceipt(int amount, string label) {
        Amount = amount;
        Label = label;
    }

    /// <summary>The recorded amount.</summary>
    public int Amount { get; }

    /// <summary>The recorded label.</summary>
    public string Label { get; }
}

/// <summary>The target every injection-surface fixture patches.</summary>
public static class SurfaceRegister {
    /// <summary>Records twice, once outside the fence and once inside it, then issues a receipt.</summary>
    /// <param name="seed">The amount to record.</param>
    /// <returns>The issued receipt's amount.</returns>
    public static int Ring(int seed) {
        SurfaceLedger.Record(seed, "outside");
        SurfaceFence.Open();
        int inside = SurfaceLedger.Record(seed + 1, "inside");
        SurfaceReceipt receipt = new SurfaceReceipt(inside, "inside");
        SurfaceFence.Close();
        return receipt.Amount;
    }
}

/// <summary>An invoke-Tail injection binding one argument of the matched call.</summary>
[Patch(typeof(SurfaceRegister))]
public static class SurfaceCapturePatch {
    /// <summary>The label the capture bound, written by the injection.</summary>
    public static string CapturedLabel = string.Empty;

    [Inject(nameof(SurfaceRegister.Ring), typeof(SurfaceLedger), nameof(SurfaceLedger.Record), At.Tail, 1)]
    private static void OnRecord([Capture(2)] string label) {
        CapturedLabel = label;
    }
}

/// <summary>A construction-site injection.</summary>
[Patch(typeof(SurfaceRegister))]
public static class SurfaceNewPatch {
    /// <summary>How many receipts the target constructed, written by the injection.</summary>
    public static int Constructions;

    [InjectNew(nameof(SurfaceRegister.Ring), typeof(SurfaceReceipt), At.Head, 1)]
    private static void OnReceipt() {
        Constructions++;
    }
}

/// <summary>An invoke injection whose search is bounded to the fenced region.</summary>
[Patch(typeof(SurfaceRegister))]
public static class SurfaceSlicePatch {
    /// <summary>How many fenced records ran, written by the injection.</summary>
    public static int FencedRecords;

    [Inject(nameof(SurfaceRegister.Ring), typeof(SurfaceLedger), nameof(SurfaceLedger.Record), At.Head, 1)]
    [Slice(typeof(SurfaceFence), nameof(SurfaceFence.Open), 1, typeof(SurfaceFence), nameof(SurfaceFence.Close), 1)]
    private static void OnFencedRecord() {
        FencedRecords++;
    }
}

/// <summary>A head write and a tail read of one declaration's state slot.</summary>
[Patch(typeof(SurfaceRegister))]
public static class SurfaceStatePatch {
    /// <summary>The value the tail injection read back out of the slot.</summary>
    public static long Observed;

    [Inject(At.Head, nameof(SurfaceRegister.Ring))]
    private static void Enter(ControlHandle<int> ch, int seed) {
        ch.SetState(seed + 100L);
    }

    [Inject(At.Tail, nameof(SurfaceRegister.Ring))]
    private static void Leave(ControlHandle<int> ch) {
        Observed = ch.GetState<long>();
    }
}
