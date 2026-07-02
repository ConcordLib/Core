using System.Runtime.CompilerServices;
using Concord;

namespace Concord.Orchestration.Tests.RollbackAssembly;

/// <summary>A static target patched by a declaration that composes and applies cleanly.</summary>
public static class RollbackGoodTarget {
    /// <summary>Returns a fixed value the good declaration adds one to.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 7;
    }
}

/// <summary>A static target patched by a declaration whose injection method throws CONC012 at compose time.</summary>
public static class RollbackBadTarget {
    /// <summary>Returns a fixed value that is never reached once the bad declaration is scanned.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 13;
    }
}

/// <summary>Declared before <see cref="RollbackBadDeclaration" /> so it scans and applies first.</summary>
[Patch(typeof(RollbackGoodTarget))]
public static class RollbackGoodDeclaration {
    /// <summary>Adds one to the target's return value.</summary>
    /// <param name="ch">The control handle used to observe and adjust the return value.</param>
    [Inject(At.Tail, nameof(RollbackGoodTarget.Compute))]
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 1;
    }
}

/// <summary>Composing this declaration throws <see cref="Concord.Emit.ConcordEmitException" /> (CONC012).</summary>
[Patch(typeof(RollbackBadTarget))]
public static class RollbackBadDeclaration {
    /// <summary>Cancels the target without setting a return value, which CONC012 forbids for non-void targets.</summary>
    /// <param name="ch">The control handle used to cancel the target method.</param>
    [Inject(At.Head, nameof(RollbackBadTarget.Compute))]
    public static void CancelWithoutReturn(ControlHandle<int> ch) {
        ch.Cancel();
    }
}
